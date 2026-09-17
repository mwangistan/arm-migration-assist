using System.Net;
using System.Text;
using System.Text.Json;
using MigrationPlanner.Application.Reporting;

namespace MigrationPlanner.Infrastructure.Reporting;

internal static class ReportRendering
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static JsonDocument ReadPlan(MigrationReport report) =>
        JsonDocument.Parse(JsonSerializer.Serialize(report.Plan, JsonOptions));

    public static string ReadString(JsonElement root, string name, string fallback = "Not provided") =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    public static IEnumerable<JsonElement> ReadArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [];

    public static string JoinStrings(JsonElement root, string name) =>
        string.Join(", ", ReadArray(root, name)
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()));

    public static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

    public static string Markdown(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    public static string Html(string value) => WebUtility.HtmlEncode(value);

    public static MemoryStream Stream(StringBuilder content)
    {
        var bytes = Encoding.UTF8.GetBytes(content.ToString());
        return new MemoryStream(bytes, writable: false);
    }
}
