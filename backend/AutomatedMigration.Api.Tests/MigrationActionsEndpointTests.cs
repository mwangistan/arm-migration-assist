using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutomatedMigration.Api.Contracts;
using AutomatedMigration.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AutomatedMigration.Api.Tests;

// End-to-end HTTP-level validation of POST /api/migration-actions and GET
// /api/migration-actions/jobs/{jobId}. No network calls (bad target URL yields
// a 400 before the worker runs).
public sealed class MigrationActionsEndpointTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static MigrationPlan ValidPlan() => new(
        "1.0", "plan-endpoint-test", "native-arm64",
        new[]
        {
            new WorkItem(
                "wi-1", 1, "P0", "Add ARM64 CI job", "obj",
                "pipeline/github-actions-arm64-job", null, new[] { "patch" },
                null, null, null, null, true)
        });

    private static RepositoryTarget ValidTarget() => new(
        "https://github.com/example/repo",
        "0123456789abcdef0123456789abcdef01234567");

    [Fact]
    public async Task Post_returns_400_when_body_is_missing_plan()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/migration-actions",
            new { target = ValidTarget() }, Json);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_returns_400_when_url_is_not_github()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var body = new MigrationActionsRequest(
            ValidPlan(),
            new RepositoryTarget("https://example.com/repo", ValidTarget().CommitSha));
        var response = await client.PostAsJsonAsync("/api/migration-actions", body, Json);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_returns_400_when_commit_sha_is_short()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var body = new MigrationActionsRequest(
            ValidPlan(),
            new RepositoryTarget(ValidTarget().Url, "abcd"));
        var response = await client.PostAsJsonAsync("/api/migration-actions", body, Json);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_returns_202_and_status_url_for_valid_request()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var body = new MigrationActionsRequest(ValidPlan(), ValidTarget());
        var response = await client.PostAsJsonAsync("/api/migration-actions", body, Json);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await response.Content.ReadFromJsonAsync<MigrationActionsAccepted>(Json);
        accepted.Should().NotBeNull();
        accepted!.JobId.Should().StartWith("job-");
        accepted.StatusUrl.Should().Contain(accepted.JobId);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_returns_404_for_unknown_job()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var response = await client.GetAsync("/api/migration-actions/jobs/job-nope");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
