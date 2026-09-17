using AutomatedMigration.Api.Configuration;
using AutomatedMigration.Api.Endpoints;
using AutomatedMigration.Api.Jobs;
using AutomatedMigration.Api.Repository;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AutomationOptions>(builder.Configuration.GetSection(AutomationOptions.SectionName));
var options = builder.Configuration.GetSection(AutomationOptions.SectionName).Get<AutomationOptions>() ?? new AutomationOptions();

var originsEnv = Environment.GetEnvironmentVariable("AUTOMATION_ALLOWED_ORIGINS");
if (!string.IsNullOrWhiteSpace(originsEnv))
{
    options.AllowedOrigins = originsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

builder.Services.AddProblemDetails();
if (options.AllowedOrigins.Length > 0)
{
    builder.Services.AddCors(cors => cors.AddPolicy("AutomationFrontend", policy => policy
        .WithOrigins(options.AllowedOrigins)
        .WithMethods("GET", "POST", "OPTIONS")
        .WithHeaders("Content-Type")));
}

builder.Services.AddHttpClient<RepositoryFetcher>();
builder.Services.AddSingleton<MigrationJobStore>();
builder.Services.AddHostedService<MigrationJobWorker>();

var app = builder.Build();

if (options.AllowedOrigins.Length > 0)
{
    app.UseCors("AutomationFrontend");
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapMigrationActionsEndpoints();

app.Run();

public partial class Program { }
