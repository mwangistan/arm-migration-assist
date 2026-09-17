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

    [Theory]
    [InlineData("md", "text/markdown", "# sample-repo migration report")]
    [InlineData("html", "text/html", "<!doctype html>")]
    public async Task GetReport_SuccessfulPlanningRun_ReturnsDownload(
        string extension,
        string mediaType,
        string expectedContent)
    {
        using var client = _factory.CreateClient();
        var planResponse = await client.PostAsJsonAsync(
            "/api/migration-plans",
            MinimalValidAssessmentFactory.Build($"assessment-report-{extension}"));
        using var planBody = await planResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var runId = planBody!.RootElement.GetProperty("runId").GetString();

        var response = await client.GetAsync($"/api/migration-plans/{runId}/report.{extension}");
        var report = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(mediaType);
        response.Content.Headers.ContentDisposition!.FileName
            .Should().Be($"migration-report-{runId}.{extension}");
        report.Should().Contain(expectedContent);
        report.Should().Contain("Score digest");
        if (extension == "html")
        {
            report.ToLowerInvariant().Should().NotContain("<script");
        }
    }

    [Fact]
    public async Task GetReport_UnknownRun_Returns404()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/migration-plans/missing-run/report.md");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetReport_UnsupportedFormat_Returns415()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/migration-plans/missing-run/report.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }
}
