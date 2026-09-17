using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutomatedMigration.Api.Contracts;

namespace AutomatedMigration.Api.Validation;

public interface IValidationDispatcher
{
    // Creates an F4 validation plan for the (patched) worktree, then triggers a run.
    // Returns null when no dispatch was attempted (no target URL configured).
    Task<ValidationDispatchDto?> DispatchAsync(
        AutomatedMigration.Models.MigrationPlan plan,
        string worktreePath,
        string branchName,
        string branchHeadSha,
        CancellationToken cancellationToken);
}

public sealed class ValidationDispatcherOptions
{
    public const string SectionName = "ValidationDispatcher";
    public string? BaseUrl { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

internal sealed class HttpValidationDispatcher : IValidationDispatcher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _client;
    private readonly ValidationDispatcherOptions _options;
    private readonly ILogger<HttpValidationDispatcher> _logger;

    public HttpValidationDispatcher(HttpClient client, ValidationDispatcherOptions options, ILogger<HttpValidationDispatcher> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<ValidationDispatchDto?> DispatchAsync(
        AutomatedMigration.Models.MigrationPlan plan,
        string worktreePath,
        string branchName,
        string branchHeadSha,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return null;
        }

        // F4 requires all validation-check groups to be non-null lists. Synthesize an empty
        // ValidationPlan when the incoming F2 plan didn't include one so the dispatch still
        // produces a plan with F4-side workflow (which will simply have zero criteria then).
        var validationPlan = plan.ValidationPlan ?? new AutomatedMigration.Models.ValidationPlan(
            Array.Empty<string>(), Array.Empty<AutomatedMigration.Models.ValidationCheck>(),
            Array.Empty<AutomatedMigration.Models.ValidationCheck>(), Array.Empty<AutomatedMigration.Models.ValidationCheck>(),
            Array.Empty<AutomatedMigration.Models.ValidationCheck>(), Array.Empty<AutomatedMigration.Models.ValidationCheck>(),
            Array.Empty<AutomatedMigration.Models.ValidationCheck>(), Array.Empty<AutomatedMigration.Models.ValidationCheck>(),
            Array.Empty<AutomatedMigration.Models.ValidationCheck>());

        // F4 requires each work item to declare AcceptanceTests. F2's plan usually does; if any
        // are missing, coerce to empty rather than null so RequestValidation.HasRequiredMigrationFields
        // accepts the request.
        var workItems = plan.WorkItems.Select(w => new
        {
            id = w.Id,
            acceptanceTests = w.AcceptanceTests ?? Array.Empty<AutomatedMigration.Models.AcceptanceTest>(),
        }).ToArray();

        var evidenceDirectory = Path.Combine(Path.GetTempPath(), "arm-migration-evidence", $"amma-{Guid.NewGuid():N}");
        Directory.CreateDirectory(evidenceDirectory);

        var payload = new
        {
            migrationPlan = new
            {
                schemaVersion = plan.SchemaVersion,
                planId = plan.PlanId,
                validationPlan,
                workItems,
            },
            target = new
            {
                path = worktreePath,
                commitSha = branchHeadSha,
                branch = branchName,
            },
            options = new
            {
                evidenceDirectory,
            },
            includeProposal = false,
        };

        try
        {
            using var response = await _client
                .PostAsJsonAsync("/api/v1/validation/plans", payload, Json, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Validation plan dispatch returned {StatusCode}: {Body}", (int)response.StatusCode, body);
                return new ValidationDispatchDto(null, null, null, Dispatched: false, Error: $"F4 responded {(int)response.StatusCode}");
            }

            var envelope = await response.Content.ReadFromJsonAsync<PlanEnvelope>(Json, cancellationToken).ConfigureAwait(false);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.PlanId))
            {
                return new ValidationDispatchDto(null, null, null, Dispatched: false, Error: "F4 returned an empty plan envelope");
            }

            // Trigger the run immediately. Approval defaults to auto-approve when no explicit
            // approval was stored (F4's AutoApprove path in ValidationApiHost).
            var runResp = await _client
                .PostAsync($"/api/v1/validation/plans/{envelope.PlanId}/runs", content: null, cancellationToken)
                .ConfigureAwait(false);
            string? runId = null;
            string? statusUrl = null;
            string? error = null;
            if (runResp.IsSuccessStatusCode)
            {
                var runEnvelope = await runResp.Content.ReadFromJsonAsync<RunEnvelope>(Json, cancellationToken).ConfigureAwait(false);
                runId = runEnvelope?.RunId;
                statusUrl = runEnvelope?.Links?.Status;
            }
            else
            {
                error = $"F4 run trigger responded {(int)runResp.StatusCode}";
            }

            return new ValidationDispatchDto(envelope.PlanId, runId, statusUrl, Dispatched: true, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Validation dispatch timed out.");
            return new ValidationDispatchDto(null, null, null, Dispatched: false, Error: "timed out");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Validation dispatch failed.");
            return new ValidationDispatchDto(null, null, null, Dispatched: false, Error: ex.Message);
        }
    }

    private sealed record PlanEnvelope(string? PlanId, string? Fingerprint);
    private sealed record RunEnvelope(string? RunId, RunLinks? Links);
    private sealed record RunLinks(string? Status, string? Report, string? Dashboard);
}

internal sealed class NoopValidationDispatcher : IValidationDispatcher
{
    public Task<ValidationDispatchDto?> DispatchAsync(
        AutomatedMigration.Models.MigrationPlan plan,
        string worktreePath,
        string branchName,
        string branchHeadSha,
        CancellationToken cancellationToken)
        => Task.FromResult<ValidationDispatchDto?>(null);
}
