using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Api.Automation;

internal sealed class HttpAutomationDispatcher : IAutomationDispatcher
{
    private static readonly JsonSerializerOptions RequestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _client;
    private readonly ILogger<HttpAutomationDispatcher> _logger;

    public HttpAutomationDispatcher(HttpClient client, ILogger<HttpAutomationDispatcher> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AutomationDispatch?> DispatchAsync(
        MigrationPlanV1 plan,
        Repository repository,
        CancellationToken cancellationToken)
    {
        if (repository is null || string.IsNullOrWhiteSpace(repository.Url) || string.IsNullOrWhiteSpace(repository.CommitSha))
        {
            _logger.LogInformation("Automation dispatch skipped: repository url or commitSha missing.");
            return null;
        }

        var payload = new
        {
            plan,
            target = new { url = repository.Url, commitSha = repository.CommitSha }
        };

        try
        {
            using var response = await _client
                .PostAsJsonAsync("/api/migration-actions", payload, RequestJson, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Automation dispatch to F3 returned {StatusCode}; planner response will omit automation block.",
                    (int)response.StatusCode);
                return null;
            }

            var accepted = await response.Content
                .ReadFromJsonAsync<AcceptedEnvelope>(ResponseJson, cancellationToken)
                .ConfigureAwait(false);

            if (accepted is null || string.IsNullOrWhiteSpace(accepted.JobId))
            {
                _logger.LogWarning("Automation dispatch to F3 returned success but no jobId.");
                return null;
            }

            return new AutomationDispatch(accepted.JobId, accepted.Status ?? "accepted", accepted.StatusUrl ?? string.Empty);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Automation dispatch to F3 timed out; planner response will omit automation block.");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Automation dispatch to F3 failed; planner response will omit automation block.");
            return null;
        }
    }

    private sealed record AcceptedEnvelope(string JobId, string? Status, string? StatusUrl);
}
