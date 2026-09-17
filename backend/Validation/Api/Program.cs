using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json.Serialization;
using Validation.Api;
using Validation.BuildValidation;
using Validation.Dashboard;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5084");

builder.Services.ConfigureHttpJsonOptions(options => CopyValidationJsonOptions(options.SerializerOptions));
builder.Services.Configure<ValidationApiOptions>(builder.Configuration.GetSection("ValidationApi"));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IProcessRunner, LocalProcessRunner>();
builder.Services.AddSingleton<IRepositoryInspector>(services => new GitRepositoryInspector(services.GetRequiredService<IProcessRunner>()));
builder.Services.AddSingleton<IValidationWorkflowRunner, ValidationWorkflowRunner>();
builder.Services.AddSingleton<IValidationStore, FileValidationStore>();
builder.Services.AddSingleton<ValidationRunQueue>();
builder.Services.AddHostedService<ValidationRunWorker>();

var foundry = FoundryValidationAiOptions.FromEnvironment() ?? FoundryFromConfiguration(builder.Configuration);
if (foundry is not null)
{
    builder.Services.AddSingleton(foundry);
    builder.Services.AddSingleton<FoundryValidationAiClient>(services => FoundryValidationAiClient.Create(services.GetRequiredService<FoundryValidationAiOptions>()));
    builder.Services.AddSingleton<IValidationPlanner>(services => services.GetRequiredService<FoundryValidationAiClient>());
    builder.Services.AddSingleton<IEvidenceAnalyzer>(services => services.GetRequiredService<FoundryValidationAiClient>());
    builder.Services.AddSingleton<ICoverageReviewer>(services => services.GetRequiredService<FoundryValidationAiClient>());
}

var app = builder.Build();

app.UseExceptionHandler(exceptionApp => exceptionApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
    var (status, detail) = ExceptionMapping.Classify(error);
    context.Response.StatusCode = status;
    await Results.Problem(detail, statusCode: status)
        .ExecuteAsync(context);
}));

app.UseMiddleware<LoopbackOnlyMiddleware>();

app.MapGet("/healthz", () => Results.Ok(new HealthResponse("ok")));

var group = app.MapGroup("/api/v1/validation");

group.MapPost("/plans", async (
    CreatePlanRequest? request,
    IValidationWorkflowRunner workflow,
    IValidationStore store,
    CancellationToken cancellationToken) =>
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
});

// Clone-based sibling of POST /plans. Accepts a github.com URL + branch (optionally pinned
// to a commit sha) and delegates to the same workflow once the tree is on disk. Scratch
// lives under /tmp so evidence-under-storage guards on the local-path endpoint still hold.
group.MapPost("/plans/from-git", async (
    CreatePlanFromGitRequest? request,
    IValidationWorkflowRunner workflow,
    IValidationStore store,
    CancellationToken cancellationToken) =>
{
    if (request is null)
        return Problems.BadRequest("Request body is required.");
    if (request.MigrationPlan is null)
        return Problems.BadRequest("migrationPlan is required.");
    if (!RequestValidation.HasRequiredMigrationFields(request.MigrationPlan))
        return Problems.BadRequest("migrationPlan requires plan IDs, validation check lists, targetDevices, and workItems with acceptanceTests.");
    if (request.Source is null)
        return Problems.BadRequest("source is required.");

    GitCloner.CloneResult cloned;
    try
    {
        cloned = await GitCloner.CloneAsync(request.Source, cancellationToken);
    }
    catch (InvalidDataException ex)
    {
        return Problems.BadRequest(ex.Message);
    }
    catch (TimeoutException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status504GatewayTimeout);
    }

    try
    {
        var target = new Validation.BuildValidation.RepositoryTarget(
            cloned.RepoPath,
            CommitSha: string.IsNullOrWhiteSpace(cloned.ResolvedCommitSha) ? null : cloned.ResolvedCommitSha,
            Branch: request.Source.Branch);
        var options = new Validation.BuildValidation.PlanningOptions(cloned.EvidencePath);

        if (PathGuard.IsSameOrInside(cloned.RepoPath, cloned.EvidencePath))
            return Problems.BadRequest("Internal scratch layout invalid: evidence directory is inside repo.");
        if (store.EvidenceDirectoryIsInsideStorage(cloned.EvidencePath))
            return Problems.BadRequest("Internal scratch layout invalid: evidence directory is inside storage root.");
        if (store.StorageRootIsInsideTarget(cloned.RepoPath))
            return Problems.BadRequest("Internal scratch layout invalid: storage root is inside cloned repo.");

        PreparedValidation prepared;
        try
        {
            prepared = await workflow.PrepareAsync(request.MigrationPlan, target, options, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Problems.BadRequest("Repository or planning input failed validation. Check the supplied plan, branch, and commit.");
        }
        PlanSafety.Validate(prepared);
        var created = await store.CreatePlanAsync(prepared, cancellationToken);
        var response = PlanResponse.From(created.Metadata, includeProposal: request.IncludeProposal ? prepared : null);
        return Results.Created(response.Links.Self, response);
    }
    catch
    {
        try { if (Directory.Exists(cloned.ScratchRoot)) Directory.Delete(cloned.ScratchRoot, recursive: true); } catch { }
        throw;
    }
});

group.MapGet("/plans/{planId}", async (string planId, IValidationStore store, CancellationToken cancellationToken) =>
{
    var plan = await store.GetPlanAsync(planId, cancellationToken);
    return plan is null
        ? Problems.NotFound("Validation plan was not found.")
        : Results.Json(PlanResponse.From(plan.Metadata, plan.Prepared), ValidationJson.Options);
});

group.MapGet("/plans/{planId}/approval", async (string planId, IValidationStore store, CancellationToken cancellationToken) =>
{
    var plan = await store.GetPlanAsync(planId, cancellationToken);
    if (plan is null) return Problems.NotFound("Validation plan was not found.");
    var approval = plan.Approval ?? AutoApprove(plan.Prepared);
    return Results.Json(ApprovalResponse.From(plan.Metadata.PlanId, approval), ValidationJson.Options);
});

group.MapPut("/plans/{planId}/approval", async (
    string planId,
    UpdateApprovalRequest? request,
    IValidationStore store,
    CancellationToken cancellationToken) =>
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
});

group.MapPost("/plans/{planId}/runs", async (
    string planId,
    IValidationStore store,
    ValidationRunQueue queue,
    CancellationToken cancellationToken) =>
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
});

group.MapGet("/runs/{runId}", async (string runId, IValidationStore store, CancellationToken cancellationToken) =>
{
    var record = await store.GetRunAsync(runId, cancellationToken);
    return record is null
        ? Problems.NotFound("Validation run was not found.")
        : Results.Json(RunResponse.From(record), ValidationJson.Options);
});

group.MapGet("/runs/{runId}/report", async (string runId, IValidationStore store, CancellationToken cancellationToken) =>
{
    var record = await store.GetRunAsync(runId, cancellationToken);
    if (record is null) return Problems.NotFound("Validation run was not found.");
    if (record.Status != ValidationRunStatus.Completed) return Problems.TerminalOrPendingConflict(record, "report");
    var report = await store.GetReportAsync(runId, cancellationToken);
    return report is null ? Problems.NotFound("Validation report was not found.") : Results.Json(report, ValidationJson.Options);
});

group.MapGet("/runs/{runId}/dashboard", async (string runId, IValidationStore store, CancellationToken cancellationToken) =>
{
    var record = await store.GetRunAsync(runId, cancellationToken);
    if (record is null) return Problems.NotFound("Validation run was not found.");
    if (record.Status != ValidationRunStatus.Completed) return Problems.TerminalOrPendingConflict(record, "dashboard");
    var dashboard = await store.GetDashboardAsync(runId, cancellationToken);
    return dashboard is null ? Problems.NotFound("Validation dashboard was not found.") : Results.Json(dashboard, ValidationJson.Options);
});

app.Run();

static void CopyValidationJsonOptions(System.Text.Json.JsonSerializerOptions target)
{
    target.PropertyNamingPolicy = ValidationJson.Options.PropertyNamingPolicy;
    target.PropertyNameCaseInsensitive = ValidationJson.Options.PropertyNameCaseInsensitive;
    target.WriteIndented = ValidationJson.Options.WriteIndented;
    target.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false));
}

static PlanApproval AutoApprove(PreparedValidation prepared)
{
    return new PlanApproval(
        PlanSafety.Fingerprint(prepared),
        prepared.Commands.Select(command => command.Id).ToArray(),
        null);
}

static string? ValidateApproval(PreparedValidation prepared, PlanApproval approval)
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

static FoundryValidationAiOptions? FoundryFromConfiguration(IConfiguration configuration)
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

public partial class Program;

namespace Validation.Api
{
    public sealed record HealthResponse(string Status);
    public sealed record CreatePlanRequest(MigrationPlan MigrationPlan, RepositoryTarget Target, PlanningOptions Options, bool IncludeProposal = false);
    public sealed record UpdateApprovalRequest(IReadOnlyList<string> ApprovedCommandIds, IReadOnlyDictionary<string, string>? SkippedCommands = null, string? PlanFingerprint = null);
    public sealed record PlanLinks(string Self, string Approval, string Runs);
    public sealed record RunLinks(string Status, string Report, string Dashboard)
    {
        public static string StatusPath(string runId) => $"/api/v1/validation/runs/{runId}";
        public static RunLinks For(string runId) => new(StatusPath(runId), $"{StatusPath(runId)}/report", $"{StatusPath(runId)}/dashboard");
    }
    public sealed record PlanResponse(string PlanId, string Fingerprint, string Status, DateTimeOffset CreatedAt, PlanLinks Links, PreparedValidation? Proposal = null)
    {
        public static PlanResponse From(PlanMetadata metadata, PreparedValidation? includeProposal) => new(
            metadata.PlanId, metadata.Fingerprint, "prepared", metadata.CreatedAt,
            new($"/api/v1/validation/plans/{metadata.PlanId}", $"/api/v1/validation/plans/{metadata.PlanId}/approval", $"/api/v1/validation/plans/{metadata.PlanId}/runs"),
            includeProposal);
    }
    public sealed record ApprovalResponse(string PlanId, PlanApproval Approval)
    {
        public static ApprovalResponse From(string planId, PlanApproval approval) => new(planId, approval);
    }
    public sealed record RunResponse(string RunId, string PlanId, ValidationRunStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? Summary, string? Error, RunLinks Links)
    {
        public static RunResponse From(ValidationRunRecord record) => new(record.RunId, record.PlanId, record.Status, record.CreatedAt,
            record.StartedAt, record.FinishedAt, record.Summary, record.Error, RunLinks.For(record.RunId));
    }
    public sealed record PlanMetadata(string PlanId, string MigrationPlanId, string Fingerprint, DateTimeOffset CreatedAt);
    // Approval is null until an explicit PUT is stored; it is never auto-populated at plan creation.
    public sealed record StoredPlan(PlanMetadata Metadata, PreparedValidation Prepared, PlanApproval? Approval);
    public sealed record CreatedPlan(PlanMetadata Metadata, PreparedValidation Prepared);
    public enum ValidationRunStatus { Queued, Running, Completed, Failed, Cancelled }
    public sealed record ValidationRunRecord(
        string RunId,
        string PlanId,
        ValidationRunStatus Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt = null,
        DateTimeOffset? FinishedAt = null,
        string? Summary = null,
        string? Error = null);

    public sealed class ValidationApiOptions
    {
        public string? StorageRoot { get; set; }
        public int QueueCapacity { get; set; } = 100;
        public bool AllowNonLoopback { get; set; }
    }

    public static class Problems
    {
        public static IResult BadRequest(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
        public static IResult NotFound(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status404NotFound);
        public static IResult Conflict(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status409Conflict);

        // Failed/cancelled are terminal: a report/dashboard will never become available for this
        // run, unlike queued/running where polling again may eventually succeed. The message must
        // say so plainly instead of implying the artifact merely isn't ready yet.
        public static IResult TerminalOrPendingConflict(ValidationRunRecord record, string artifactName) => record.Status switch
        {
            ValidationRunStatus.Failed => Conflict(
                $"Validation run failed; no {artifactName} is available." + (string.IsNullOrWhiteSpace(record.Error) ? "" : $" Error: {record.Error}")),
            ValidationRunStatus.Cancelled => Conflict($"Validation run was cancelled; no {artifactName} is available."),
            _ => Conflict($"Validation {artifactName} is available only after the run completes.")
        };
    }

    // Input validation is handled at the request boundary. An exception escaping that boundary
    // (including InvalidDataException from persisted JSON) is an internal failure.
    public static class ExceptionMapping
    {
        public const string GenericServerErrorDetail = "An unexpected validation API error occurred.";

        public static (int Status, string Detail) Classify(Exception? error) =>
            (StatusCodes.Status500InternalServerError, GenericServerErrorDetail);
    }

    public static class RequestValidation
    {
        public static bool HasRequiredMigrationFields(MigrationPlan migration)
        {
            var plan = migration.ValidationPlan;
            if (string.IsNullOrWhiteSpace(migration.SchemaVersion) || string.IsNullOrWhiteSpace(migration.PlanId) ||
                plan?.TargetDevices is null || migration.WorkItems is null)
                return false;
            IReadOnlyList<ValidationCheck>?[] groups = [plan.BuildChecks, plan.FunctionalChecks,
                plan.ReliabilityChecks, plan.PerformanceChecks, plan.PowerChecks, plan.OfflineChecks,
                plan.AccessibilityChecks, plan.WindowsExperienceChecks];
            return groups.All(group => group is not null && group.All(check =>
                    check is not null && !string.IsNullOrWhiteSpace(check.Id) &&
                    check.Description is not null && check.ExpectedOutcome is not null)) &&
                migration.WorkItems.All(item => item is not null && !string.IsNullOrWhiteSpace(item.Id) &&
                    item.AcceptanceTests is not null && item.AcceptanceTests.All(test =>
                        test is not null && !string.IsNullOrWhiteSpace(test.Id) &&
                        test.Description is not null && test.ExpectedOutcome is not null));
        }
    }

    public interface IValidationWorkflowRunner
    {
        Task<PreparedValidation> PrepareAsync(MigrationPlan migration, RepositoryTarget target, PlanningOptions options, CancellationToken cancellationToken);
        Task<ValidationReport> RunAsync(PreparedValidation prepared, PlanApproval approval, CancellationToken cancellationToken);
    }

    public sealed class ValidationWorkflowRunner(
        IRepositoryInspector repositoryInspector,
        IProcessRunner processRunner,
        IServiceProvider services) : IValidationWorkflowRunner
    {
        public Task<PreparedValidation> PrepareAsync(MigrationPlan migration, RepositoryTarget target, PlanningOptions options, CancellationToken cancellationToken)
        {
            var workflow = CreateWorkflow();
            return workflow.PrepareAsync(migration, target, options, cancellationToken);
        }

        public Task<ValidationReport> RunAsync(PreparedValidation prepared, PlanApproval approval, CancellationToken cancellationToken)
        {
            var workflow = CreateWorkflow();
            return workflow.RunAsync(prepared, approval, cancellationToken);
        }

        private ValidationWorkflow CreateWorkflow() => new(
            repositoryInspector,
            processRunner,
            services.GetService<IValidationPlanner>(),
            services.GetService<IEvidenceAnalyzer>(),
            services.GetService<ICoverageReviewer>());
    }

    // Loopback-only by default. Setting AllowNonLoopback=true removes this API's only network
    // access control; it is unsafe unless the deployment adds its own authentication,
    // authorization, and network isolation in front of it. This API implements none of those
    // itself and must not be exposed to non-loopback callers without them.
    public sealed class LoopbackOnlyMiddleware(RequestDelegate next, IOptions<ValidationApiOptions> options)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            if (!options.Value.AllowNonLoopback &&
                context.Connection.RemoteIpAddress is { } remote &&
                !IPAddress.IsLoopback(remote))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await Results.Problem("Validation API accepts loopback requests only by default.", statusCode: StatusCodes.Status403Forbidden)
                    .ExecuteAsync(context);
                return;
            }
            await next(context);
        }
    }
}