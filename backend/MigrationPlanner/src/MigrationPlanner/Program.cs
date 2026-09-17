using MigrationPlanner.Api;
using MigrationPlanner.Llm;
using MigrationPlanner.Scoring;

var builder = WebApplication.CreateBuilder(args);

var options = new PlannerOptions();
builder.Configuration.GetSection("Planner").Bind(options);

var corpusEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_CORPUS_VERSION");
if (!string.IsNullOrWhiteSpace(corpusEnv)) options.CorpusVersion = corpusEnv;
var originsEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_ALLOWED_ORIGINS");
if (!string.IsNullOrWhiteSpace(originsEnv))
{
    options.AllowedOrigins = originsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
var skeletonOnlyEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_SKELETON_ONLY");
if (!string.IsNullOrWhiteSpace(skeletonOnlyEnv) && bool.TryParse(skeletonOnlyEnv, out var skOnly))
{
    options.SkeletonOnly = skOnly;
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<DeterministicReadinessScorer>();
builder.Services.AddProblemDetails();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

var llmOptions = new LlmOptions();
builder.Configuration.GetSection(LlmOptions.SectionName).Bind(llmOptions);
var llmEndpointEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_LLM_ENDPOINT");
if (!string.IsNullOrWhiteSpace(llmEndpointEnv)) llmOptions.Endpoint = llmEndpointEnv;
var llmDeploymentEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_LLM_DEPLOYMENT");
if (!string.IsNullOrWhiteSpace(llmDeploymentEnv)) llmOptions.DeploymentName = llmDeploymentEnv;
var llmKeyEnv = Environment.GetEnvironmentVariable("MIGRATIONPLANNER_LLM_API_KEY");
if (!string.IsNullOrWhiteSpace(llmKeyEnv)) llmOptions.ApiKey = llmKeyEnv;

if (!string.IsNullOrWhiteSpace(llmOptions.Endpoint) && !options.SkeletonOnly)
{
    builder.Services.AddSingleton(llmOptions);
    builder.Services.AddSingleton<LlmClient>();
    builder.Services.AddSingleton<NarrativeFiller>();

    // Sync provenance shown in the plan with the actual model.
    options.ProvenanceName = llmOptions.DeploymentName;
}

if (options.AllowedOrigins.Length > 0)
{
    builder.Services.AddCors(cors => cors.AddPolicy(PlannerOptions.CorsPolicyName, policy => policy
        .WithOrigins(options.AllowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
if (options.AllowedOrigins.Length > 0) app.UseCors(PlannerOptions.CorsPolicyName);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow }));
app.MapMigrationPlans();

app.Run();

public partial class Program;
