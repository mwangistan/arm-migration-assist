using ArmMigrationAssist.RepositoryDiscovery;
using AutomatedMigration.Api;
using MigrationPlanner.Api;
using Validation.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAssessmentApi(builder.Configuration);
builder.Services.AddMigrationPlannerApi(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddAutomationApi(builder.Configuration);
builder.Services.AddValidationApi(builder.Configuration);

var app = builder.Build();

app.UseAssessmentApiPipeline();
app.UseMigrationPlannerPipeline();
app.UseAutomationApiPipeline(builder.Configuration);
// Validation loopback policy is applied as a group filter below rather than global middleware,
// so it doesn't reject F1/F2/F3 calls in this composed host.

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    services = new[] { "assessment", "planner", "automation", "validation" },
}));

app.MapAssessmentApi();
app.MapMigrationPlannerApi(includeRootHealth: false);
app.MapAutomationApi(includeRootHealth: false);
app.MapValidationApi(scopeLoopbackFilter: true);

app.Run();

public partial class Program;
