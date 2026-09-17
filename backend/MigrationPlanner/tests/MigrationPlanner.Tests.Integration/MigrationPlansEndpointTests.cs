using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MigrationPlanner.Tests.Integration.Fixtures;
using Xunit;

namespace MigrationPlanner.Tests.Integration;

public sealed class MigrationPlansEndpointTests : IClassFixture<PlannerWebApplicationFactory>
{
    private readonly PlannerWebApplicationFactory _factory;

    public MigrationPlansEndpointTests(PlannerWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_MinimalValidAssessment_Returns200_WithMatchingAssessmentId()
    {
        var assessment = MinimalValidAssessmentFactory.Build("assessment-integration-001");
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/migration-plans", assessment);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: await response.Content.ReadAsStringAsync());

        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        body.Should().NotBeNull();
        var root = body!.RootElement;

        root.TryGetProperty("plan", out var plan).Should().BeTrue();
        plan.TryGetProperty("assessmentId", out var idElement).Should().BeTrue();
        idElement.GetString().Should().Be("assessment-integration-001");

        root.TryGetProperty("runId", out var runId).Should().BeTrue();
        runId.GetString().Should().NotBeNullOrWhiteSpace();
    }
}
