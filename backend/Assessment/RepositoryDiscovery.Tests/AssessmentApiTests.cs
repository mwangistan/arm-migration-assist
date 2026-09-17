using System.Net;
using System.Net.Http.Json;
using ArmMigrationAssist.RepositoryDiscovery;
using ArmMigrationAssist.RepositoryDiscovery.Authentication;
using ArmMigrationAssist.RepositoryDiscovery.Jobs;
using ArmMigrationAssist.RepositoryDiscovery.Models;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArmMigrationAssist.RepositoryDiscovery.Tests;

public sealed class AssessmentApiTests
{
    [Fact]
    public async Task Build_RegistersProductionAssessmentService()
    {
        await using var app = AssessmentApi.Build([]);

        var service = app.Services.GetRequiredService<IRepositoryAssessmentService>();

        Assert.IsType<RepositoryDiscoveryService>(service);
    }

    [Fact]
    public async Task Api_ReturnsAssessmentForAnonymousGitHubUrl()
    {
        var service = new StubAssessmentService(CreateAssessment());
        await using var app = AssessmentApi.Build([], service);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = ResolveAddress(app.Services) };
        var response = await client.PostAsJsonAsync(
            "/api/assessments",
            new { source = "https://github.com/example/sample-app" });
        var assessment = await response.Content.ReadFromJsonAsync<RepositoryAssessment>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(assessment);
        Assert.Equal("assessment-api-test", assessment.AssessmentId);
        Assert.Equal("https://github.com/example/sample-app", service.LastSource);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_ExposesRepositoryAssessmentSchemaForConsumers()
    {
        await using var app = AssessmentApi.Build([], new StubAssessmentService(CreateAssessment()));
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = ResolveAddress(app.Services) };
        var response = await client.GetAsync("/api/contracts/repository-assessment/v1");
        var schema = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/schema+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"title\": \"RepositoryAssessmentV1\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_RejectsLocalPathsWithoutInvokingScanner()
    {
        var service = new StubAssessmentService(CreateAssessment());
        await using var app = AssessmentApi.Build([], service);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = ResolveAddress(app.Services) };
        var response = await client.PostAsJsonAsync(
            "/api/assessments",
            new { source = "C:\\private\\repository" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(service.LastSource);
    }

    [Fact]
    public async Task Api_RequestsAuthenticationThenUsesAuthorizedCredentialSession()
    {
        var service = new StubAssessmentService(CreateAssessment())
        {
            AuthenticationRequired = true,
        };
        var authentication = new StubAuthenticationBroker();
        await using var app = AssessmentApi.Build([], service, authentication);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = ResolveAddress(app.Services) };
        var anonymous = await client.PostAsJsonAsync(
            "/api/assessments",
            new { source = "https://github.com/example/private-app" });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var startRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/github/sessions");
        startRequest.Headers.Add("X-Arm-Migration-Client", "dashboard");
        var start = await client.SendAsync(startRequest);
        var session = await start.Content.ReadFromJsonAsync<GitHubAuthenticationSession>();
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.NotNull(session);

        authentication.Authorize(session.SessionId);
        var authenticated = await client.PostAsJsonAsync(
            "/api/assessments",
            new
            {
                source = "https://github.com/example/private-app",
                authenticationSessionId = session.SessionId,
            });

        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        Assert.True(service.LastAccessOptions?.UseStoredGitHubCredentials);
    }

    [Fact]
    public async Task Api_RejectsHeaderlessAuthenticationStartWithoutLaunchingBroker()
    {
        var authentication = new StubAuthenticationBroker();
        await using var app = AssessmentApi.Build([], new StubAssessmentService(CreateAssessment()), authentication);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = ResolveAddress(app.Services) };
        var response = await client.PostAsync("/api/auth/github/sessions", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, authentication.StartCount);
    }

    [Fact]
    public async Task Api_CancelsPendingAuthenticationSession()
    {
        var authentication = new StubAuthenticationBroker();
        await using var app = AssessmentApi.Build([], new StubAssessmentService(CreateAssessment()), authentication);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = ResolveAddress(app.Services) };
        using var startRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/github/sessions");
        startRequest.Headers.Add("X-Arm-Migration-Client", "dashboard");
        var start = await client.SendAsync(startRequest);
        var session = await start.Content.ReadFromJsonAsync<GitHubAuthenticationSession>();
        Assert.NotNull(session);

        using var cancelRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/auth/github/sessions/{session.SessionId}");
        cancelRequest.Headers.Add("X-Arm-Migration-Client", "dashboard");
        var cancel = await client.SendAsync(cancelRequest);

        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Equal("failed", authentication.Get(session.SessionId)?.Status);
    }

    [Fact]
    public async Task Api_PublishesAssessmentJobEventsAndJsonResultForFeatureTwo()
    {
        var service = new StubAssessmentService(CreateAssessment());
        await using var app = AssessmentApi.Build([], service, new StubAuthenticationBroker());
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = ResolveAddress(app.Services) };
        var create = await client.PostAsJsonAsync(
            "/api/assessment-jobs",
            new { source = "https://github.com/example/sample-app" });
        var created = await create.Content.ReadFromJsonAsync<AssessmentJobResource>();

        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        Assert.NotNull(created);

        AssessmentJobResource? status = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            status = await client.GetFromJsonAsync<AssessmentJobResource>(created.StatusUrl);
            if (status?.Status == "completed")
            {
                break;
            }

            await Task.Delay(10);
        }

        Assert.NotNull(status);
        Assert.Equal("completed", status.Status);
        var result = await client.GetFromJsonAsync<RepositoryAssessment>(created.ResultUrl);
        Assert.NotNull(result);
        Assert.Equal("assessment-api-test", result.AssessmentId);

        var eventStream = await client.GetStringAsync(created.EventsUrl);
        Assert.Contains("retry: 3000", eventStream, StringComparison.Ordinal);
        Assert.Contains("event: assessment", eventStream, StringComparison.Ordinal);
        Assert.Contains("\"eventType\":\"completed\"", eventStream, StringComparison.Ordinal);
        Assert.Contains("\"phase\":\"dependency-scanner\"", eventStream, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_CancelsRunningAssessmentJob()
    {
        var service = new BlockingAssessmentService(CreateAssessment());
        await using var app = AssessmentApi.Build([], service, new StubAuthenticationBroker());
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = ResolveAddress(app.Services) };
        var create = await client.PostAsJsonAsync(
            "/api/assessment-jobs",
            new { source = "https://github.com/example/sample-app" });
        var job = await create.Content.ReadFromJsonAsync<AssessmentJobResource>();
        Assert.NotNull(job);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cancel = await client.DeleteAsync(job.StatusUrl);
        var canceled = await cancel.Content.ReadFromJsonAsync<AssessmentJobResource>();
        await service.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.NotNull(canceled);
        Assert.Equal("canceled", canceled.Status);
        var result = await client.GetAsync(job.ResultUrl);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
    }

    [Fact]
    public async Task Api_InvalidatesAuthenticatedJobSessionWhenRepositoryAccessIsDenied()
    {
        var service = new StubAssessmentService(CreateAssessment())
        {
            AlwaysDenyAccess = true,
        };
        var authentication = new StubAuthenticationBroker();
        var session = authentication.Start();
        authentication.Authorize(session.SessionId);
        await using var app = AssessmentApi.Build([], service, authentication);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = ResolveAddress(app.Services) };
        var create = await client.PostAsJsonAsync(
            "/api/assessment-jobs",
            new
            {
                source = "https://github.com/example/private-app",
                authenticationSessionId = session.SessionId,
            });
        var job = await create.Content.ReadFromJsonAsync<AssessmentJobResource>();
        Assert.NotNull(job);

        AssessmentJobResource? status = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            status = await client.GetFromJsonAsync<AssessmentJobResource>(job.StatusUrl);
            if (status?.Status == "failed")
            {
                break;
            }

            await Task.Delay(10);
        }

        Assert.NotNull(status);
        Assert.Equal("failed", status.Status);
        Assert.Equal("access-denied", status.ErrorCode);
        Assert.False(authentication.IsAuthorized(session.SessionId));
    }

    private static Uri ResolveAddress(IServiceProvider services)
    {
        var server = services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new Uri(address);
    }

    private static RepositoryAssessment CreateAssessment() => new(
        "1.0",
        "assessment-api-test",
        DateTimeOffset.Parse("2026-09-16T00:00:00Z"),
        new AssessmentProducer("test", "1.0.0", "test-1.0", []),
        new RepositoryIdentity(
            "sample-app",
            "https://github.com/example/sample-app",
            new string('a', 40),
            "main",
            null),
        new TechnologyInventory([], [], [], [], [], [], []),
        [],
        [],
        new BuildFindings(
            "build-api-test",
            false,
            false,
            false,
            false,
            false,
            [],
            [new Evidence("artifact", null, "tracked-file-inventory", "No build signals were detected.")]),
        new WindowsExperience(
            false,
            "unknown",
            false,
            false,
            "unknown",
            null,
            false,
            false,
            [new Evidence("artifact", null, "tracked-file-inventory", "No Windows signals were detected.")]),
        new ScanCoverage(0, 0, 1m, [], []),
        [],
        []);

    private sealed class StubAssessmentService(RepositoryAssessment assessment) : IRepositoryAssessmentService
    {
        public string? LastSource { get; private set; }
        public RepositoryAccessOptions? LastAccessOptions { get; private set; }
        public bool AuthenticationRequired { get; init; }
        public bool AlwaysDenyAccess { get; init; }

        public Task<RepositoryAssessment> DiscoverAsync(
            string source,
            CancellationToken cancellationToken = default,
            RepositoryAccessOptions? accessOptions = null,
            IProgress<AssessmentProgress>? progress = null)
        {
            LastSource = source;
            LastAccessOptions = accessOptions;
            if (AuthenticationRequired && accessOptions?.UseStoredGitHubCredentials != true)
            {
                throw new RepositoryAuthenticationRequiredException("Authentication required.");
            }

            if (AlwaysDenyAccess)
            {
                throw new RepositoryAuthenticationRequiredException("Access denied.");
            }

            progress?.Report(new AssessmentProgress(
                "dependency-scanner",
                60,
                "Scanning dependencies."));

            return Task.FromResult(assessment);
        }
    }

    private sealed class StubAuthenticationBroker : IGitHubAuthenticationBroker
    {
        private GitHubAuthenticationSession session = new(
            new string('a', 32),
            "pending",
            DateTimeOffset.UtcNow.AddMinutes(10),
            "Complete sign-in.");

        public int StartCount { get; private set; }

        public GitHubAuthenticationSession Start()
        {
            StartCount++;
            return session;
        }

        public GitHubAuthenticationSession? Get(string sessionId) =>
            sessionId == session.SessionId ? session : null;

        public bool IsAuthorized(string sessionId) => Get(sessionId)?.Status == "succeeded";

        public void Invalidate(string sessionId)
        {
            if (sessionId == session.SessionId)
            {
                session = session with { Status = "failed", Message = "Access denied." };
            }
        }

        public bool Cancel(string sessionId)
        {
            if (sessionId != session.SessionId || session.Status != "pending")
            {
                return false;
            }

            session = session with { Status = "failed", Message = "Canceled." };
            return true;
        }

        public void Authorize(string sessionId)
        {
            Assert.Equal(session.SessionId, sessionId);
            session = session with { Status = "succeeded", Message = "Signed in." };
        }
    }

    private sealed class BlockingAssessmentService(RepositoryAssessment assessment) : IRepositoryAssessmentService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RepositoryAssessment> DiscoverAsync(
            string source,
            CancellationToken cancellationToken = default,
            RepositoryAccessOptions? accessOptions = null,
            IProgress<AssessmentProgress>? progress = null)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return assessment;
            }
            catch (OperationCanceledException)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }
}