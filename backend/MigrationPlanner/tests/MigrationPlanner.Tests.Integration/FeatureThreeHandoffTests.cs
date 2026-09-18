using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutomatedMigration;
using AutomatedMigration.Models;
using FluentAssertions;
using MigrationPlanner.Tests.Integration.Fixtures;
using Xunit;

namespace MigrationPlanner.Tests.Integration;

public sealed class FeatureThreeHandoffTests : IClassFixture<PlannerWebApplicationFactory>
{
    private readonly PlannerWebApplicationFactory factory;

    public FeatureThreeHandoffTests(PlannerWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task PlannedAssessment_IsDirectlyConsumableByFeatureThree()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAndAwaitPlanAsync(
            "/api/migration-plans",
            MinimalValidAssessmentFactory.Build("assessment-feature-three"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: await response.Content.ReadAsStringAsync());
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var planJson = payload!.RootElement.GetProperty("plan").GetRawText();
        var plan = JsonSerializer.Deserialize<MigrationPlan>(planJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        plan.Should().NotBeNull();
        var selected = new MigrationActionsRunner().SelectWorkItems(plan!);
        selected.Select(item => item.AgentOrSkill).Should().Contain("build/add-arm64-target");
        selected.Select(item => item.AgentOrSkill).Should().Contain("pipeline/github-actions-arm64-job");
        selected.Should().OnlyContain(item => item.ApprovalRequired);
    }
}
