using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ArmMigrationAssist.RepositoryDiscovery.GitHub;

internal sealed record GitHubRepositorySnapshot(
    string Name,
    string CommitSha,
    string DefaultBranch,
    ExtractedRepositoryArchive Archive);

internal interface IGitHubRepositorySource
{
    Task<GitHubRepositorySnapshot> DownloadAsync(
        string repositoryUrl,
        string temporaryRoot,
        bool useStoredCredentials,
        CancellationToken cancellationToken);
}

internal interface IGitHubCredentialProvider
{
    Task<string?> GetTokenAsync(CancellationToken cancellationToken);
}

internal sealed class GitHubRepositorySource : IGitHubRepositorySource
{
    private const long MaximumArchiveBytes = 268_435_456;
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly HttpClient httpClient;
    private readonly IGitHubCredentialProvider credentialProvider;

    public GitHubRepositorySource()
        : this(SharedHttpClient, new GitHubCredentialProvider())
    {
    }

    internal GitHubRepositorySource(
        HttpClient httpClient,
        IGitHubCredentialProvider credentialProvider)
    {
        this.httpClient = httpClient;
        this.credentialProvider = credentialProvider;
    }

    public async Task<GitHubRepositorySnapshot> DownloadAsync(
        string repositoryUrl,
        string temporaryRoot,
        bool useStoredCredentials,
        CancellationToken cancellationToken)
    {
        var repositoryUri = new Uri(repositoryUrl);
        var segments = repositoryUri.AbsolutePath.Trim('/').Split('/');
        var owner = segments[0];
        var repository = segments[1];
        var token = useStoredCredentials
            ? await credentialProvider.GetTokenAsync(cancellationToken)
            : null;
        if (useStoredCredentials && string.IsNullOrWhiteSpace(token))
        {
            throw new RepositoryAuthenticationRequiredException(
                "Git Credential Manager did not return a GitHub credential.");
        }

        using var metadata = await GetJsonAsync(
            $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}",
            token,
            useStoredCredentials,
            cancellationToken);
        var defaultBranch = ReadRequiredString(metadata.RootElement, "default_branch", 200);
        var name = ReadRequiredString(metadata.RootElement, "name", 200);

        using var commit = await GetJsonAsync(
            $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/commits/{Uri.EscapeDataString(defaultBranch)}",
            token,
            useStoredCredentials,
            cancellationToken);
        var commitSha = ReadRequiredString(commit.RootElement, "sha", 64).ToLowerInvariant();
        if (!IsCommitSha(commitSha))
        {
            throw new RepositoryDiscoveryException("GitHub returned an invalid commit identifier.");
        }

        var archivePath = Path.Combine(temporaryRoot, "repository.zip");
        var repositoryPath = Path.Combine(temporaryRoot, "repository");
        await DownloadArchiveAsync(
            $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/zipball/{commitSha}",
            archivePath,
            token,
            useStoredCredentials,
            cancellationToken);
        var archive = await RepositoryArchiveExtractor.ExtractAsync(
            archivePath,
            repositoryPath,
            cancellationToken);
        File.Delete(archivePath);

        return new GitHubRepositorySnapshot(name, commitSha, defaultBranch, archive);
    }

    private async Task<JsonDocument> GetJsonAsync(
        string relativeUrl,
        string? token,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, relativeUrl, token);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        EnsureSuccess(response, authenticated);
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonDocument.ParseAsync(
                content,
                new JsonDocumentOptions { MaxDepth = 32 },
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new RepositoryDiscoveryException(
                "GitHub returned an invalid repository response.",
                exception);
        }
    }

    private async Task DownloadArchiveAsync(
        string relativeUrl,
        string archivePath,
        string? token,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, relativeUrl, token);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        EnsureSuccess(response, authenticated);
        if (response.Content.Headers.ContentLength > MaximumArchiveBytes)
        {
            throw new RepositoryDiscoveryException("The GitHub repository archive exceeds the scan download limit.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            65_536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[65_536];
        long totalBytes = 0;
        while (true)
        {
            var bytesRead = await input.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > MaximumArchiveBytes)
            {
                throw new RepositoryDiscoveryException("The GitHub repository archive exceeds the scan download limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativeUrl,
        string? token)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, bool authenticated)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            throw new RepositoryAuthenticationRequiredException(authenticated
                ? "The signed-in GitHub account could not access the repository."
                : "GitHub authentication is required or repository access could not be verified.");
        }

        if ((int)response.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            throw new RepositoryDiscoveryException("GitHub API capacity is temporarily unavailable. Try again later.");
        }

        throw new RepositoryDiscoveryException(
            $"GitHub repository access failed with HTTP status {(int)response.StatusCode}.");
    }

    private static string ReadRequiredString(JsonElement root, string propertyName, int maximumLength)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new RepositoryDiscoveryException("GitHub returned incomplete repository metadata.");
        }

        var value = property.GetString()!;
        if (value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new RepositoryDiscoveryException("GitHub repository metadata exceeds the assessment contract limit.");
        }

        return value;
    }

    private static bool IsCommitSha(string value) =>
        value.Length is 40 or 64 && value.All(Uri.IsHexDigit);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(30),
        })
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromMinutes(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("arm-migration-assist/1.1");
        return client;
    }
}

internal sealed class GitHubCredentialProvider : IGitHubCredentialProvider
{
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(),
        };
        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        await process.StandardInput.WriteAsync("protocol=https\nhost=github.com\n\n");
        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            await Task.WhenAll(standardOutput, standardError);
            throw;
        }

        var output = await standardOutput;
        _ = await standardError;
        if (process.ExitCode != 0)
        {
            return null;
        }

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("password=", StringComparison.Ordinal))
            {
                return line["password=".Length..];
            }
        }

        return null;
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("credential.helper=manager");
        startInfo.ArgumentList.Add("credential");
        startInfo.ArgumentList.Add("fill");
        return startInfo;
    }
}