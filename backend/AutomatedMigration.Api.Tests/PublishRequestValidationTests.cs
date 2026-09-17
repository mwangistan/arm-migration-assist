using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutomatedMigration.Api.Contracts;
using AutomatedMigration.Api.Publication;
using AutomatedMigration.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AutomatedMigration.Api.Tests;

// Endpoint-level validation of the new `publish` request block. No live GitHub
// calls: rejected payloads short-circuit at 400 before the worker runs, and
// the 202 case only exercises the queue.
public sealed class PublishRequestValidationTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static MigrationPlan ValidPlan() => new(
        "1.0", "plan-publish-test", "native-arm64",
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
    public async Task Post_returns_400_when_publish_enabled_but_branchName_missing()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var body = new MigrationActionsRequest(ValidPlan(), ValidTarget(),
            new PublishRequest(Enabled: true, BranchName: ""));
        var response = await client.PostAsJsonAsync("/api/migration-actions", body, Json);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/leading-slash")]
    [InlineData("trailing-slash/")]
    [InlineData("has..dotdot")]
    [InlineData("bad chars")]
    [InlineData("no$symbols")]
    public async Task Post_returns_400_when_publish_branchName_is_unsafe(string branch)
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var body = new MigrationActionsRequest(ValidPlan(), ValidTarget(),
            new PublishRequest(Enabled: true, BranchName: branch));
        var response = await client.PostAsJsonAsync("/api/migration-actions", body, Json);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_returns_202_when_publish_is_omitted()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var body = new MigrationActionsRequest(ValidPlan(), ValidTarget(), Publish: null);
        var response = await client.PostAsJsonAsync("/api/migration-actions", body, Json);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Post_returns_202_when_publish_enabled_false()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var body = new MigrationActionsRequest(ValidPlan(), ValidTarget(),
            new PublishRequest(Enabled: false, BranchName: "unused"));
        var response = await client.PostAsJsonAsync("/api/migration-actions", body, Json);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Post_returns_202_when_publish_block_is_valid()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var body = new MigrationActionsRequest(ValidPlan(), ValidTarget(),
            new PublishRequest(
                Enabled: true,
                BranchName: "arm-migration-assist/wave-1",
                PrTitle: "ARM64 wave 1",
                PrBody: null,
                UpstreamDefaultBranch: null));
        var response = await client.PostAsJsonAsync("/api/migration-actions", body, Json);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
