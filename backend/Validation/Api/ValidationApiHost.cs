using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Validation.BuildValidation;
using Validation.Dashboard;

namespace Validation.Api;

public static class ValidationApiHost
{
    public const string GroupPrefix = "/api/v1/validation";

    public static IServiceCollection AddValidationApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureHttpJsonOptions(options => CopyValidationJsonOptions(options.SerializerOptions));
        services.Configure<ValidationApiOptions>(configuration.GetSection("ValidationApi"));
        services.AddProblemDetails();
        services.AddSingleton<IProcessRunner, LocalProcessRunner>();
        services.AddSingleton<IRepositoryInspector>(sp => new GitRepositoryInspector(sp.GetRequiredService<IProcessRunner>()));
        services.AddSingleton<IValidationWorkflowRunner, ValidationWorkflowRunner>();
        services.AddSingleton<IValidationStore, FileValidationStore>();
        services.AddSingleton<ValidationRunQueue>();
        services.AddHostedService<ValidationRunWorker>();
        services.AddSingleton<LoopbackOnlyEndpointFilter>();

        var foundry = FoundryValidationAiOptions.FromEnvironment() ?? FoundryFromConfiguration(configuration);
        if (foundry is not null)
        {
            services.AddSingleton(foundry);
            services.AddSingleton<FoundryValidationAiClient>(sp => FoundryValidationAiClient.Create(sp.GetRequiredService<FoundryValidationAiOptions>()));
            services.AddSingleton<IValidationPlanner>(sp => sp.GetRequiredService<FoundryValidationAiClient>());
            services.AddSingleton<IEvidenceAnalyzer>(sp => sp.GetRequiredService<FoundryValidationAiClient>());
            services.AddSingleton<ICoverageReviewer>(sp => sp.GetRequiredService<FoundryValidationAiClient>());
        }

        return services;
    }

    // Global installation for the single-service host (backend/Validation/Api).
    public static WebApplication UseValidationApiPipeline(this WebApplication app)
    {
        app.UseExceptionHandler(exceptionApp => exceptionApp.Run(async context =>
        {
            var error = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
            var (status, detail) = ExceptionMapping.Classify(error);
            context.Response.StatusCode = status;
            await Results.Problem(detail, statusCode: status).ExecuteAsync(context);
        }));
        app.UseMiddleware<LoopbackOnlyMiddleware>();
        return app;
    }

    // Endpoint-scoped registration for the composed host. Loopback policy is attached to
    // the group filter instead of a global middleware so other apps' endpoints are unaffected.
    public static IEndpointRouteBuilder MapValidationApi(this IEndpointRouteBuilder endpoints, bool scopeLoopbackFilter)
    {
        endpoints.MapGet("/healthz", () => Results.Ok(new HealthResponse("ok")));

        var group = endpoints.MapGroup(GroupPrefix);
        if (scopeLoopbackFilter)
        {
            group.AddEndpointFilter<LoopbackOnlyEndpointFilter>();
        }

        group.MapPost("/plans", CreatePlanAsync);
        group.MapGet("/plans/{planId}", GetPlanAsync);
        group.MapGet("/plans/{planId}/approval", GetApprovalAsync);
        group.MapPut("/plans/{planId}/approval", UpdateApprovalAsync);
        group.MapPost("/plans/{planId}/runs", CreateRunAsync);
        group.MapGet("/runs/{runId}", GetRunAsync);
        group.MapGet("/runs/{runId}/report", GetRunReportAsync);
        group.MapGet("/runs/{runId}/dashboard", GetRunDashboardAsync);
        return endpoints;
    }

    private static async Task<IResult> CreatePlanAsync(
        CreatePlanRequest? request,
        IValidationWorkflowRunner workflow,
        IValidationStore store,
        CancellationToken cancellationToken)
    {
        // Every field below is attacker/caller-controlled input. Reject anything missing or
        // malformed with 400 here, before it can reach Path.GetFullPath/IsPathFullyQualified
        // (which throw ArgumentException for some invalid strings) or a null-reference deeper
        // in the pipeline, either of which the exception handler would otherwise map to a 500.
        if (request is null)
            return Problems.BadRequest("Request body is required.");
        if (request.MigrationPlan is null)
            return Problems.BadRequest("migrationPlan is required.");
        if (!RequestValidation.HasRequiredMigrationFields(request.MigrationPlan))
            return Problems.BadRequest("migrationPlan requires plan IDs, validation check lists, targetDevices, and workItems with acceptanceTests.");
        if (request.Target is null || string.IsNullOrWhiteSpace(request.Target.Path))
            return Problems.BadRequest("target.path is required.");
        if (request.Options is null || string.IsNullOrWhiteSpace(request.Options.EvidenceDirectory))
            return Problems.BadRequest("options.evidenceDirectory is required.");

        string targetPath, evidenceDirectory;
        try
        {
            if (!Path.IsPathFullyQualified(request.Target.Path))
                return Problems.BadRequest("target.path must be an absolute path.");
            if (!Path.IsPathFullyQualified(request.Options.EvidenceDirectory))
                return Problems.BadRequest("options.evidenceDirectory must be an absolute path.");
            targetPath = Path.GetFullPath(request.Target.Path);
            evidenceDirectory = Path.GetFullPath(request.Options.EvidenceDirectory);
            RepositoryPaths.RejectLinks(targetPath);
            RepositoryPaths.RejectLinks(evidenceDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or InvalidDataException)
        {
            return Problems.BadRequest("target.path or options.evidenceDirectory is invalid or contains a linked path.");
        }

        // The evidence directory must not be nested inside the repository being validated (so
        // validation evidence is never mistaken for tracked repository content) nor inside this
        // API's own storage root (so plan/run bookkeeping and evidence never collide on disk).
        if (PathGuard.IsSameOrInside(targetPath, evidenceDirectory))
            return Problems.BadRequest("options.evidenceDirectory must not be inside the target repository.");
        if (store.EvidenceDirectoryIsInsideStorage(evidenceDirectory))
            return Problems.BadRequest("options.evidenceDirectory must not be inside the validation API storage root.");
        if (store.StorageRootIsInsideTarget(targetPath))
            return Problems.BadRequest("Validation API storage must not be inside the target repository.");

        PreparedValidation prepared;
        try
        {
            prepared = await workflow.PrepareAsync(request.MigrationPlan, request.Target, request.Options, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Problems.BadRequest("Repository or planning input failed validation. Check the supplied paths, commit, and plan.");
        }
        PlanSafety.Validate(prepared);
        var created = await store.CreatePlanAsync(prepared, cancellationToken);
        var response = PlanResponse.From(created.Metadata, includeProposal: request.IncludeProposal ? prepared : null);
        return Results.Created(response.Links.Self, response);
    }

    private static async Task<IResult> GetPlanAsync(string planId, IValidationStore store, CancellationToken cancellationToken)
    {
        var plan = await store.GetPlanAsync(planId, cancellationToken);
        return plan is null
            ? Problems.NotFound("Validation plan was not found.")
            : Results.Json(PlanResponse.From(plan.Metadata, plan.Prepared), ValidationJson.Options);
    }

    private static async Task<IResult> GetApprovalAsync(string planId, IValidationStore store, CancellationToken cancellationToken)
    {
        var plan = await store.GetPlanAsync(planId, cancellationToken);
        if (plan is null) return Problems.NotFound("Validation plan was not found.");
        var approval = plan.Approval ?? AutoApprove(plan.Prepared);
        return Results.Json(ApprovalResponse.From(plan.Metadata.PlanId, approval), ValidationJson.Options);
    }

    private static async Task<IResult> UpdateApprovalAsync(
        string planId,
        UpdateApprovalRequest? request,
        IValidationStore store,
        CancellationToken cancellationToken)
    {
        if (request?.ApprovedCommandIds is null)
            return Problems.BadRequest("approvedCommandIds is required; use an empty list to explicitly approve no commands.");
        var plan = await store.GetPlanAsync(planId, cancellationToken);
        if (plan is null) return Problems.NotFound("Validation plan was not found.");
        if (!string.IsNullOrWhiteSpace(request.PlanFingerprint) &&
            !string.Equals(request.PlanFingerprint, plan.Metadata.Fingerprint, StringComparison.Ordinal))
            return Problems.Conflict("Approval fingerprint does not match the stored plan fingerprint.");

        var approval = new PlanApproval(plan.Metadata.Fingerprint, request.ApprovedCommandIds, request.SkippedCommands);
        string? validationError = ValidateApproval(plan.Prepared, approval);
        if (validationError is not null) return Problems.BadRequest(validationError);

        await store.SaveApprovalAsync(planId, approval, cancellationToken);
        return Results.Json(ApprovalResponse.From(plan.Metadata.PlanId, approval), ValidationJson.Options);
    }

    private static async Task<IResult> CreateRunAsync(
        string planId,
        IValidationStore store,
        ValidationRunQueue queue,
        CancellationToken cancellationToken)
    {
        var plan = await store.GetPlanAsync(planId, cancellationToken);
        if (plan is null) return Problems.NotFound("Validation plan was not found.");

        PlanSafety.Validate(plan.Prepared);
        var approval = plan.Approval is not null && ValidateApproval(plan.Prepared, plan.Approval) is null
            ? plan.Approval
            : AutoApprove(plan.Prepared);

        // The prepared plan and approval are snapshotted into the run at creation time (see
        // FileValidationStore.CreateRunAsync); a later PUT to the plan's approval can never affect
        // this run once it exists. CreateRunAsync also enforces one lifetime run per
        // plan, because TRX/evidence proof paths are plan-scoped, not run-scoped.
        var record = await store.CreateRunAsync(planId, plan.Prepared, approval, cancellationToken);
        if (record is null)
            return Problems.Conflict("This plan has already been used for a run. Create and approve a new plan to avoid stale proof evidence.");

        // TryEnqueue is nonblocking and cannot be tied to request cancellation: awaiting a bounded
        // channel write against the request's CancellationToken would, on client abort, throw and
        // leave the just-created run permanently "Queued" with nothing left to ever dequeue it.
        if (!queue.TryEnqueue(record.RunId))
        {
            await store.FailRunAsync(record.RunId, "Validation run queue is full; retry later.", CancellationToken.None);
            return Results.Problem("Validation run queue is full. Retry later.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Accepted(RunLinks.StatusPath(record.RunId), RunResponse.From(record));
    }

    private static async Task<IResult> GetRunAsync(string runId, IValidationStore store, CancellationToken cancellationToken)
    {
        var record = await store.GetRunAsync(runId, cancellationToken);
        return record is null
            ? Problems.NotFound("Validation run was not found.")
            : Results.Json(RunResponse.From(record), ValidationJson.Options);
    }

    private static async Task<IResult> GetRunReportAsync(string runId, IValidationStore store, CancellationToken cancellationToken)
    {
        var record = await store.GetRunAsync(runId, cancellationToken);
        if (record is null) return Problems.NotFound("Validation run was not found.");
        if (record.Status != ValidationRunStatus.Completed) return Problems.TerminalOrPendingConflict(record, "report");
        var report = await store.GetReportAsync(runId, cancellationToken);
        return report is null ? Problems.NotFound("Validation report was not found.") : Results.Json(report, ValidationJson.Options);
    }

    private static async Task<IResult> GetRunDashboardAsync(string runId, IValidationStore store, CancellationToken cancellationToken)
    {
        var record = await store.GetRunAsync(runId, cancellationToken);
        if (record is null) return Problems.NotFound("Validation run was not found.");
        if (record.Status != ValidationRunStatus.Completed) return Problems.TerminalOrPendingConflict(record, "dashboard");
        var dashboard = await store.GetDashboardAsync(runId, cancellationToken);
        return dashboard is null ? Problems.NotFound("Validation dashboard was not found.") : Results.Json(dashboard, ValidationJson.Options);
    }

    public static void CopyValidationJsonOptions(JsonSerializerOptions target)
    {
        target.PropertyNamingPolicy = ValidationJson.Options.PropertyNamingPolicy;
        target.PropertyNameCaseInsensitive = ValidationJson.Options.PropertyNameCaseInsensitive;
        target.WriteIndented = ValidationJson.Options.WriteIndented;
        target.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false));
    }

    internal static PlanApproval AutoApprove(PreparedValidation prepared) => new(
        PlanSafety.Fingerprint(prepared),
        prepared.Commands.Select(command => command.Id).ToArray(),
        null);

    internal static string? ValidateApproval(PreparedValidation prepared, PlanApproval approval)
    {
        if (!string.Equals(PlanSafety.Fingerprint(prepared), approval.PlanFingerprint, StringComparison.Ordinal))
            return "Approval fingerprint does not match the stored plan fingerprint.";
        var commandIds = prepared.Commands.Select(command => command.Id).ToHashSet(StringComparer.Ordinal);
        if (approval.ApprovedCommandIds.Any(id => !commandIds.Contains(id)))
            return "Approval contains unknown command IDs.";
        if (approval.SkippedCommands is not null &&
            approval.SkippedCommands.Any(pair => !commandIds.Contains(pair.Key) ||
                approval.ApprovedCommandIds.Contains(pair.Key) ||
                string.IsNullOrWhiteSpace(pair.Value)))
            return "Skipped commands require a reason, a known ID, and must not also be approved.";
        return null;
    }

    internal static FoundryValidationAiOptions? FoundryFromConfiguration(IConfiguration configuration)
    {
        string? endpoint = configuration["ValidationApi:Foundry:Endpoint"];
        string? deployment = configuration["ValidationApi:Foundry:DeploymentName"];
        if (string.IsNullOrWhiteSpace(endpoint) && string.IsNullOrWhiteSpace(deployment))
            return null;
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment))
            throw new InvalidDataException("ValidationApi:Foundry:Endpoint and ValidationApi:Foundry:DeploymentName must both be set.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("ValidationApi:Foundry:Endpoint must be an absolute HTTPS URI.");
        return new(uri, deployment);
    }
}
