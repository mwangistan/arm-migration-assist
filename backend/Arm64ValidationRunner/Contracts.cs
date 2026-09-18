using System.Text.Json.Serialization;

namespace Arm64ValidationRunner;

public sealed record RunRequest(
    [property: JsonPropertyName("sourceUrl")] string SourceUrl,
    [property: JsonPropertyName("baseCommitSha")] string BaseCommitSha,
    [property: JsonPropertyName("branch")] string? Branch,
    [property: JsonPropertyName("patches")] IReadOnlyList<PatchInput> Patches,
    [property: JsonPropertyName("projectHints")] ProjectHints? ProjectHints = null);

public sealed record PatchInput(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("diff")] string Diff);

public sealed record ProjectHints(
    [property: JsonPropertyName("primaryLanguage")] string? PrimaryLanguage,
    [property: JsonPropertyName("dotnetProjectPaths")] IReadOnlyList<string>? DotnetProjectPaths,
    [property: JsonPropertyName("pythonRequirementsPaths")] IReadOnlyList<string>? PythonRequirementsPaths,
    [property: JsonPropertyName("nodeManifestPaths")] IReadOnlyList<string>? NodeManifestPaths);

public sealed record JobEnvelope(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("statusUrl")] string StatusUrl);

public sealed record JobStatus(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("startedAt")] DateTimeOffset? StartedAt,
    [property: JsonPropertyName("finishedAt")] DateTimeOffset? FinishedAt,
    [property: JsonPropertyName("scorecard")] Arm64Scorecard? Scorecard,
    [property: JsonPropertyName("error")] string? Error);

public sealed record Arm64Scorecard(
    [property: JsonPropertyName("hardware")] HardwareInfo Hardware,
    [property: JsonPropertyName("sourceCommitSha")] string SourceCommitSha,
    [property: JsonPropertyName("resolvedCommitSha")] string ResolvedCommitSha,
    [property: JsonPropertyName("patchApplication")] PatchApplicationReport PatchApplication,
    [property: JsonPropertyName("build")] StepOutcome? Build,
    [property: JsonPropertyName("tests")] StepOutcome? Tests,
    [property: JsonPropertyName("wallClockSeconds")] double WallClockSeconds,
    [property: JsonPropertyName("summary")] string Summary);

public sealed record HardwareInfo(
    [property: JsonPropertyName("vmSku")] string VmSku,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("architecture")] string Architecture,
    [property: JsonPropertyName("kernel")] string Kernel,
    [property: JsonPropertyName("cpuModel")] string CpuModel,
    [property: JsonPropertyName("cpuCount")] int CpuCount,
    [property: JsonPropertyName("memoryMB")] long MemoryMB);

public sealed record PatchApplicationReport(
    [property: JsonPropertyName("applied")] IReadOnlyList<string> Applied,
    [property: JsonPropertyName("rejected")] IReadOnlyList<PatchRejection> Rejected);

public sealed record PatchRejection(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record StepOutcome(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("workingDirectory")] string WorkingDirectory,
    [property: JsonPropertyName("exitCode")] int ExitCode,
    [property: JsonPropertyName("succeeded")] bool Succeeded,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("stdoutTail")] string StdoutTail,
    [property: JsonPropertyName("stderrTail")] string StderrTail);
