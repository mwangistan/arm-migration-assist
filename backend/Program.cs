using System.Text.Json;
using System.Text.Json.Serialization;
using ArmMigrationAssist.Api.Assessment;
using ArmMigrationAssist.Api.Assessment.CodeCompatibility;
using ArmMigrationAssist.Api.Assessment.Contract;
using ArmMigrationAssist.Api.Assessment.DependencyScanner;
using ArmMigrationAssist.Api.Assessment.RepositoryDiscovery;
using ArmMigrationAssist.Api.MigrationPlanner;

var builder = WebApplication.CreateBuilder(args);
var frontendOrigins = builder.Configuration.GetSection("FrontendOrigins").Get<string[]>() ?? [];

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (frontendOrigins.Length > 0)
            policy.WithOrigins(frontendOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

// Feature 1 - Repository Assessment Engine (deterministic scanners, no AI).
builder.Services.AddScoped<RepositoryIngestionService>();
builder.Services.AddScoped<AssessmentService>();
builder.Services.AddSingleton<ComponentDetectionScanner>();
builder.Services.AddHttpClient<DependencyRegistryVerifier>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("arm-migration-assist-feature1/1.0");
});
builder.Services.AddHttpClient<MigrationPlannerClient>(c =>
{
    var baseUrl = builder.Configuration["MigrationPlanner:BaseUrl"]
        ?? throw new InvalidOperationException("MigrationPlanner:BaseUrl is required.");
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
        baseUri.Scheme != Uri.UriSchemeHttps)
        throw new InvalidOperationException("MigrationPlanner:BaseUrl must be an absolute HTTPS URL.");

    c.BaseAddress = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromMinutes(10);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("arm-migration-assist/1.0");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

// Reusable assessment skills (Stories 1.2 - 1.4 + build readiness).
builder.Services.AddScoped<IAssessmentSkill, TechnologyStackDiscoverySkill>();
builder.Services.AddScoped<IAssessmentSkill, WindowsExperienceSkill>();
builder.Services.AddScoped<IAssessmentSkill, BuildReadinessSkill>();
builder.Services.AddScoped<IAssessmentSkill, DependencyScanSkill>();
builder.Services.AddScoped<IAssessmentSkill, CodeCompatibilitySkill>();

var app = builder.Build();

// CLI entry point:  dotnet run -- assess <repoUrl> [Arm64Native|Arm64EC]
// Runs the full Feature 1 pipeline in-process and prints the RepositoryAssessmentV1 document.
if (args.Length >= 2 && args[0].Equals("assess", StringComparison.OrdinalIgnoreCase))
{
    var target = args.Length >= 3 && args[2].Equals("Arm64EC", StringComparison.OrdinalIgnoreCase)
        ? MigrationTarget.Arm64EC
        : MigrationTarget.Arm64Native;

    using var scope = app.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<AssessmentService>();
    var manifest = await svc.AssessAsync(new AssessmentRequest(args[1], target));

    var doc = AssessmentV1Mapper.Map(manifest);
    var errors = EvidenceValidator.Validate(doc);
    if (errors.Count > 0)
        await Console.Error.WriteLineAsync("Evidence validation errors: " + string.Join("; ", errors));

    Console.WriteLine(AssessmentV1Mapper.Serialize(doc));
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("Frontend");
app.UseAuthorization();

app.MapControllers();

app.Run();
