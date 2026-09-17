using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutomatedMigration.Api.Contracts;
using AutomatedMigration.Models;

namespace AutomatedMigration.Api.Publication;

// Fire-and-forget bridge to F4 after a successful publish. F3 stays responsive:
// when validation is unreachable, unconfigured, or errors, the dispatcher returns
// null and the publication result's `validation` block is left null.
public interface IValidationDispatcher
{
    Task<ValidationDispatch?> DispatchAsync(
        string forkFullName,
        string branchName,
        string commitSha,
        MigrationPlan plan,
        CancellationToken cancellationToken);
}

public sealed record ValidationDispatch(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("statusUrl")] string StatusUrl);

public sealed class ValidationDispatcherOptions
{
    public const string SectionName = "ValidationDispatcher";
    public string? BaseUrl { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

internal sealed class HttpValidationDispatcher : IValidationDispatcher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly ValidationDispatcherOptions _options;
    private readonly ILogger<HttpValidationDispatcher> _logger;

    public HttpValidationDispatcher(HttpClient client, ValidationDispatcherOptions options, ILogger<HttpValidationDispatcher> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<ValidationDispatch?> DispatchAsync(
        string forkFullName, string branchName, string commitSha, MigrationPlan plan, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return null;
        }
        var payload = new
        {
            migrationPlan = plan,
            source = new
            {
                url = $"https://github.com/{forkFullName}.git",
                branch = branchName,
                commitSha = string.IsNullOrWhiteSpace(commitSha) ? null : commitSha,
            },
            includeProposal = false,
        };

        try
        {
            using var response = await _client
                .PostAsJsonAsync("/api/v1/validation/plans/from-git", payload, Json, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Validation dispatch returned {StatusCode}: {Body}", (int)response.StatusCode, body);
                return null;
            }
            var envelope = await response.Content.ReadFromJsonAsync<Envelope>(Json, cancellationToken).ConfigureAwait(false);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.PlanId))
            {
                return null;
            }
            var statusUrl = _client.BaseAddress is null
                ? envelope.Links?.Self ?? string.Empty
                : new Uri(_client.BaseAddress, envelope.Links?.Self ?? string.Empty).ToString();
            return new ValidationDispatch(envelope.PlanId, envelope.Fingerprint ?? "", statusUrl);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Validation dispatch timed out.");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Validation dispatch failed.");
            return null;
        }
    }

    private sealed record Envelope(string PlanId, string? Fingerprint, EnvelopeLinks? Links);
    private sealed record EnvelopeLinks(string Self);
}
