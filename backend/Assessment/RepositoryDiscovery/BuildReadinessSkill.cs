using System.Text.RegularExpressions;

namespace ArmMigrationAssist.Api.Assessment.RepositoryDiscovery;

/// <summary>
/// Build-target readiness scanner (plan Section 5 "Build Scanner"). Detects whether the
/// repository already declares ARM64 build targets, win-arm64 runtime identifiers, or an
/// ARM64 CI job. Feeds Feature 2 Story 2.1's build-readiness score. Facts only.
/// </summary>
public sealed partial class BuildReadinessSkill : IAssessmentSkill
{
    public string Name => "build-readiness";
    public int Order => 20;
    public string Description => "Detects ARM64/Arm64EC build targets, win-arm64 RIDs, ARM64 CI jobs, test suites, packaging and detected build targets.";
    public IReadOnlyList<string> Outputs => ["buildReadiness"];

    public Task ContributeAsync(RepositorySnapshot repo, ReadinessManifest manifest, CancellationToken ct = default)
    {
        manifest.BuildReadiness = Scan(repo);
        return Task.CompletedTask;
    }

    public BuildReadiness Scan(RepositorySnapshot snapshot)
    {
        var root = snapshot.LocalRepoPath;
        var result = new BuildReadiness();
        var rids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in RepoFiles.Enumerate(root))
        {
            var rel = Path.GetRelativePath(root, path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var name = Path.GetFileName(path).ToLowerInvariant();

            // Test-suite existence: by file/folder signals across ecosystems.
            if (!result.TestsExist && IsTestSignal(rel, name))
                result.TestsExist = true;

            // Installer/packaging that targets ARM64.
            if (ext is ".wxs" or ".iss" or ".nsi" or ".msix" or ".appx" or ".msi")
            {
                string? pkgText = SafeRead(path);
                if (pkgText != null && (ArmPlatform().IsMatch(pkgText) || WinArm64Rid().IsMatch(pkgText)))
                {
                    result.PackagingSupportsArm64 = true;
                    result.Evidence.Add($"ARM64 packaging artifact configured in {rel}");
                }
            }

            bool isProject = ext is ".sln" or ".csproj" or ".vcxproj" or ".fsproj" or ".vbproj";
            bool isCi = rel.Replace('\\', '/').StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase)
                        || name is "azure-pipelines.yml";
            bool isCMake = name is "cmakelists.txt";
            if (!isProject && !isCi && !isCMake) continue;

            string text;
            try { text = File.ReadAllText(path); } catch { continue; }

            // Arm64EC target.
            if (isProject && Arm64Ec().IsMatch(text))
            {
                result.HasArm64EcTarget = true;
                result.Evidence.Add($"Arm64EC build target declared in {rel}");
            }

            // ARM64 MSBuild platform / configuration.
            if (isProject && ArmPlatform().IsMatch(text))
            {
                result.HasArm64BuildTarget = true;
                result.Evidence.Add($"ARM64 build platform declared in {rel}");
            }

            // Detected build target/platform tokens (e.g. ProjectConfiguration Include="Release|ARM64").
            foreach (Match m in MsBuildConfig().Matches(text))
                targets.Add(m.Groups[1].Value.Trim());

            // win-arm64 runtime identifiers.
            foreach (Match m in WinArm64Rid().Matches(text))
            {
                if (rids.Add(m.Value))
                {
                    result.HasArm64BuildTarget = true;
                    result.Evidence.Add($"Runtime identifier '{m.Value}' in {rel}");
                }
            }

            // ARM64 CI job / runner.
            if (isCi && Arm64Ci().IsMatch(text))
            {
                result.CiHasArm64Job = true;
                result.Evidence.Add($"ARM64 CI job/runner referenced in {rel}");
            }
        }

        foreach (var rid in rids.OrderBy(x => x)) result.RuntimeIdentifiers.Add(rid);
        foreach (var t in targets.OrderBy(x => x)) result.DetectedTargets.Add(t);

        return result;
    }

    private static bool IsTestSignal(string rel, string name)
    {
        var norm = rel.Replace('\\', '/').ToLowerInvariant();
        if (norm.Contains("/test/") || norm.Contains("/tests/") || norm.StartsWith("test/") || norm.StartsWith("tests/"))
            return true;
        return name.EndsWith(".tests.csproj")
            || name.StartsWith("test_") && name.EndsWith(".py")
            || name.EndsWith("_test.go")
            || name.EndsWith(".spec.ts") || name.EndsWith(".test.ts")
            || name.EndsWith(".spec.js") || name.EndsWith(".test.js");
    }

    private static string? SafeRead(string path)
    {
        try { return File.ReadAllText(path); } catch { return null; }
    }

    [GeneratedRegex(@"\bARM64\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ArmPlatform();

    [GeneratedRegex(@"\bARM64EC\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex Arm64Ec();

    [GeneratedRegex(@"win-arm64", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WinArm64Rid();

    [GeneratedRegex(@"windows-11-arm|windows-\d+-arm|arm64", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex Arm64Ci();

    [GeneratedRegex(@"Include=""([^""]*\|[^""]+)""", RegexOptions.Compiled)]
    private static partial Regex MsBuildConfig();
}
