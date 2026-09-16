using System.Net;
using System.Net.Http.Json;
using ArmMigrationAssist.RepositoryDiscovery;
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

        public Task<RepositoryAssessment> DiscoverAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            LastSource = source;
            return Task.FromResult(assessment);
        }
    }
}