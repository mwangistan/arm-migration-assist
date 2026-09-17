using Azure;
using Azure.AI.Inference;
using Azure.Core;
using Azure.Identity;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;
using Microsoft.Extensions.Logging;

namespace MigrationPlanner.Infrastructure.Model;

/// <summary>
/// Hosted <see cref="IPlannerModel"/> backed by an Azure AI Foundry Phi
/// deployment. Uses the Azure AI Model Inference API via
/// <c>Azure.AI.Inference</c>. Emits the guidance index in the prompt because
/// Phi does not reliably support OpenAI-style function calling; the model must
/// cite <c>guidanceId</c> values, which the plan validator resolves post-hoc.
/// </summary>
public sealed class PhiPlannerModel : IPlannerModel
{
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

    private readonly ChatCompletionsClient _client;
    private readonly PhiModelOptions _options;
    private readonly ILogger<PhiPlannerModel> _logger;

    public PhiPlannerModel(PhiModelOptions options, ILogger<PhiPlannerModel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException(
                "PhiModelOptions.Endpoint must be configured when the Phi provider is selected.");
        }

        _options = options;
        _logger = logger;

        var endpoint = new Uri(options.Endpoint.TrimEnd('/'));
        _client = string.IsNullOrWhiteSpace(options.ApiKey)
            ? new ChatCompletionsClient(endpoint, new ScopedTokenCredential(new DefaultAzureCredential(), CognitiveServicesScope))
            : new ChatCompletionsClient(endpoint, new AzureKeyCredential(options.ApiKey));
    }

    public async Task<PlannerModelResult> GeneratePlanJsonAsync(
        RepositoryAssessmentV1 assessment,
        ReadinessScoreV1 score,
        IGuidanceLookup guidanceLookup,
        CancellationToken cancellationToken,
        PlannerRetryHint? retryHint = null)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(guidanceLookup);

        var systemPrompt = PlannerPromptBuilder.BuildSystemPrompt();
        var provenance = new PlannerProvenance(_options.ProvenanceProvider, _options.ProvenanceName, _options.ProvenanceVersion);
        var userPrompt = PlannerPromptBuilder.BuildUserPrompt(assessment, score, guidanceLookup, provenance, retryHint);

        var options = new ChatCompletionsOptions
        {
            Model = _options.DeploymentName,
            MaxTokens = _options.MaxOutputTokens,
            Temperature = _options.Temperature,
            ResponseFormat = new ChatCompletionsResponseFormatJsonObject(),
        };
        options.Messages.Add(new ChatRequestSystemMessage(systemPrompt));
        options.Messages.Add(new ChatRequestUserMessage(userPrompt));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.RequestTimeout);

        Response<ChatCompletions> response;
        try
        {
            response = await _client.CompleteAsync(options, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Phi inference failed status={Status} errorCode={Code}", ex.Status, ex.ErrorCode);
            if (ex.Status == 429)
            {
                throw new ModelRateLimitedException(
                    message: $"Phi-4 rate limit reached: {ex.ErrorCode}",
                    retryAfter: ExtractRetryAfter(ex),
                    inner: ex);
            }
            throw;
        }

        var content = response.Value.Content ?? string.Empty;

        _logger.LogInformation(
            "phi_completion assessmentId={AssessmentId} promptTokens={Prompt} completionTokens={Completion} totalTokens={Total}",
            assessment.AssessmentId,
            response.Value.Usage?.PromptTokens,
            response.Value.Usage?.CompletionTokens,
            response.Value.Usage?.TotalTokens);

        return PlannerModelResult.FromJson(ExtractJsonObject(content));
    }

    /// <summary>Strips markdown code fences and any preamble Phi may emit around its JSON body.</summary>
    private static string ExtractJsonObject(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var first = content.IndexOf('{');
        var last = content.LastIndexOf('}');
        return first >= 0 && last > first ? content[first..(last + 1)] : content;
    }

    private static TimeSpan? ExtractRetryAfter(RequestFailedException ex)
    {
        var raw = ex.GetRawResponse();
        if (raw is null) return null;

        // Retry-After can be seconds (integer) or an HTTP-date; retry-after-ms is milliseconds.
        if (raw.Headers.TryGetValue("retry-after-ms", out var ms)
            && double.TryParse(ms, out var msVal) && msVal > 0)
        {
            return TimeSpan.FromMilliseconds(msVal);
        }
        if (raw.Headers.TryGetValue("Retry-After", out var seconds)
            && double.TryParse(seconds, out var secVal) && secVal > 0)
        {
            return TimeSpan.FromSeconds(secVal);
        }
        return null;
    }
}
