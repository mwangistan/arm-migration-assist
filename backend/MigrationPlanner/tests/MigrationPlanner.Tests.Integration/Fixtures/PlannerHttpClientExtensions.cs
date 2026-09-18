using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MigrationPlanner.Tests.Integration.Fixtures;

// Shared helper for tests that hit POST /api/migration-plans. The endpoint returns 202
// with a runId that must be polled at /api/migration-plans/runs/{runId} until the run
// reaches a terminal state. Returns a synthetic HttpResponseMessage whose body mirrors
// the pre-async response shape so tests that previously asserted on the sync 200 body
// can remain unchanged.
internal static class PlannerHttpClientExtensions
{
    public static async Task<HttpResponseMessage> PostAndAwaitPlanAsync<T>(
        this HttpClient client,
        string url,
        T payload,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null)
    {
        var initial = await client.PostAsJsonAsync(url, payload);
        if (initial.StatusCode != HttpStatusCode.Accepted)
        {
            return initial;
        }

        using var doc = await initial.Content.ReadFromJsonAsync<JsonDocument>();
        var runId = doc!.RootElement.GetProperty("runId").GetString();
        if (string.IsNullOrEmpty(runId))
        {
            throw new InvalidOperationException("Planner returned 202 without a runId.");
        }

        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromMinutes(2));
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(200);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var pollResponse = await client.GetAsync($"/api/migration-plans/runs/{runId}");
            var bodyJson = await pollResponse.Content.ReadAsStringAsync();
            using var pollDoc = JsonDocument.Parse(bodyJson);
            var status = pollDoc.RootElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;

            if (status == "completed")
            {
                var forwardedStatus = HttpStatusCode.OK;
                return new HttpResponseMessage(forwardedStatus)
                {
                    Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json"),
                };
            }
            if (status == "failed")
            {
                var errorObj = pollDoc.RootElement.TryGetProperty("error", out var err) ? err : default;
                var statusCode = errorObj.ValueKind == JsonValueKind.Object && errorObj.TryGetProperty("statusCode", out var sc)
                    ? (HttpStatusCode)sc.GetInt32()
                    : HttpStatusCode.InternalServerError;
                var problemBody = errorObj.ValueKind == JsonValueKind.Object ? errorObj.GetRawText() : bodyJson;
                return new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(problemBody, System.Text.Encoding.UTF8, "application/problem+json"),
                };
            }

            await Task.Delay(interval);
        }

        throw new TimeoutException($"Planner run {runId} did not complete within the timeout.");
    }
}
