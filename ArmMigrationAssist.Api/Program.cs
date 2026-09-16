using System.Text.Json;
using System.Text.Json.Serialization;
using ArmMigrationAssist.Api.Assessment;
using ArmMigrationAssist.Api.Assessment.CodeCompatibility;
using ArmMigrationAssist.Api.Assessment.Contract;
using ArmMigrationAssist.Api.Assessment.DependencyScanner;
using ArmMigrationAssist.Api.Assessment.RepositoryDiscovery;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Feature 1 - Repository Assessment Engine (deterministic scanners, no AI).
builder.Services.AddScoped<RepositoryIngestionService>();
builder.Services.AddScoped<AssessmentService>();
builder.Services.AddSingleton<ComponentDetectionScanner>();
builder.Services.AddHttpClient<DependencyRegistryVerifier>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("arm-migration-assist-feature1/1.0");
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

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();
