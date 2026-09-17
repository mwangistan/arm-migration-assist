using System.Text.Json.Serialization;
using ArmMigrationAssist.RepositoryDiscovery.Authentication;
using ArmMigrationAssist.RepositoryDiscovery.Jobs;
using ArmMigrationAssist.RepositoryDiscovery.Models;
using ArmMigrationAssist.RepositoryWorkspace;
using Microsoft.AspNetCore.Http.Json;

namespace ArmMigrationAssist.RepositoryDiscovery;

public static class AssessmentApi
{
    private const string LocalClientHeader = "X-Arm-Migration-Client";
    private static readonly byte[] RepositoryAssessmentSchema = ReadRepositoryAssessmentSchema();

    public const string DashboardCorsPolicyName = "dashboard";

    public static WebApplication Build(
        string[] args,
        IRepositoryAssessmentService? assessmentService = null,
        IGitHubAuthenticationBroker? authenticationBroker = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddAssessmentApi(builder.Configuration, assessmentService, authenticationBroker);
        var app = builder.Build();
        app.UseAssessmentApiPipeline();
        app.MapAssessmentApi();
        return app;
    }

    public static IServiceCollection AddAssessmentApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IRepositoryAssessmentService? assessmentService = null,
        IGitHubAuthenticationBroker? authenticationBroker = null)
    {
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        services.AddRepositoryClonePool(configuration);

        if (assessmentService is null)
        {
            services.AddSingleton<IRepositoryAssessmentService>(sp =>
                new RepositoryDiscoveryService(sp.GetRequiredService<IRepositoryClonePool>()));
        }
        else
        {
            services.AddSingleton<IRepositoryAssessmentService>(assessmentService);
        }

        if (authenticationBroker is null)
        {
            services.AddSingleton<IGitHubAuthenticationBroker, GitHubAuthenticationBroker>();
        }
        else
        {
            services.AddSingleton(authenticationBroker);
        }

        services.AddSingleton<AssessmentExecutionGate>();
        services.AddSingleton<AssessmentJobQueue>();
        services.AddHostedService<AssessmentJobWorker>();

        services.AddCors(options => options.AddPolicy(DashboardCorsPolicyName, policy => policy
            .WithOrigins(ReadAllowedOrigins(configuration))
            .AllowAnyHeader()
            .AllowAnyMethod()));

        return services;
    }

    public static WebApplication UseAssessmentApiPipeline(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.Headers.CacheControl = "no-store";
            }
            await next(context);
        });
        app.UseCors(DashboardCorsPolicyName);
        return app;
    }

    public static IEndpointRouteBuilder MapAssessmentApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ready",
            service = "repository-assessment",
            schemaVersion = "1.0",
            capabilities = new[]
            {
                "synchronous-assessment",
                "asynchronous-jobs",
                "server-sent-events",
                "github-browser-authentication",
                "readonly-github-archive",
            },
        }));
        endpoints.MapGet("/api/contracts/repository-assessment/v1", () =>
            Results.Bytes(RepositoryAssessmentSchema, "application/schema+json"));
        endpoints.MapPost("/api/auth/github/sessions", StartAuthentication);
        endpoints.MapGet("/api/auth/github/sessions/{sessionId}", GetAuthentication);
        endpoints.MapDelete("/api/auth/github/sessions/{sessionId}", CancelAuthentication);
        endpoints.MapPost("/api/assessments", AssessAsync);
        endpoints.MapPost("/api/assessment-jobs", CreateAssessmentJob);
        endpoints.MapGet("/api/assessment-jobs/{jobId}", GetAssessmentJob);
        endpoints.MapGet("/api/assessment-jobs/{jobId}/result", GetAssessmentJobResult);
        endpoints.MapGet("/api/assessment-jobs/{jobId}/events", StreamAssessmentJobEvents);
        endpoints.MapDelete("/api/assessment-jobs/{jobId}", CancelAssessmentJob);
        return endpoints;
    }

    private static async Task<IResult> AssessAsync(
        AssessmentRequest request,
        IRepositoryAssessmentService assessmentService,
        IGitHubAuthenticationBroker authenticationBroker,
        AssessmentExecutionGate executionGate,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > 2048)
        {
            return ValidationError("Enter a GitHub repository URL.");
        }

        var useStoredCredentials = false;
        if (!string.IsNullOrWhiteSpace(request.AuthenticationSessionId))
        {
            if (!IsLoopbackRequest(context))
            {
                return LocalAuthenticationProblem();
            }

            if (!authenticationBroker.IsAuthorized(request.AuthenticationSessionId))
            {
                return AuthenticationProblem(
                    "GitHub sign-in is incomplete or expired.",
                    StatusCodes.Status401Unauthorized);
            }

            useStoredCredentials = true;
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

        if (!executionGate.TryEnter())
        {
            return Results.Problem(
                title: "Assessment capacity reached",
                detail: "Two assessments are already running. Try again when one completes.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        try
        {
            var assessment = await assessmentService.DiscoverAsync(
                source,
                cancellationToken,
                new RepositoryAccessOptions(useStoredCredentials),
                null);
            return Results.Ok(assessment);
        }
        catch (RepositoryAuthenticationRequiredException)
        {
            if (useStoredCredentials && request.AuthenticationSessionId is not null)
            {
                authenticationBroker.Invalidate(request.AuthenticationSessionId);
            }

            return useStoredCredentials
                ? AuthenticationProblem(
                    "The signed-in GitHub account cannot access this repository. Verify organization SSO access, then try again.",
                    StatusCodes.Status403Forbidden)
                : AuthenticationProblem(
                    "This repository requires GitHub sign-in.",
                    StatusCodes.Status401Unauthorized);
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
            executionGate.Exit();
        }
    }

    private static IResult CreateAssessmentJob(
        AssessmentRequest request,
        IGitHubAuthenticationBroker authenticationBroker,
        AssessmentJobQueue jobs,
        HttpContext context)
    {
        if (!TryValidateRequest(
                request,
                authenticationBroker,
                context,
                out var source,
                out var useStoredCredentials,
                out var error))
        {
            return error!;
        }

        var job = jobs.Enqueue(
            source!,
            useStoredCredentials ? request.AuthenticationSessionId : null);
        return job is null
            ? Results.Problem(
                title: "Assessment queue capacity reached",
                detail: "The assessment queue is full. Try again when a queued assessment completes.",
                statusCode: StatusCodes.Status429TooManyRequests)
            : Results.Accepted(job.Snapshot().StatusUrl, job.Snapshot());
    }

    private static IResult GetAssessmentJob(string jobId, AssessmentJobQueue jobs)
    {
        var job = jobs.Get(jobId);
        return job is null ? Results.NotFound() : Results.Ok(job.Snapshot());
    }

    private static IResult GetAssessmentJobResult(string jobId, AssessmentJobQueue jobs)
    {
        var job = jobs.Get(jobId);
        if (job is null)
        {
            return Results.NotFound();
        }

        var snapshot = job.Snapshot();
        if (snapshot.Status == "completed")
        {
            return Results.Ok(job.GetResult());
        }

        return snapshot.Status is "failed" or "canceled"
            ? Results.Problem(
                title: "Assessment result unavailable",
                detail: snapshot.Error ?? snapshot.Message,
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["errorCode"] = snapshot.ErrorCode })
            : Results.Accepted(snapshot.StatusUrl, snapshot);
    }

    private static async Task StreamAssessmentJobEvents(
        string jobId,
        AssessmentJobQueue jobs,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var job = jobs.Get(jobId);
        if (job is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await AssessmentJobEvents.StreamAsync(job, context, cancellationToken);
    }

    private static IResult CancelAssessmentJob(string jobId, AssessmentJobQueue jobs)
    {
        var job = jobs.Get(jobId);
        if (job is null)
        {
            return Results.NotFound();
        }

        job.Cancel();
        return Results.Accepted(job.Snapshot().StatusUrl, job.Snapshot());
    }

    private static bool TryValidateRequest(
        AssessmentRequest request,
        IGitHubAuthenticationBroker authenticationBroker,
        HttpContext context,
        out string? source,
        out bool useStoredCredentials,
        out IResult? error)
    {
        source = null;
        useStoredCredentials = false;
        error = null;
        if (string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > 2048)
        {
            error = ValidationError("Enter a GitHub repository URL.");
            return false;
        }

        try
        {
            source = RepositoryIntake.NormalizeGitHubUrl(request.Source);
        }
        catch (RepositoryDiscoveryException exception)
        {
            error = ValidationError(exception.Message);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.AuthenticationSessionId))
        {
            if (!IsLoopbackRequest(context))
            {
                error = LocalAuthenticationProblem();
                return false;
            }

            if (!authenticationBroker.IsAuthorized(request.AuthenticationSessionId))
            {
                error = AuthenticationProblem(
                    "GitHub sign-in is incomplete or expired.",
                    StatusCodes.Status401Unauthorized);
                return false;
            }

            useStoredCredentials = true;
        }

        return true;
    }

    private static IResult StartAuthentication(
        IGitHubAuthenticationBroker authenticationBroker,
        HttpContext context)
    {
        if (!IsLoopbackRequest(context))
        {
            return LocalAuthenticationProblem();
        }

        if (!string.Equals(
                context.Request.Headers[LocalClientHeader],
                "dashboard",
                StringComparison.Ordinal))
        {
            return Results.BadRequest();
        }

        context.Response.Headers.CacheControl = "no-store";
        var session = authenticationBroker.Start();
        return Results.Accepted(
            $"/api/auth/github/sessions/{session.SessionId}",
            session);
    }

    private static IResult GetAuthentication(
        string sessionId,
        IGitHubAuthenticationBroker authenticationBroker,
        HttpContext context)
    {
        if (!IsLoopbackRequest(context))
        {
            return LocalAuthenticationProblem();
        }

        context.Response.Headers.CacheControl = "no-store";
        var session = authenticationBroker.Get(sessionId);
        return session is null ? Results.NotFound() : Results.Ok(session);
    }

    private static IResult CancelAuthentication(
        string sessionId,
        IGitHubAuthenticationBroker authenticationBroker,
        HttpContext context)
    {
        if (!IsLoopbackRequest(context))
        {
            return LocalAuthenticationProblem();
        }

        if (!string.Equals(
                context.Request.Headers[LocalClientHeader],
                "dashboard",
                StringComparison.Ordinal))
        {
            return Results.BadRequest();
        }

        return authenticationBroker.Cancel(sessionId)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static IResult AuthenticationProblem(string detail, int statusCode) =>
        Results.Problem(
            title: statusCode == StatusCodes.Status403Forbidden
                ? "Repository access denied"
                : "GitHub authentication required",
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?>
            {
                ["authenticationRequired"] = statusCode == StatusCodes.Status401Unauthorized,
                ["authenticationStartUrl"] = "/api/auth/github/sessions",
            });

    private static IResult LocalAuthenticationProblem() =>
        Results.Problem(
            title: "Local authentication required",
            detail: "Stored GitHub credentials can only be used through the loopback API.",
            statusCode: StatusCodes.Status403Forbidden);

    private static bool IsLoopbackRequest(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        return address is not null && System.Net.IPAddress.IsLoopback(address);
    }

    private static byte[] ReadRepositoryAssessmentSchema()
    {
        using var stream = typeof(AssessmentApi).Assembly.GetManifestResourceStream(
            "ArmMigrationAssist.Contracts.RepositoryAssessmentV1.schema.json")
            ?? throw new InvalidOperationException("The RepositoryAssessmentV1 schema resource is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
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

public sealed record AssessmentRequest(string Source, string? AuthenticationSessionId = null);