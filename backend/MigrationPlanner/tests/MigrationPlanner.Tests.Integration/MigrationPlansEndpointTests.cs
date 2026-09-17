using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Infrastructure.Model;
using MigrationPlanner.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    [Fact]
    public async Task Post_AssessmentUnknownWithoutSkill_ReturnsValidPlanUnknown()
    {
        var assessment = MinimalValidAssessmentFactory.Build("assessment-unknown-projection") with
        {
            Unknowns = new[]
            {
                new Unknown(
                    "Packaging architecture still needs verification.",
                    UnknownArea.Build,
                    RequiredSkill: null,
                    EvidenceIds: new[] { "build-001" }),
            },
        };
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/migration-plans", assessment);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: await response.Content.ReadAsStringAsync());
        var unknown = body!.RootElement.GetProperty("plan").GetProperty("unknowns")[0];
        unknown.GetProperty("description").GetString()
            .Should().Be("Packaging architecture still needs verification.");
        unknown.TryGetProperty("requiredSkill", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Post_ModelSkillIoMismatch_RetriesWithDeclaredSkillContracts()
    {
        var model = new SkillIoMismatchThenValidModel();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlannerModel>();
                services.AddSingleton<IPlannerModel>(model);
            }));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/migration-plans",
            MinimalValidAssessmentFactory.Build("assessment-skill-io-retry"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: await response.Content.ReadAsStringAsync());
        model.Hints.Should().HaveCount(2);
        model.Hints[0].Should().BeNull();
        model.Hints[1]!.Reason.Should().Be(PlannerRetryReason.SkillIoMismatch);
        model.Hints[1]!.AvailableSkillContracts.Should().Contain(contract =>
            contract.Name == "assessment/repository-discovery"
            && contract.SupportedInputs.SequenceEqual(new[] { "repository" })
            && contract.SupportedOutputs.SequenceEqual(new[] { "assessment" }));
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
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Content.Headers.ContentType!.MediaType.Should().Be(mediaType);
        response.Content.Headers.ContentDisposition!.FileName
            .Should().Be($"migration-report-{runId}.{extension}");
        report.Should().Contain(expectedContent);
        report.Should().Contain("Score digest");
        report.Should().Contain("Alternatives considered");
        report.Should().Contain("Validation plan");
        report.Should().Contain("Capability and approval gates");
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

    private sealed class SkillIoMismatchThenValidModel : IPlannerModel
    {
        private readonly FakePlannerModel _validModel = new();

        public List<PlannerRetryHint?> Hints { get; } = [];

        public async Task<PlannerModelResult> GeneratePlanJsonAsync(
            RepositoryAssessmentV1 assessment,
            Domain.Plan.ReadinessScoreV1 score,
            IGuidanceLookup guidanceLookup,
            CancellationToken cancellationToken,
            PlannerRetryHint? retryHint = null)
        {
            Hints.Add(retryHint);
            var valid = await _validModel.GeneratePlanJsonAsync(
                assessment, score, guidanceLookup, cancellationToken, retryHint);
            if (retryHint is not null)
            {
                return valid;
            }

            var root = JsonNode.Parse(valid.PlanJson)!.AsObject();
            var workItem = root["workItems"]!.AsArray()[0]!.AsObject();
            workItem["agentOrSkill"] = "assessment/repository-discovery";
            workItem["inputs"] = new JsonArray("tests/Makefile");
            workItem["expectedOutputs"] = new JsonArray("patch");
            return PlannerModelResult.FromJson(root.ToJsonString());
        }
    }
}
