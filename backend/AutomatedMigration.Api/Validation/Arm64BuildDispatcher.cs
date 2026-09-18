using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutomatedMigration.Api.Contracts;

namespace AutomatedMigration.Api.Validation;

public interface IArm64BuildDispatcher
{
    // POSTs {sourceUrl, baseCommitSha, patches[]} to the ARM64 runner VM.
    // Returns null when no runner is configured (env var missing).
    Task<Arm64BuildDispatchDto?> DispatchAsync(
        AutomatedMigration.Models.MigrationPlan plan,
        string sourceUrl,
        string baseCommitSha,
        IReadOnlyList<AutomatedMigration.Api.Validation.Arm64PatchInput> patches,
        CancellationToken cancellationToken);
}

public sealed record Arm64PatchInput(string Id, string Diff);

public sealed class Arm64BuildDispatcherOptions
{
    public const string SectionName = "Arm64BuildDispatcher";
    public string? BaseUrl { get; set; }
    public string? BearerToken { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

internal sealed class HttpArm64BuildDispatcher : IArm64BuildDispatcher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _client;
    private readonly Arm64BuildDispatcherOptions _options;
    private readonly ILogger<HttpArm64BuildDispatcher> _logger;

    public HttpArm64BuildDispatcher(HttpClient client, Arm64BuildDispatcherOptions options, ILogger<HttpArm64BuildDispatcher> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<Arm64BuildDispatchDto?> DispatchAsync(
        AutomatedMigration.Models.MigrationPlan plan,
        string sourceUrl,
        string baseCommitSha,
        IReadOnlyList<Arm64PatchInput> patches,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return null;
        }

        var payload = new
        {
            sourceUrl,
            baseCommitSha,
            branch = (string?)null,
            patches = patches.Select(p => new { id = p.Id, diff = p.Diff }).ToArray(),
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/arm64/runs")
            {
                Content = JsonContent.Create(payload, options: Json),
            };
            if (!string.IsNullOrWhiteSpace(_options.BearerToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.BearerToken);
            }

            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Arm64 build dispatch returned {StatusCode}: {Body}", (int)response.StatusCode, body);
                return new Arm64BuildDispatchDto(null, null, Dispatched: false, Error: $"Runner responded {(int)response.StatusCode}");
            }

            var envelope = await response.Content.ReadFromJsonAsync<Envelope>(Json, cancellationToken).ConfigureAwait(false);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.JobId))
            {
                return new Arm64BuildDispatchDto(null, null, Dispatched: false, Error: "Runner returned an empty job envelope");
            }

            // Prefer the caller-facing URL when the runner is reachable at a public FQDN.
            var statusUrl = envelope.StatusUrl ?? $"/api/v1/arm64/runs/{envelope.JobId}";
            return new Arm64BuildDispatchDto(envelope.JobId, statusUrl, Dispatched: true, Error: null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Arm64 build dispatch timed out.");
            return new Arm64BuildDispatchDto(null, null, Dispatched: false, Error: "timed out");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Arm64 build dispatch failed.");
            return new Arm64BuildDispatchDto(null, null, Dispatched: false, Error: ex.Message);
        }
    }

    private sealed record Envelope(string? JobId, string? Status, string? StatusUrl);
}

internal sealed class NoopArm64BuildDispatcher : IArm64BuildDispatcher
{
    public Task<Arm64BuildDispatchDto?> DispatchAsync(
        AutomatedMigration.Models.MigrationPlan plan,
        string sourceUrl,
        string baseCommitSha,
        IReadOnlyList<Arm64PatchInput> patches,
        CancellationToken cancellationToken) => Task.FromResult<Arm64BuildDispatchDto?>(null);
}
