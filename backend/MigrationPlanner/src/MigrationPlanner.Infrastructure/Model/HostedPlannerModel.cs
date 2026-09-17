using System.Text.Json;
using Azure;
using Azure.AI.Inference;
using Azure.Identity;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Mcp;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;
using Microsoft.Extensions.Logging;

namespace MigrationPlanner.Infrastructure.Model;

/// <summary>
/// Hosted <see cref="IPlannerModel"/> backed by an Azure AI Foundry
/// GPT-4-class deployment. Uses the same Azure AI Model Inference API path as
/// <see cref="PhiPlannerModel"/>; the deployment on the Foundry account picks
/// which model actually serves the request.
/// </summary>
public sealed class HostedPlannerModel : IPlannerModel
{
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

    private readonly ChatCompletionsClient _client;
    private readonly HostedModelOptions _options;
    private readonly IMcpToolRegistry _toolRegistry;
    private readonly ILogger<HostedPlannerModel> _logger;

    public HostedPlannerModel(HostedModelOptions options, IMcpToolRegistry toolRegistry, ILogger<HostedPlannerModel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(logger);
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException(
                "HostedModelOptions.Endpoint must be configured when the Hosted provider is selected.");
        }

        _options = options;
        _toolRegistry = toolRegistry;
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
        var userPrompt = PlannerPromptBuilder.BuildUserPrompt(
            assessment, score, guidanceLookup, provenance, retryHint,
            enableGuidanceLookupTool: _options.EnableGuidanceLookupTool);

        var options = new ChatCompletionsOptions
        {
            Model = _options.DeploymentName,
            MaxTokens = _options.MaxOutputTokens,
            Temperature = _options.Temperature,
            ResponseFormat = new ChatCompletionsResponseFormatJsonObject(),
        };
        options.Messages.Add(new ChatRequestSystemMessage(systemPrompt));
        options.Messages.Add(new ChatRequestUserMessage(userPrompt));

        if (_options.EnableGuidanceLookupTool)
        {
            options.Tools.Add(BuildGuidanceLookupToolDefinition());
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.RequestTimeout);

        Response<ChatCompletions>? response = null;
        var toolCallCount = 0;
        var toolCountsByName = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var round = 0; round <= _options.MaxToolRounds; round++)
        {
            try
            {
                response = await _client.CompleteAsync(options, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Hosted inference failed status={Status} errorCode={Code}", ex.Status, ex.ErrorCode);
                if (ex.Status == 429)
                {
                    throw new ModelRateLimitedException(
                        message: $"Hosted model rate limit reached: {ex.ErrorCode}",
                        retryAfter: ExtractRetryAfter(ex),
                        inner: ex);
                }
                throw;
            }

            _logger.LogInformation(
                "hosted_completion assessmentId={AssessmentId} deployment={Deployment} round={Round} finishReason={Finish} promptTokens={Prompt} completionTokens={Completion} totalTokens={Total}",
                assessment.AssessmentId,
                _options.DeploymentName,
                round,
                response.Value.FinishReason,
                response.Value.Usage?.PromptTokens,
                response.Value.Usage?.CompletionTokens,
                response.Value.Usage?.TotalTokens);

            var toolCalls = response.Value.ToolCalls;
            var hasToolCalls = toolCalls is not null && toolCalls.Count > 0;

            if (!hasToolCalls || round == _options.MaxToolRounds)
            {
                break;
            }

            options.Messages.Add(new ChatRequestAssistantMessage(response.Value));

            foreach (var call in toolCalls!)
            {
                toolCallCount++;
                var toolName = call.Function?.Name ?? call.Name ?? "unknown";
                toolCountsByName[toolName] = toolCountsByName.GetValueOrDefault(toolName) + 1;
                var resultJson = await ExecuteToolCallAsync(call, assessment.AssessmentId, timeoutCts.Token).ConfigureAwait(false);
                options.Messages.Add(new ChatRequestToolMessage(resultJson, call.Id));
            }
        }

        _logger.LogInformation(
            "hosted_tool_summary assessmentId={AssessmentId} toolCallCount={ToolCallCount}",
            assessment.AssessmentId, toolCallCount);

        var content = response!.Value.Content ?? string.Empty;
        var observations = BuildObservations(toolCountsByName);
        return new PlannerModelResult(ExtractJsonObject(content), observations);
    }

    private static IReadOnlyList<string> BuildObservations(IReadOnlyDictionary<string, int> toolCounts)
    {
        if (toolCounts.Count == 0)
        {
            return Array.Empty<string>();
        }
        var parts = toolCounts
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => $"{kvp.Key} \u00d7 {kvp.Value}");
        return new[] { "Model called MCP tool(s): " + string.Join(", ", parts) + "." };
    }

    private async Task<string> ExecuteToolCallAsync(
        ChatCompletionsToolCall call, string assessmentId, CancellationToken cancellationToken)
    {
        var name = call.Function?.Name ?? call.Name;
        var argsJson = call.Function?.Arguments ?? call.Arguments ?? "{}";

        _logger.LogInformation(
            "hosted_tool_call assessmentId={AssessmentId} toolCallId={ToolCallId} tool={Tool} argsLength={ArgsLength}",
            assessmentId, call.Id, name, argsJson.Length);

        if (!_toolRegistry.TryResolve(name, out var tool) || tool is null)
        {
            return JsonSerializer.Serialize(new { error = "unknown_tool", toolName = name });
        }

        JsonElement argsElement;
        try
        {
            argsElement = JsonDocument.Parse(argsJson).RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { error = "invalid_arguments_json", toolName = name });
        }

        McpToolResponse toolResponse;
        try
        {
            toolResponse = await tool.InvokeAsync(new McpToolRequest(name, argsElement), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "hosted_tool_failed tool={Tool}", name);
            return JsonSerializer.Serialize(new { error = "tool_execution_failed", toolName = name, message = ex.Message });
        }

        if (!toolResponse.IsSuccess)
        {
            return JsonSerializer.Serialize(new
            {
                error = toolResponse.ErrorCode ?? "tool_error",
                toolName = name,
                message = toolResponse.ErrorMessage,
            });
        }

        return toolResponse.Result?.GetRawText() ?? "{}";
    }

    private static ChatCompletionsToolDefinition BuildGuidanceLookupToolDefinition()
    {
        const string parameters = """
        {
          "type": "object",
          "properties": {
            "guidanceId": {
              "type": "string",
              "description": "Exact guidanceId as listed in the guidanceIndex. Returns that single snippet with its full body."
            },
            "topic": {
              "type": "string",
              "description": "Topic slug (e.g. 'arm64ec', 'packaging', 'build-toolchain', 'testing'). Returns all snippets for that topic.",
              "enum": ["arm64ec", "arm64-target", "packaging", "build-toolchain", "testing", "migration-overview", "runtime", "third-party"]
            }
          },
          "additionalProperties": false
        }
        """;

        return new ChatCompletionsToolDefinition(new FunctionDefinition(McpToolNames.LookupWindowsArmGuidance)
        {
            Description = "Fetch Windows on Arm guidance snippet bodies. Pass guidanceId to get one snippet with its full text, or topic to get all snippets for that topic. Use this before citing any guidanceId if you need to verify its content.",
            Parameters = BinaryData.FromString(parameters),
        });
    }

    /// <summary>Strips markdown code fences and any preamble the model may emit around its JSON body.</summary>
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
