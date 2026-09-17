using ArmMigrationAssist.RepositoryDiscovery;
using AutomatedMigration.Api;
using MigrationPlanner.Api;
using Validation.Api;

var builder = WebApplication.CreateBuilder(args);

// Wire F3 → F4 dispatch to the loopback address inside this same process. Callers can
// override via AUTOMATION_VALIDATION_API_URL for out-of-process F4 deployments.
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATION_VALIDATION_API_URL")))
{
    var listenUrl = builder.Configuration["urls"]
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
        ?? "http://127.0.0.1:8080";
    var firstUrl = listenUrl.Split(';', StringSplitOptions.RemoveEmptyEntries).First().Trim();
    Environment.SetEnvironmentVariable(
        "AUTOMATION_VALIDATION_API_URL",
        firstUrl.Replace("+", "127.0.0.1").Replace("*", "127.0.0.1"));
}

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
