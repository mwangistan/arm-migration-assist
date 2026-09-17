using System.Text.RegularExpressions;
using System.Xml;

namespace Validation.BuildValidation;

public sealed class DeterministicPlanner
{
    public PreparedValidation Prepare(MigrationPlan migration, RepositoryContext repository, PlanningOptions options)
    {
        PlanSafety.Require(Regex.IsMatch(migration.SchemaVersion ?? "", @"\A1\.\d{1,3}\z"),
            "Only MigrationPlanV1 (1.x) is supported.");
        PlanSafety.Require(migration.ValidationPlan is not null && migration.WorkItems is not null,
            "Migration validationPlan and workItems are required.");
        var validation = migration.ValidationPlan!;
        PlanSafety.Require(validation.TargetDevices is { Count: > 0 } &&
            new[] { validation.BuildChecks, validation.FunctionalChecks, validation.ReliabilityChecks,
                    validation.PerformanceChecks, validation.PowerChecks, validation.OfflineChecks,
                    validation.AccessibilityChecks, validation.WindowsExperienceChecks }.All(list => list is not null),
            "All ValidationPlan categories and targetDevices are required.");
        var criteria = validation.Checks().Select(item => new Criterion(
            $"validation:{item.Check.Id}", CheckSource.ValidationCheck, item.Check.Id, null, item.Category,
            item.Check.Description, item.Check.ExpectedOutcome, item.Check.EvidenceIds ?? [], item.Check.GuidanceIds ?? []))
            .ToList();
        foreach (var workItem in migration.WorkItems!)
        {
            PlanSafety.Require(workItem.AcceptanceTests is not null, $"acceptanceTests required: {workItem.Id}");
            criteria.AddRange(workItem.AcceptanceTests!.Select(test => new Criterion(
                $"acceptance:{workItem.Id}:{test.Id}", CheckSource.AcceptanceTest, test.Id, workItem.Id,
                "acceptance", test.Description, test.ExpectedOutcome, [], [])));
        }
        var commands = new List<ValidationCommand>();
        var notices = repository.Notices.Select(message => new StageNotice("discovery", message)).ToList();
        string planId = Guid.NewGuid().ToString("N");
        string evidenceDirectory = Path.GetFullPath(options.EvidenceDirectory);

        string AddCriterion(string id, string category, string description, string expected)
        {
            string key = "discovered:" + id;
            if (criteria.All(c => c.Key != key))
                criteria.Add(new(key, CheckSource.Discovered, id, null, category, description, expected, [], []));
            return key;
        }

        ValidationCommand AddCommand(
            string id, string description, CommandKind kind, ExecutionSurface surface,
            string executable, IReadOnlyList<string> arguments, IReadOnlyList<string>? dependencies = null,
            IReadOnlyList<string>? extraKeys = null, ProofKind proof = ProofKind.ExitCode, string? proofPath = null)
        {
            string key = AddCriterion(id, kind is CommandKind.DotNetTest or CommandKind.NativeSmoke or CommandKind.ContainerSmoke
                ? "functional" : "build", description,
                proof == ProofKind.Trx ? "A fresh TRX proves at least one executed test and all tests pass." :
                proof == ProofKind.ContainerArchitecture ? "Image metadata reports linux/arm64." : "Approved command exits zero.");
            var command = new ValidationCommand(id, description, kind, surface, executable, arguments, ".",
                new Dictionary<string, string>(), 600, dependencies ?? [], new[] { key }.Concat(extraKeys ?? []).ToArray(),
                proof, proofPath);
            commands.Add(command);
            return command;
        }

        var nativeSmoke = options.NativeSmokeChecks ?? [];
        var containerSmoke = options.ContainerSmokeChecks ?? [];
        var consumedNative = new HashSet<NativeSmokeCheck>();
        var consumedContainer = new HashSet<ContainerSmokeCheck>();
        foreach (var artifact in repository.Artifacts)
        {
            string path = artifact.Path;
            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                string buildId = PlanSafety.StableId("dotnet-build", path);
                AddCommand(buildId, $"Build {path} for win-arm64", CommandKind.DotNetBuild,
                    ExecutionSurface.WindowsArm64Build, "dotnet",
                    ["build", path, "--configuration", "Release", "--runtime", "win-arm64"]);
                string runtimeKey = AddCriterion("windows-arm64-runtime", "functional",
                    "Execute Windows ARM64 tests or an explicitly supplied native smoke check.",
                    "Runtime evidence from a Windows ARM64 runner, not a cross-build.");
                bool isTest = false;
                string[] frameworks = [];
                try
                {
                    var xml = SafeXml.Parse(artifact.Content);
                    isTest = xml.Descendants().Any(element =>
                        element.Name.LocalName == "IsTestProject" && element.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
                        element.Name.LocalName == "PackageReference" &&
                        string.Equals((string?)element.Attribute("Include"), "Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase));
                    var declarations = xml.Descendants().Where(element =>
                        element.Name.LocalName is "TargetFramework" or "TargetFrameworks").ToArray();
                    if (declarations.Length == 1 && !declarations[0].HasElements &&
                        declarations[0].AncestorsAndSelf().All(element => element.Attribute("Condition") is null))
                    {
                        string[] declared = declarations[0].Value.Split(';', StringSplitOptions.TrimEntries);
                        if (declared.All(PlanSafety.IsLiteralFramework) &&
                            declared.Distinct(StringComparer.OrdinalIgnoreCase).Count() == declared.Length &&
                            (declarations[0].Name.LocalName == "TargetFrameworks" || declared.Length == 1))
                            frameworks = declared;
                    }
                }
                catch (XmlException ex)
                {
                    notices.Add(new("discovery", $"Cannot inspect test metadata in {path}: {ex.Message}"));
                }
                if (isTest)
                {
                    string testId = PlanSafety.StableId("dotnet-test", path);
                    string testKey = AddCriterion(testId, "functional", $"Test {path} on Windows ARM64",
                        "Fresh per-framework TRX evidence proves tests executed and passed for every declared target framework.");
                    if (frameworks.Length == 0)
                        notices.Add(new("discovery",
                            $"{path}: no unambiguous literal target frameworks; tests remain manual without per-framework proof."));
                    foreach (string framework in frameworks)
                    {
                        string id = frameworks.Length == 1 ? testId : PlanSafety.StableId("dotnet-test", path + "\0" + framework);
                        string trx = Path.Combine(evidenceDirectory, planId, id, "results.trx");
                        AddCommand(id, $"Test {path} ({framework}) on Windows ARM64", CommandKind.DotNetTest,
                            ExecutionSurface.WindowsArm64Runtime, "dotnet",
                            ["test", path, "--configuration", "Release", "--runtime", "win-arm64", "--framework", framework,
                             "--logger", "trx;LogFileName=results.trx", "--results-directory", Path.GetDirectoryName(trx)!],
                            [buildId], new[] { runtimeKey, testKey }.Where(key => key != "discovered:" + id).ToArray(), ProofKind.Trx, trx);
                    }
                }
            }
            else if (path.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase))
            {
                string runtimeKey = AddCriterion("windows-arm64-runtime", "functional",
                    "Execute Windows ARM64 tests or an explicitly supplied native smoke check.",
                    "Runtime evidence from a Windows ARM64 runner, not a cross-build.");
                string? configuration = null;
                try
                {
                    configuration = SafeXml.Parse(artifact.Content).Descendants()
                        .Where(e => e.Name.LocalName == "ProjectConfiguration")
                        .Select(e => (string?)e.Attribute("Include")).Where(value => value is not null)
                        .OrderBy(value => value!.StartsWith("Release|", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .FirstOrDefault(value => value!.EndsWith("|ARM64", StringComparison.OrdinalIgnoreCase))
                        ?.Split('|')[0];
                }
                catch (XmlException ex) { notices.Add(new("discovery", $"Cannot inspect {path}: {ex.Message}")); }
                if (configuration is null)
                {
                    AddCriterion(PlanSafety.StableId("native-build", path), "build",
                        $"Build {path} for ARM64", "A declared ARM64 MSBuild configuration builds successfully.");
                    notices.Add(new("discovery", $"{path}: no explicit ARM64 configuration; no native build command inferred."));
                    continue;
                }
                string buildId = PlanSafety.StableId("native-build", path);
                AddCommand(buildId, $"Build {path} ({configuration}|ARM64)", CommandKind.NativeBuild,
                    ExecutionSurface.WindowsArm64Build, "msbuild", [path, "/t:Build", $"/p:Configuration={configuration}", "/p:Platform=ARM64"]);
                foreach (var smoke in nativeSmoke.Where(smoke => smoke.ProjectPath == path))
                {
                    consumedNative.Add(smoke);
                    // Resolve a supplied executable inside the repository; it can be produced by the build.
                    string executable = RepositoryPaths.ResolveWithin(repository.RootPath, smoke.Executable);
                    AddCommand(PlanSafety.StableId("native-smoke", path + smoke.Executable), $"Native ARM64 smoke: {path}",
                        CommandKind.NativeSmoke, ExecutionSurface.WindowsArm64Runtime, executable, smoke.Arguments,
                        [buildId], smoke.CriterionKeys.Concat([runtimeKey]).ToArray());
                }
            }
            else if (Path.GetFileName(path) == "Dockerfile")
            {
                string runtimeKey = AddCriterion("linux-arm64-container-runtime", "functional",
                    "Execute an explicitly supplied Linux ARM64 container smoke check.",
                    "Container smoke passes; this does not validate Windows on Arm or native hardware performance.");
                string buildId = PlanSafety.StableId("container-build", path);
                string image = $"arm-validation:{planId}-{buildId}";
                var build = AddCommand(buildId, $"Build Linux ARM64 container from {path} (container validation)",
                    CommandKind.ContainerBuild, ExecutionSurface.LinuxArm64Container, "docker",
                    ["build", "--platform", "linux/arm64", "--tag", image, "--file", path,
                        Path.GetDirectoryName(path) is { Length: > 0 } directory ? directory : "."]);
                string inspectId = PlanSafety.StableId("container-inspect", path);
                AddCommand(inspectId, $"Verify linux/arm64 image metadata for {path} (container validation)",
                    CommandKind.ContainerInspect, ExecutionSurface.LinuxArm64Container, "docker",
                    ["image", "inspect", "--format", "{{.Os}}/{{.Architecture}}", image],
                    [buildId], build.CriterionKeys, ProofKind.ContainerArchitecture);
                foreach (var smoke in containerSmoke.Where(smoke => smoke.DockerfilePath == path))
                {
                    PlanSafety.Require(smoke.Arguments.Count > 0, "Container smoke requires an explicit command.");
                    consumedContainer.Add(smoke);
                    AddCommand(PlanSafety.StableId("container-smoke", path + string.Join('\0', smoke.Arguments)),
                        $"Smoke Linux ARM64 image from {path} (container validation)",
                        CommandKind.ContainerSmoke, ExecutionSurface.LinuxArm64Container, "docker",
                        new[] { "run", "--rm", "--platform", "linux/arm64", image }.Concat(smoke.Arguments).ToArray(),
                        [inspectId], smoke.CriterionKeys.Concat([runtimeKey]).ToArray());
                }
            }
        }
        PlanSafety.Require(consumedNative.Count == nativeSmoke.Count && consumedContainer.Count == containerSmoke.Count,
            "A supplied smoke check does not match a supported, discovered build artifact.");
        if (commands.Count == 0)
            AddCriterion("supported-build", "build", "No executable build was conservatively detected.",
                "Supply and approve repository-specific build and runtime checks.");
        foreach (var target in validation.TargetDevices)
            AddCriterion("target-" + target, "device", $"Validate target device: {target}",
                target == "x64-baseline-for-comparison"
                    ? "Record comparable x64 and ARM64 measurements for the same scenario."
                    : "Record explicit evidence identifying and exercising the requested device class.");

        var plan = new PreparedValidation("1.0", planId, migration.PlanId, repository, evidenceDirectory,
            validation.TargetDevices, criteria, commands, [], notices);
        plan = ApplyMappings(plan, options.Mappings ?? []);
        plan = WithUnmappedManualChecks(plan);
        PlanSafety.Validate(plan);
        return plan;
    }

    internal static PreparedValidation ApplyMappings(PreparedValidation plan, IReadOnlyList<CommandMapping> mappings)
    {
        var ids = plan.Commands.Select(command => command.Id).ToHashSet(StringComparer.Ordinal);
        PlanSafety.Require(mappings.All(mapping => ids.Contains(mapping.CommandId)), "Mapping references an unknown command.");
        var mapped = plan.Commands.Select(command => command with
        {
            CriterionKeys = command.CriterionKeys.Concat(mappings.Where(mapping => mapping.CommandId == command.Id)
                .SelectMany(mapping => mapping.CriterionKeys)).Distinct(StringComparer.Ordinal).ToArray()
        }).ToArray();
        return plan with
        {
            Commands = mapped.Select(command => command.Kind == CommandKind.ContainerInspect ? command with
            {
                CriterionKeys = command.CriterionKeys.Concat(mapped.Where(parent => parent.Kind == CommandKind.ContainerBuild &&
                    command.DependsOn.Contains(parent.Id)).SelectMany(parent => parent.CriterionKeys)).Distinct(StringComparer.Ordinal).ToArray()
            } : command).ToArray()
        };
    }

    internal static PreparedValidation WithUnmappedManualChecks(PreparedValidation plan)
    {
        var mapped = plan.Commands.SelectMany(command => command.CriterionKeys).ToHashSet(StringComparer.Ordinal);
        var manual = plan.ManualChecks.Where(check => !mapped.Contains(check.CriterionKey)).ToList();
        manual.AddRange(plan.Criteria.Where(criterion => !mapped.Contains(criterion.Key) &&
                manual.All(check => check.CriterionKey != criterion.Key))
            .Select(criterion => new ManualCheck(criterion.Key, criterion.ExpectedOutcome,
                "No approved executable mapping is known. Manual or future observed evidence is still required.")));
        return plan with { ManualChecks = manual };
    }
}
