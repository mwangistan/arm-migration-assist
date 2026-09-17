using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Tests.Integration.Fixtures;
using Xunit;

namespace MigrationPlanner.Tests.Integration;

public sealed class ScoringFixturesEndpointTests : IClassFixture<PlannerWebApplicationFactory>
{
    private readonly PlannerWebApplicationFactory _factory;

    public ScoringFixturesEndpointTests(PlannerWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReadyManagedApp_ProducesReadyBandAndNativeArm64Path()
    {
        var (band, path, plan) = await PostAsync(ScoringFixtures.ReadyManagedApp());

        band.Should().Be("ready-or-minor-changes");
        path.Should().Be("native-arm64");

        var confidence = plan.GetProperty("confidence").GetString();
        confidence.Should().Be("high");
    }

    [Fact]
    public async Task ModerateBuildGap_ProducesModerateBandAndNativeArm64Path()
    {
        var (band, path, plan) = await PostAsync(ScoringFixtures.ModerateBuildGap());

        band.Should().Be("moderate-migration");
        path.Should().Be("native-arm64");
        plan.GetProperty("confidence").GetString().Should().Be("medium");
    }

    [Fact]
    public async Task BlockedNativeApp_CapsScoreBelow40AndPicksArm64Ec()
    {
        var (band, path, plan) = await PostAsync(ScoringFixtures.BlockedNativeApp());

        band.Should().Be("blocked-or-major-redesign");
        path.Should().Be("arm64ec");
        plan.GetProperty("confidence").GetString().Should().Be("low");
    }

    [Fact]
    public async Task IncompleteAssessment_ProducesInsufficientEvidenceBandAndPath()
    {
        var (band, path, plan) = await PostAsync(ScoringFixtures.IncompleteAssessment());

        band.Should().Be("insufficient-evidence");
        path.Should().Be("insufficient-evidence");
        plan.GetProperty("confidence").GetString().Should().Be("low");
    }

    private async Task<(string Band, string Path, JsonElement Plan)> PostAsync(RepositoryAssessmentV1 assessment)
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/migration-plans", assessment);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: await response.Content.ReadAsStringAsync());

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        body.Should().NotBeNull();
        var root = body!.RootElement;

        var score = root.GetProperty("score");
        var plan = root.GetProperty("plan");

        return (
            Band: score.GetProperty("band").GetString()!,
            Path: plan.GetProperty("recommendedPath").GetString()!,
            Plan: plan);
    }
}
