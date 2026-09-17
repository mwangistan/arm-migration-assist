using AutomatedMigration.Generators;
using AutomatedMigration.Models;

namespace AutomatedMigration.PipelineUpdates;

// Story 3.2 — adds an ARM64 build + validation stage to CI.
// - Detects the CI system (GitHub Actions vs Azure Pipelines).
// - Derives the build command from the build-config work item this one depends
//   on (3.1 feeds 3.2), so the pipeline matches the stack without re-detecting.
public sealed class PipelineGenerator : IMigrationGenerator
{
    private enum BuildKind { Docker, DotNet, Native, Unknown }
    private enum CiSystem { GitHubActions, AzurePipelines, None }

    public GeneratedPatch? Generate(WorkItem workItem, MigrationContext context)
    {
        var kind = ResolveBuildKind(workItem, context.WorkItemsById);
        var ci = DetectCi(context.RepoPath);

        var (path, yaml) = ci == CiSystem.AzurePipelines
            ? AzurePipeline(kind)
            : GitHubWorkflow(kind);

        return new GeneratedPatch(UnifiedDiff.NewFile(path, yaml));
    }

    // 3.1 feeds 3.2: the CI build command comes from the dependency's build file.
    private static BuildKind ResolveBuildKind(WorkItem ci, IReadOnlyDictionary<string, WorkItem> byId)
    {
        foreach (var depId in ci.Dependencies ?? Array.Empty<string>())
        {
            if (!byId.TryGetValue(depId, out var dep))
                continue;
            var file = dep.Inputs?.FirstOrDefault();
            if (file is null)
                continue;
            var name = Path.GetFileName(file);
            if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase))
                return BuildKind.Docker;
            if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return BuildKind.DotNet;
            if (name.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase))
                return BuildKind.Native;
        }
        return BuildKind.Unknown;
    }

    private static CiSystem DetectCi(string repoPath)
    {
        if (Directory.Exists(Path.Combine(repoPath, ".github", "workflows")))
            return CiSystem.GitHubActions;
        if (File.Exists(Path.Combine(repoPath, "azure-pipelines.yml")))
            return CiSystem.AzurePipelines;
        return CiSystem.None; // default to a new GitHub Actions workflow
    }

    private static (string path, string yaml) GitHubWorkflow(BuildKind kind) => kind switch
    {
        BuildKind.DotNet => (".github/workflows/arm64-dotnet.yml",
            """
            name: arm64-dotnet
            on: [push, pull_request]
            jobs:
              build-arm64:
                runs-on: windows-11-arm
                steps:
                  - uses: actions/checkout@v4
                  - uses: actions/setup-dotnet@v4
                    with:
                      dotnet-version: '8.0.x'
                  - name: Build for win-arm64
                    run: dotnet build -c Release -r win-arm64
                  - name: Validate (tests)
                    run: dotnet test -c Release -r win-arm64
            """),
        BuildKind.Native => (".github/workflows/arm64-native.yml",
            """
            name: arm64-native
            on: [push, pull_request]
            jobs:
              build-arm64:
                runs-on: windows-11-arm
                steps:
                  - uses: actions/checkout@v4
                  - uses: microsoft/setup-msbuild@v2
                  - name: Build ARM64
                    run: msbuild /p:Platform=ARM64 /p:Configuration=Release
                  - name: Validate (smoke test)
                    run: echo Run ARM64 smoke tests here
            """),
        _ => (".github/workflows/arm64-build.yml",
            """
            name: arm64-build
            on: [push, pull_request]
            jobs:
              build-arm64:
                runs-on: ubuntu-24.04-arm
                steps:
                  - uses: actions/checkout@v4
                  - name: Build for linux/arm64
                    run: docker buildx build --platform linux/arm64 -t app:arm64 .
                  - name: Validate arm64 image
                    run: docker run --rm --platform linux/arm64 app:arm64 --version
            """),
    };

    private static (string path, string yaml) AzurePipeline(BuildKind kind)
    {
        var buildStep = kind switch
        {
            BuildKind.DotNet => "dotnet build -c Release -r win-arm64",
            BuildKind.Native => "msbuild /p:Platform=ARM64 /p:Configuration=Release",
            _ => "docker buildx build --platform linux/arm64 -t app:arm64 .",
        };
        var validateStep = kind switch
        {
            BuildKind.DotNet => "dotnet test -c Release -r win-arm64",
            BuildKind.Native => "echo Run ARM64 smoke tests here",
            _ => "docker run --rm --platform linux/arm64 app:arm64 --version",
        };
        var pool = kind is BuildKind.Docker or BuildKind.Unknown ? "ubuntu-latest" : "windows-latest";

        var yaml =
            $"""
            trigger: [main]
            pool:
              vmImage: '{pool}'
            stages:
              - stage: build_arm64
                jobs:
                  - job: build
                    steps:
                      - script: {buildStep}
                        displayName: 'Build ARM64'
                      - script: {validateStep}
                        displayName: 'Validate ARM64'
            """;
        return ("azure-pipelines-arm64.yml", yaml);
    }
}
