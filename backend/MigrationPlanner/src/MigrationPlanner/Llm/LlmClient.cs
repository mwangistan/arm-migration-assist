using Azure;
using Azure.AI.Inference;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace MigrationPlanner.Llm;

public sealed class LlmClient
{
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

    private readonly ChatCompletionsClient _client;
    private readonly LlmOptions _options;
    private readonly ILogger<LlmClient> _logger;

    public LlmClient(LlmOptions options, ILogger<LlmClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException("LlmOptions.Endpoint must be configured.");
        }

        _options = options;
        _logger = logger;

        var endpoint = new Uri(options.Endpoint.TrimEnd('/'));
        _client = string.IsNullOrWhiteSpace(options.ApiKey)
            ? new ChatCompletionsClient(endpoint, new ScopedTokenCredential(new DefaultAzureCredential(), CognitiveServicesScope))
            : new ChatCompletionsClient(endpoint, new AzureKeyCredential(options.ApiKey));
    }

    public string DeploymentName => _options.DeploymentName;

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
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

        var response = await _client.CompleteAsync(options, timeoutCts.Token).ConfigureAwait(false);

        _logger.LogInformation(
            "llm_completion deployment={Deployment} promptTokens={Prompt} completionTokens={Completion}",
            _options.DeploymentName,
            response.Value.Usage?.PromptTokens,
            response.Value.Usage?.CompletionTokens);

        return response.Value.Content ?? string.Empty;
    }
}

/// <summary>
/// TokenCredential wrapper that restricts every token request to a specific
/// scope. Prevents the model inference call from accidentally requesting a
/// broader scope than intended.
/// </summary>
internal sealed class ScopedTokenCredential : TokenCredential
{
    private readonly TokenCredential _inner;
    private readonly string _scope;

    public ScopedTokenCredential(TokenCredential inner, string scope)
    {
        _inner = inner;
        _scope = scope;
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        _inner.GetToken(new TokenRequestContext(new[] { _scope }), cancellationToken);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        _inner.GetTokenAsync(new TokenRequestContext(new[] { _scope }), cancellationToken);
}
