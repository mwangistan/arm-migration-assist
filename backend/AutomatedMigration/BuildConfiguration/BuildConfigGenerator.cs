using AutomatedMigration.Generators;
using AutomatedMigration.Models;

namespace AutomatedMigration.BuildConfiguration;

// Story 3.1 — adds an ARM64 build target to the repo's build/packaging config.
// The target file comes from the work item's inputs (the stack was detected
// upstream by Feature 1). Each supported build system has its own deterministic
// rule; an unsupported one returns null (no guessing).
public sealed class BuildConfigGenerator : IMigrationGenerator
{
    public GeneratedPatch? Generate(WorkItem workItem, MigrationContext context)
    {
        var target = workItem.Inputs?.FirstOrDefault() ?? "Dockerfile";
        var full = Path.Combine(context.RepoPath, target);
        if (!File.Exists(full))
            return null;

        var name = Path.GetFileName(target);
        if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase))
            return PatchDockerfile(full, target);
        if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return PatchCsproj(full, target);
        if (name.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase))
            return PatchVcxproj(full, target);

        return null; // build system we don't support yet
    }

    // Dockerfile: pin the base image to linux/arm64.
    private static GeneratedPatch? PatchDockerfile(string fullPath, string relative)
    {
        var raw = File.ReadAllText(fullPath);
        var nl = UnifiedDiff.DetectNewline(raw);
        var lines = UnifiedDiff.SplitLines(raw).ToList();
        int fromIndex = lines.FindIndex(l =>
            l.TrimStart().StartsWith("FROM ", StringComparison.OrdinalIgnoreCase));
        if (fromIndex < 0)
            return null;

        var original = lines[fromIndex];
        if (original.Contains("--platform=", StringComparison.OrdinalIgnoreCase))
            return null; // already pinned to a platform

        var indent = original[..(original.Length - original.TrimStart().Length)];
        var afterFrom = original.TrimStart()["FROM ".Length..];
        var newLine = $"{indent}FROM --platform=linux/arm64 {afterFrom}";

        return new GeneratedPatch(UnifiedDiff.ReplaceLine(Normalize(relative), lines, fromIndex, newLine, nl));
    }

    // .csproj: add win-arm64 to the project's runtime identifiers.
    private static GeneratedPatch? PatchCsproj(string fullPath, string relative)
    {
        var raw = File.ReadAllText(fullPath);
        var nl = UnifiedDiff.DetectNewline(raw);
        var lines = UnifiedDiff.SplitLines(raw).ToList();
        if (lines.Any(l => l.Contains("win-arm64", StringComparison.OrdinalIgnoreCase)))
            return null; // already targets arm64

        int ridIndex = lines.FindIndex(l =>
            l.Contains("<RuntimeIdentifiers>", StringComparison.OrdinalIgnoreCase));
        if (ridIndex >= 0)
        {
            var line = lines[ridIndex];
            int close = line.IndexOf("</RuntimeIdentifiers>", StringComparison.OrdinalIgnoreCase);
            if (close < 0)
                return null;
            var newLine = line.Insert(close, ";win-arm64");
            return new GeneratedPatch(UnifiedDiff.ReplaceLine(Normalize(relative), lines, ridIndex, newLine, nl));
        }

        // No runtime identifiers yet: add one inside the first PropertyGroup.
        int pg = lines.FindIndex(l =>
            l.TrimStart().StartsWith("<PropertyGroup", StringComparison.OrdinalIgnoreCase));
        if (pg < 0)
            return null;

        var baseIndent = lines[pg][..(lines[pg].Length - lines[pg].TrimStart().Length)];
        var insert = $"{baseIndent}  <RuntimeIdentifiers>win-arm64</RuntimeIdentifiers>";
        return new GeneratedPatch(UnifiedDiff.InsertAfter(Normalize(relative), lines, pg, insert, nl));
    }

    // .vcxproj: add ARM64 Debug and Release project configurations.
    private static GeneratedPatch? PatchVcxproj(string fullPath, string relative)
    {
        var raw = File.ReadAllText(fullPath);
        var nl = UnifiedDiff.DetectNewline(raw);
        var lines = UnifiedDiff.SplitLines(raw).ToList();
        if (lines.Any(l => l.Contains("|ARM64", StringComparison.OrdinalIgnoreCase)))
            return null; // already has arm64 configurations

        int group = lines.FindIndex(l =>
            l.Contains("Label=\"ProjectConfigurations\"", StringComparison.OrdinalIgnoreCase));
        if (group < 0)
            return null;

        var baseIndent = lines[group][..(lines[group].Length - lines[group].TrimStart().Length)] + "  ";
        var inner = baseIndent + "  ";
        var block =
            $"{baseIndent}<ProjectConfiguration Include=\"Debug|ARM64\">\n" +
            $"{inner}<Configuration>Debug</Configuration>\n" +
            $"{inner}<Platform>ARM64</Platform>\n" +
            $"{baseIndent}</ProjectConfiguration>\n" +
            $"{baseIndent}<ProjectConfiguration Include=\"Release|ARM64\">\n" +
            $"{inner}<Configuration>Release</Configuration>\n" +
            $"{inner}<Platform>ARM64</Platform>\n" +
            $"{baseIndent}</ProjectConfiguration>";

        return new GeneratedPatch(UnifiedDiff.InsertAfter(Normalize(relative), lines, group, block, nl));
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
