using System.Text.Json.Serialization;

namespace AutomatedMigration.Api.Publication;

// Wire-level request block for opting the job into publishing to GitHub.
public sealed record PublishRequest(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("branchName")] string BranchName,
    [property: JsonPropertyName("prTitle")] string? PrTitle = null,
    [property: JsonPropertyName("prBody")] string? PrBody = null,
    [property: JsonPropertyName("upstreamDefaultBranch")] string? UpstreamDefaultBranch = null);

// What the worker records on the job after attempting to publish. Attempted=false means
// the caller did not opt in (or the publisher was not configured on the server); Attempted=true
// with a non-null Error means the publish was tried and failed — the job itself still succeeds.
public sealed record PublicationResult(
    [property: JsonPropertyName("attempted")] bool Attempted,
    [property: JsonPropertyName("forkFullName")] string? ForkFullName,
    [property: JsonPropertyName("branchName")] string? BranchName,
    [property: JsonPropertyName("baseCommitSha")] string? BaseCommitSha,
    [property: JsonPropertyName("headCommitSha")] string? HeadCommitSha,
    [property: JsonPropertyName("pullRequestUrl")] string? PullRequestUrl,
    [property: JsonPropertyName("patchesApplied")] int PatchesApplied,
    [property: JsonPropertyName("patchesFailed")] int PatchesFailed,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("validation")] ValidationDispatch? Validation = null);
