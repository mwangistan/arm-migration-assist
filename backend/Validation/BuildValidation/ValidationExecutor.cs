using System.Globalization;
using System.Xml;

namespace Validation.BuildValidation;

public sealed record ExecutionBatch(
    RunnerContext Runner, IReadOnlyList<CommandResult> Results, IReadOnlyList<ExecutionEvidence> Evidence);

public sealed class ValidationExecutor(
    IRepositoryInspector repositoryInspector, IProcessRunner processRunner, RunnerContext? runner = null)
{
    public async Task<ExecutionBatch> ExecuteAsync(
        PreparedValidation plan, PlanApproval? approval, CancellationToken cancellationToken = default)
    {
        PlanSafety.Validate(plan);
        var host = runner ?? RunnerEnvironment.Current;
        var results = new List<CommandResult>();
        var evidence = new List<ExecutionEvidence>();
        var approved = approval?.ApprovedCommandIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        var skipped = approval?.SkippedCommands ?? new Dictionary<string, string>();
        string? blocked = null;
        if (approval is null)
            blocked = "No approval supplied. No validation commands were executed.";
        else if (!string.Equals(PlanSafety.Fingerprint(plan), approval.PlanFingerprint, StringComparison.Ordinal))
            blocked = "Approval fingerprint does not match the complete plan. Re-review and approve this plan.";
        else if (approved.Any(id => plan.Commands.All(command => command.Id != id)))
            blocked = "Approval contains unknown command IDs.";
        else if (skipped.Any(pair => approved.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Value) ||
                     plan.Commands.All(command => command.Id != pair.Key)))
            blocked = "Skipped commands require a reason, a known ID, and must not also be approved.";

        if (blocked is null)
        {
            try
            {
                var current = await repositoryInspector.InspectAsync(
                    new(plan.Repository.RootPath, plan.Repository.CommitSha, plan.Repository.Branch), cancellationToken);
                if (current.RootPath != plan.Repository.RootPath || current.CommitSha != plan.Repository.CommitSha ||
                    current.Branch != plan.Repository.Branch ||
                    !current.Artifacts.Select(a => (a.Path, a.Sha256))
                        .SequenceEqual(plan.Repository.Artifacts.Select(a => (a.Path, a.Sha256))))
                    blocked = "Repository identity or discovered artifacts changed after planning.";
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                blocked = $"Repository verification prevented execution: {ex.Message}";
            }
        }
        string verificationId = Guid.NewGuid().ToString("N");
        evidence.Add(new(verificationId, null, "repository-verification", null, null,
            blocked ?? $"Verified clean repository {plan.Repository.RootPath} at {plan.Repository.CommitSha}" +
            $" ({plan.Repository.Branch ?? "detached HEAD"}).", []));

        foreach (var command in plan.Commands)
        {
            string workingDirectory;
            try { workingDirectory = RepositoryPaths.ResolveWithin(plan.Repository.RootPath, command.WorkingDirectory); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                string id = Guid.NewGuid().ToString("N");
                evidence.Add(new(id, command.Id, "execution-decision", null, null, ex.Message, []));
                results.Add(new(command.Id, ResultStatus.NotRun, ex.Message, [verificationId, id]));
                continue;
            }
            var invocation = new ProcessInvocation(command.Executable, command.Arguments,
                workingDirectory,
                RunnerEnvironment.Capture(command.Environment), command.TimeoutSeconds);
            string? reason = blocked;
            if (blocked is null && skipped.TryGetValue(command.Id, out var skipReason))
            {
                string id = Guid.NewGuid().ToString("N");
                evidence.Add(new(id, command.Id, "execution-decision", RunnerEnvironment.Redact(invocation), null, skipReason, []));
                results.Add(new(command.Id, ResultStatus.Skipped, skipReason, [verificationId, id]));
                continue;
            }
            if (reason is null && !approved.Contains(command.Id)) reason = "Command was not approved.";
            if (reason is null && cancellationToken.IsCancellationRequested) reason = "Run cancelled before command start.";
            if (reason is null && command.DependsOn.Any(id => results.Single(result => result.CommandId == id).Status != ResultStatus.Passed))
                reason = "A prerequisite command did not pass.";
            if (reason is null) reason = RunnerGap(command, host);
            if (reason is null && !Directory.Exists(invocation.WorkingDirectory)) reason = "Working directory is unavailable.";
            if (reason is not null)
            {
                string id = Guid.NewGuid().ToString("N");
                evidence.Add(new(id, command.Id, "execution-decision", RunnerEnvironment.Redact(invocation), null, reason, []));
                results.Add(new(command.Id, ResultStatus.NotRun, reason, [verificationId, id]));
                continue;
            }

            var artifacts = new List<ArtifactReference>();
            ProcessOutcome? outcome = null;
            ResultStatus status = ResultStatus.Inconclusive;
            var evidenceDiagnostics = new List<string>();
            void EvidenceError(Exception error)
            {
                string diagnostic = $"Evidence unavailable: {error.Message}";
                evidenceDiagnostics.Add(diagnostic);
                if (status == ResultStatus.Passed) status = ResultStatus.Inconclusive;
                reason = $"{reason} {diagnostic}";
            }
            try
            {
                if (command.Proof == ProofKind.Trx)
                {
                    RepositoryPaths.RejectLinks(command.ProofPath!);
                    if (File.Exists(command.ProofPath))
                        throw new InvalidDataException("Proof file already exists. Create a new plan to avoid stale test evidence.");
                    Directory.CreateDirectory(Path.GetDirectoryName(command.ProofPath!)!);
                }
                if (command.Kind == CommandKind.NativeSmoke)
                    VerifyArm64Executable(command.Executable);
                outcome = await processRunner.RunAsync(invocation, cancellationToken);
                (status, reason) = Classify(outcome);
                if (status == ResultStatus.Passed && command.Proof == ProofKind.Trx)
                {
                    try
                    {
                        RepositoryPaths.RejectLinks(command.ProofPath!);
                        (status, reason) = ReadTestProof(command.ProofPath!);
                    }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or XmlException)
                    {
                        EvidenceError(ex);
                    }
                }
                if (command.Proof == ProofKind.Trx && File.Exists(command.ProofPath))
                {
                    try
                    {
                        RepositoryPaths.RejectLinks(command.ProofPath!);
                        var info = new FileInfo(command.ProofPath!);
                        artifacts.Add(new(command.ProofPath!, RepositoryPaths.HashFile(command.ProofPath!), info.Length));
                    }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                    {
                        EvidenceError(ex);
                    }
                }
                if (status == ResultStatus.Passed && command.Proof == ProofKind.ContainerArchitecture &&
                    (outcome.OutputTruncated || outcome.StandardOutput.Trim() != "linux/arm64"))
                    (status, reason) = (ResultStatus.Failed, "Image metadata did not prove linux/arm64.");
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or XmlException or OperationCanceledException)
            {
                if (outcome is null)
                    (status, reason) = (ResultStatus.Inconclusive, $"Execution/evidence unavailable: {ex.Message}");
                else EvidenceError(ex);
            }
            var commandEvidenceIds = new List<string> { verificationId };
            foreach (string diagnostic in evidenceDiagnostics)
            {
                string id = Guid.NewGuid().ToString("N");
                evidence.Add(new(id, command.Id, "evidence-diagnostic", null, null, diagnostic, []));
                commandEvidenceIds.Add(id);
            }
            string evidenceId = Guid.NewGuid().ToString("N");
            evidence.Add(new(evidenceId, command.Id, "command-execution", RunnerEnvironment.Redact(invocation),
                outcome, reason!, artifacts));
            commandEvidenceIds.Add(evidenceId);
            results.Add(new(command.Id, status, reason!, commandEvidenceIds));
        }
        // Build scripts are executable code and can modify sources. Do not attribute their results to
        // the approved commit if the materialized tree changed while commands were running.
        if (evidence.Any(item => item.Process is not null))
        {
            string? provenanceError = null;
            try
            {
                var current = await repositoryInspector.InspectAsync(
                    new(plan.Repository.RootPath, plan.Repository.CommitSha, plan.Repository.Branch), CancellationToken.None);
                if (current.RootPath != plan.Repository.RootPath || current.CommitSha != plan.Repository.CommitSha ||
                    current.Branch != plan.Repository.Branch ||
                    !current.Artifacts.Select(a => (a.Path, a.Sha256))
                        .SequenceEqual(plan.Repository.Artifacts.Select(a => (a.Path, a.Sha256))))
                    provenanceError = "Repository identity or build artifacts changed during execution.";
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                provenanceError = $"Post-run repository verification failed: {ex.Message}";
            }
            string id = Guid.NewGuid().ToString("N");
            evidence.Add(new(id, null, "repository-verification-after-run", null, null,
                provenanceError ?? $"Repository remains clean at {plan.Repository.CommitSha}.", []));
            if (provenanceError is not null)
            {
                for (int index = 0; index < results.Count; index++)
                    if (results[index].Status == ResultStatus.Passed)
                        results[index] = results[index] with
                        {
                            Status = ResultStatus.Inconclusive,
                            Reason = provenanceError,
                            EvidenceIds = results[index].EvidenceIds.Append(id).ToArray()
                        };
            }
        }
        return new(host, results, evidence);
    }

    private static string? RunnerGap(ValidationCommand command, RunnerContext host)
    {
        if (command.Kind == CommandKind.NativeBuild && host.OperatingSystem != "windows")
            return "Native MSBuild validation requires a Windows runner and ARM64 C++ tools.";
        if (command.Surface == ExecutionSurface.WindowsArm64Runtime &&
            (host.OperatingSystem != "windows" || host.Architecture != "arm64"))
            return "Windows ARM64 runtime validation requires a Windows ARM64 runner; cross-builds are not runtime evidence.";
        return null;
    }

    private static (ResultStatus, string) Classify(ProcessOutcome outcome)
    {
        if (outcome.StartError is not null) return (ResultStatus.NotRun, $"Tool could not start: {outcome.StartError}");
        if (outcome.Cancelled) return (ResultStatus.Inconclusive, "Command was cancelled; completion is unproven.");
        if (outcome.TimedOut) return (ResultStatus.Inconclusive, "Command timed out; completion is unproven.");
        if (outcome.OutputIncomplete) return (ResultStatus.Inconclusive, "Output capture or process cleanup was incomplete.");
        if (outcome.ExitCode is null) return (ResultStatus.Inconclusive, "No exit code was captured.");
        if (outcome.ExitCode != 0)
        {
            string output = outcome.StandardOutput + "\n" + outcome.StandardError;
            if (new[] { "MSB8020", "MSB8036", "NETSDK1045", "No .NET SDKs were found",
                    "You must install or update .NET", "Cannot connect to the Docker daemon",
                    "error during connect", "failed to connect to the docker API" }
                .Any(marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                return (ResultStatus.Inconclusive, "Required SDK, workload, runtime or Docker daemon is unavailable; see command evidence.");
            return (ResultStatus.Failed, $"Command exited with code {outcome.ExitCode}.");
        }
        return (ResultStatus.Passed, "Approved command completed with exit code zero.");
    }

    private static (ResultStatus, string) ReadTestProof(string path)
    {
        if (!File.Exists(path)) return (ResultStatus.Inconclusive, "No fresh TRX was produced; exit zero alone does not prove tests ran.");
        if (new FileInfo(path).Length > 10_485_760) return (ResultStatus.Inconclusive, "TRX exceeds the bounded parser limit.");
        var xml = SafeXml.Parse(File.ReadAllText(path));
        if (xml.Root?.Name.LocalName != "TestRun")
            return (ResultStatus.Inconclusive, "Evidence is not a TRX TestRun.");
        var summaries = xml.Root.Elements().Where(element => element.Name.LocalName == "ResultSummary").ToArray();
        if (summaries.Length != 1) return (ResultStatus.Inconclusive, "TRX must contain exactly one result summary.");
        if (string.Equals((string?)summaries[0].Attribute("outcome"), "Failed", StringComparison.OrdinalIgnoreCase) ||
            xml.Descendants().Any(element => element.Name.LocalName == "UnitTestResult" &&
                string.Equals((string?)element.Attribute("outcome"), "Failed", StringComparison.OrdinalIgnoreCase)))
            return (ResultStatus.Failed, "TRX contains a failing result or summary.");
        var counters = summaries[0].Elements().Where(element => element.Name.LocalName == "Counters").ToArray();
        if (counters.Length != 1) return (ResultStatus.Inconclusive, "TRX must contain exactly one test summary.");
        bool Read(string name, out long value) => long.TryParse((string?)counters[0].Attribute(name),
            NumberStyles.None, CultureInfo.InvariantCulture, out value);
        if (!Read("total", out long total) || !Read("executed", out long executed) ||
            !Read("passed", out long passed) || !Read("failed", out long failed))
            return (ResultStatus.Inconclusive, "TRX summary is incomplete.");
        if (failed > 0 || new[] { "error", "timeout", "aborted" }.Any(name => Read(name, out long count) && count > 0))
            return (ResultStatus.Failed, "TRX records failing, errored, timed-out or aborted tests.");
        if (total == 0 || executed == 0) return (ResultStatus.Inconclusive, "TRX reports no executed tests.");
        if (total != executed || passed != executed ||
            new[] { "inconclusive", "notExecuted", "disconnected" }.Any(name => Read(name, out long count) && count > 0))
            return (ResultStatus.Inconclusive, "TRX includes unexecuted or inconclusive tests.");
        return (ResultStatus.Passed, $"Fresh TRX proves all {executed} tests executed and passed.");
    }

    private static void VerifyArm64Executable(string path)
    {
        RepositoryPaths.RejectLinks(path);
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d)
            throw new InvalidDataException("Native smoke executable is not a Windows PE binary.");
        stream.Position = 0x3c;
        int peOffset = reader.ReadInt32();
        if (peOffset < 0 || peOffset > stream.Length - 6)
            throw new InvalidDataException("Native smoke executable has an invalid PE header.");
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0xaa64)
            throw new InvalidDataException("Native smoke executable is not ARM64; x64 emulation is not native ARM64 validation.");
    }
}
