using System.Text;
using System.Text.Json;

namespace ArmMigrationAssist.Api.MigrationPlanner;

public sealed class MigrationPlannerClient(HttpClient httpClient)
{
    public async Task<MigrationPlannerResponse> CreatePlanAsync(
        JsonElement assessment,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/migration-plans")
        {
            Content = new StringContent(
                assessment.GetRawText(),
                Encoding.UTF8,
                "application/json")
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString()
            ?? "application/json";

        return new MigrationPlannerResponse(
            (int)response.StatusCode,
            content,
            contentType);
    }
}

public sealed record MigrationPlannerResponse(
    int StatusCode,
    string Content,
    string ContentType);
