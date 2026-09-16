using System.Text.Json;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Application.Mcp;
using MigrationPlanner.Domain.Guidance;

namespace MigrationPlanner.Infrastructure.Mcp.Tools;

/// <summary>
/// The only tool permitted to return Windows on Arm guidance content. Reads
/// exclusively from <see cref="IWindowsOnArmGuidanceStore"/> and rejects any
/// argument shape it does not understand.
/// </summary>
public sealed class LookupWindowsArmGuidanceTool : IMcpTool
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    private readonly IWindowsOnArmGuidanceStore _store;

    public LookupWindowsArmGuidanceTool(IWindowsOnArmGuidanceStore store)
    {
        _store = store;
    }

    public string Name => McpToolNames.LookupWindowsArmGuidance;

    public Task<McpToolResponse> InvokeAsync(McpToolRequest request, CancellationToken cancellationToken)
    {
        var results = new List<GuidanceSnippet>();

        if (request.Arguments.ValueKind == JsonValueKind.Object)
        {
            if (request.Arguments.TryGetProperty("guidanceId", out var idElement) &&
                idElement.ValueKind == JsonValueKind.String)
            {
                var id = idElement.GetString();
                if (!string.IsNullOrEmpty(id) && _store.TryGet(id, out var snippet) && snippet is not null)
                {
                    results.Add(snippet);
                }
            }
            else if (request.Arguments.TryGetProperty("topic", out var topicElement) &&
                     topicElement.ValueKind == JsonValueKind.String)
            {
                var topicText = topicElement.GetString();
                if (!string.IsNullOrEmpty(topicText) &&
                    TryParseTopic(topicText, out var topic))
                {
                    results.AddRange(_store.FindByTopic(topic));
                }
            }
            else
            {
                results.AddRange(_store.All());
            }
        }
        else
        {
            results.AddRange(_store.All());
        }

        var payload = new
        {
            corpusVersion = _store.CorpusVersion,
            snippets = results,
        };

        var json = JsonSerializer.SerializeToElement(payload, SerializerOptions);
        return Task.FromResult(McpToolResponse.Success(Name, json));
    }

    private static bool TryParseTopic(string value, out Topic topic)
    {
        try
        {
            var element = JsonDocument.Parse($"\"{value}\"").RootElement;
            topic = element.Deserialize<Topic>(SerializerOptions);
            return true;
        }
        catch (JsonException)
        {
            topic = default;
            return false;
        }
    }
}
