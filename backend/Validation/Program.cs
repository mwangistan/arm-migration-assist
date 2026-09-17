using Validation.BuildValidation;
using Validation.Dashboard;

// Planning only inspects Git metadata. Running validation requires a separately reviewed approval file.
try
{
    if (args.Length >= 2 && args[0] == "--settings")
    {
        RunSettingsEnvironment.Load(args[1]);
        args = args[2..];
    }
    var processRunner = new LocalProcessRunner();
    var foundryOptions = FoundryValidationAiOptions.FromEnvironment();
    var foundry = foundryOptions is null ? null : FoundryValidationAiClient.Create(foundryOptions);
    var workflow = new ValidationWorkflow(
        new GitRepositoryInspector(processRunner), processRunner, foundry, foundry, foundry);
    if (args.Length is >= 6 and <= 8 && args[0] == "plan")
    {
        var migration = ValidationJson.Deserialize<MigrationPlan>(await File.ReadAllTextAsync(args[1]));
        var options = ValidationJson.Deserialize<PlanningOptions>(await File.ReadAllTextAsync(args[3]));
        var prepared = await workflow.PrepareAsync(migration,
            new(args[2], args.Length >= 7 ? args[6] : null, args.Length == 8 ? args[7] : null), options);
        await WriteNewAsync(args[4], ValidationJson.Serialize(prepared));
        // Deliberately approve nothing; a human must populate command IDs after reviewing the proposal.
        await WriteNewAsync(args[5], ValidationJson.Serialize(new PlanApproval(PlanSafety.Fingerprint(prepared), [])));
        Console.WriteLine($"Prepared {prepared.Commands.Count} commands; none approved. Fingerprint: {PlanSafety.Fingerprint(prepared)}");
        return 0;
    }
    if (args.Length == 5 && args[0] == "run")
    {
        if (File.Exists(args[3]) || File.Exists(args[4]))
            throw new InvalidDataException("Output files already exist; choose new paths to preserve prior evidence.");
        var prepared = ValidationJson.Deserialize<PreparedValidation>(await File.ReadAllTextAsync(args[1]));
        var approval = ValidationJson.Deserialize<PlanApproval>(await File.ReadAllTextAsync(args[2]));
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var report = await workflow.RunAsync(prepared, approval, cancellation.Token);
        await WriteNewAsync(args[3], ValidationJson.Serialize(report));
        await WriteNewAsync(args[4], ValidationDashboard.FromReport(report).ToJson());
        Console.WriteLine($"{ValidationJson.Serialize(report.Scorecard.Status)}: {report.Scorecard.Passed} passed, " +
            $"{report.Scorecard.Failed} failed, {report.Scorecard.NotRun} not-run, {report.Scorecard.Inconclusive} inconclusive.");
        return report.Scorecard.Status == OverallStatus.Validated ? 0 :
            report.Scorecard.Status == OverallStatus.ValidationFailed ? 1 : 2;
    }
    Console.Error.WriteLine(
        "Usage:\n" +
        "  [--settings <validation.runsettings>] plan <migration.json> <repo> <options.json> <proposal.json> <approval.json> [full-commit-sha] [branch]\n" +
        "  [--settings <validation.runsettings>] run <proposal.json> <approval.json> <report.json> <dashboard.json>\n" +
        $"Set {FoundryValidationAiOptions.EndpointEnvironmentVariable} and " +
        $"{FoundryValidationAiOptions.DeploymentEnvironmentVariable} to enable Foundry AI stages.\n" +
        "No commands execute during planning. Empty approval lists execute nothing.");
    return 2;
}
catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or OperationCanceledException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static async Task WriteNewAsync(string path, string content)
{
    var full = Path.GetFullPath(path);
    RepositoryPaths.RejectLinks(full);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    await using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    await using var writer = new StreamWriter(stream);
    await writer.WriteAsync(content);
}
