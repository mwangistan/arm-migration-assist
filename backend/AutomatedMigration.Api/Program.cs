using AutomatedMigration.Api.Configuration;
using AutomatedMigration.Api.Endpoints;
using AutomatedMigration.Api.Jobs;
using AutomatedMigration.Api.Publication;
using AutomatedMigration.Api.Repository;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AutomationOptions>(builder.Configuration.GetSection(AutomationOptions.SectionName));
var options = builder.Configuration.GetSection(AutomationOptions.SectionName).Get<AutomationOptions>() ?? new AutomationOptions();

var originsEnv = Environment.GetEnvironmentVariable("AUTOMATION_ALLOWED_ORIGINS");
if (!string.IsNullOrWhiteSpace(originsEnv))
{
    options.AllowedOrigins = originsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

var pubOptions = builder.Configuration.GetSection(PublicationOptions.SectionName).Get<PublicationOptions>() ?? new PublicationOptions();
var publishToken = Environment.GetEnvironmentVariable("AUTOMATION_PUBLISH_TOKEN")
                   ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
if (!string.IsNullOrWhiteSpace(publishToken))
{
    pubOptions.Token = publishToken;
}
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(pubOptions));

builder.Services.AddProblemDetails();
if (options.AllowedOrigins.Length > 0)
{
    builder.Services.AddCors(cors => cors.AddPolicy("AutomationFrontend", policy => policy
        .WithOrigins(options.AllowedOrigins)
        .WithMethods("GET", "POST", "OPTIONS")
        .WithHeaders("Content-Type")));
}

builder.Services.AddHttpClient<RepositoryFetcher>();
builder.Services.AddHttpClient<IGitHubPublisher, GitHubPublisher>();

var validationOpts = new ValidationDispatcherOptions();
var validationUrl = Environment.GetEnvironmentVariable("AUTOMATION_VALIDATION_API_URL");
if (!string.IsNullOrWhiteSpace(validationUrl))
{
    validationOpts.BaseUrl = validationUrl;
}
builder.Services.AddSingleton(validationOpts);
builder.Services.AddHttpClient<IValidationDispatcher, HttpValidationDispatcher>(client =>
{
    if (!string.IsNullOrWhiteSpace(validationOpts.BaseUrl))
    {
        client.BaseAddress = new Uri(validationOpts.BaseUrl, UriKind.Absolute);
    }
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, validationOpts.TimeoutSeconds));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("arm-migration-assist-automation-api/1.0");
});

builder.Services.AddSingleton<MigrationJobStore>();
builder.Services.AddHostedService<MigrationJobWorker>();

var app = builder.Build();

if (options.AllowedOrigins.Length > 0)
{
    app.UseCors("AutomationFrontend");
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    publish = pubOptions.IsConfigured ? "configured" : "not-configured",
    validation = string.IsNullOrWhiteSpace(validationOpts.BaseUrl) ? "not-configured" : "configured",
}));
app.MapMigrationActionsEndpoints();

app.Run();

public partial class Program { }
