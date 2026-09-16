using System.Text.Json.Serialization;
using ArmMigrationAssist.RepositoryDiscovery.Models;
using Microsoft.AspNetCore.Http.Json;

namespace ArmMigrationAssist.RepositoryDiscovery;

public static class AssessmentApi
{
    private const int MaximumConcurrentAssessments = 2;
    private static readonly SemaphoreSlim AssessmentSlots = new(MaximumConcurrentAssessments);

    public static WebApplication Build(
        string[] args,
        IRepositoryAssessmentService? assessmentService = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        if (assessmentService is null)
        {
            builder.Services.AddSingleton<IRepositoryAssessmentService, RepositoryDiscoveryService>();
        }
        else
        {
            builder.Services.AddSingleton<IRepositoryAssessmentService>(assessmentService);
        }

        builder.Services.AddCors(options => options.AddPolicy("dashboard", policy =>
        {
            policy
                .WithOrigins(ReadAllowedOrigins(builder.Configuration))
                .AllowAnyHeader()
                .AllowAnyMethod();
        }));

        var app = builder.Build();
        app.UseCors("dashboard");
        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ready",
            service = "repository-assessment",
            schemaVersion = "1.0",
        }));
        app.MapPost("/api/assessments", AssessAsync);
        return app;
    }

    private static async Task<IResult> AssessAsync(
        AssessmentRequest request,
        IRepositoryAssessmentService assessmentService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > 2048)
        {
            return ValidationError("Enter a GitHub repository URL.");
        }

        string source;
        try
        {
            source = RepositoryIntake.NormalizeGitHubUrl(request.Source);
        }
        catch (RepositoryDiscoveryException exception)
        {
            return ValidationError(exception.Message);
        }

        if (!await AssessmentSlots.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return Results.Problem(
                title: "Assessment capacity reached",
                detail: "Two assessments are already running. Try again when one completes.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        try
        {
            var assessment = await assessmentService.DiscoverAsync(source, cancellationToken);
            return Results.Ok(assessment);
        }
        catch (RepositoryDiscoveryException exception)
        {
            return Results.Problem(
                title: "Repository assessment failed",
                detail: exception.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        finally
        {
            AssessmentSlots.Release();
        }
    }

    private static IResult ValidationError(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["source"] = [message],
        });

    private static string[] ReadAllowedOrigins(IConfiguration configuration)
    {
        var configured = configuration["DashboardOrigins"]?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return configured is { Length: > 0 }
            ? configured
            : ["http://localhost:5173", "http://127.0.0.1:5173"];
    }
}

public sealed record AssessmentRequest(string Source);