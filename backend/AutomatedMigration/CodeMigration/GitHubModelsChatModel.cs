using System.Text;
using System.Text.Json;

namespace AutomatedMigration.CodeMigration;

// Calls GitHub Models (free, OpenAI-compatible) over plain HTTPS. The token is
// read from the environment by the caller and only ever used as a bearer header
// here — never logged or persisted.
public sealed class GitHubModelsChatModel : IChatModel
{
    private const string Endpoint = "https://models.inference.ai.azure.com/chat/completions";
    private static readonly HttpClient Http = new();
    private readonly string _token;
    private readonly string _model;

    public GitHubModelsChatModel(string token, string model = "gpt-4o-mini")
    {
        _token = token;
        _model = model;
    }

    public string Complete(string systemPrompt, string userPrompt)
    {
        var payload = new
        {
            model = _model,
            temperature = 0,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("Authorization", $"Bearer {_token}");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = Http.Send(request);
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub Models request failed ({(int)response.StatusCode}).");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
