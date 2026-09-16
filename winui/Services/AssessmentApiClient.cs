using System.Net.Http.Json;
using System.Text.Json;
using ArmMigrationAssist.WinUI.Models;

namespace ArmMigrationAssist.WinUI.Services;

public sealed class AssessmentApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    public async Task<AssessmentResponse> AssessAsync(
        Uri serviceBaseUri,
        Uri repositoryUri,
        string target,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(serviceBaseUri, "assess");
        using var response = await _httpClient.PostAsJsonAsync(
            endpoint,
            new { repoUrl = repositoryUri.AbsoluteUri.TrimEnd('/'), target },
            JsonOptions,
            cancellationToken);

        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AssessmentApiException(GetErrorMessage(rawJson, (int)response.StatusCode));

        var assessment = JsonSerializer.Deserialize<RepositoryAssessment>(rawJson, JsonOptions)
            ?? throw new AssessmentApiException("The assessment service returned an empty response.");

        return new AssessmentResponse(assessment, FormatJson(rawJson));
    }

    private static string GetErrorMessage(string payload, int statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                var message = error.GetString() ?? $"HTTP {statusCode}";
                if (root.TryGetProperty("details", out var details) &&
                    details.ValueKind == JsonValueKind.Array)
                {
                    message += " " + string.Join(
                        " ",
                        details.EnumerateArray().Select(item => item.GetString()));
                }

                return message;
            }

            if (root.TryGetProperty("detail", out var detail))
                return detail.GetString() ?? $"HTTP {statusCode}";
            if (root.TryGetProperty("title", out var title))
                return title.GetString() ?? $"HTTP {statusCode}";
        }
        catch (JsonException)
        {
            if (!string.IsNullOrWhiteSpace(payload))
                return payload;
        }

        return $"The assessment service returned HTTP {statusCode}.";
    }

    private static string FormatJson(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        return JsonSerializer.Serialize(document.RootElement, JsonOptions);
    }
}

public sealed record AssessmentResponse(RepositoryAssessment Assessment, string RawJson);

public sealed class AssessmentApiException(string message) : Exception(message);
