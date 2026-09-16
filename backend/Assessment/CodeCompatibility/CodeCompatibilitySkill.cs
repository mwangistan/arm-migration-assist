using System.Text.RegularExpressions;

namespace ArmMigrationAssist.Api.Assessment.CodeCompatibility;

/// <summary>
/// Story 1.4 - Architecture Compatibility Scanner (assessment skill).
/// Scans source files for architecture-specific patterns that block or complicate ARM64,
/// emitting evidence-linked findings (file + line) categorized by severity.
/// </summary>
public sealed partial class CodeCompatibilitySkill : IAssessmentSkill
{
    public string Name => "code-compatibility";
    public int Order => 40;
    public string Description => "Scans C/C++/C# source for architecture-specific patterns (SIMD, inline asm, arch macros, P/Invoke, pointer-size) with file+line evidence.";
    public IReadOnlyList<string> Outputs => ["codeFindings"];

    private sealed record Rule(string Category, FindingSeverity Severity, Regex Pattern, string[] Extensions);

    private static readonly Rule[] Rules =
    [
        new("x86/x64 SIMD intrinsics (SSE/AVX)", FindingSeverity.High,
            Sse(), [".c", ".h", ".cpp", ".cc", ".cxx", ".hpp"]),
        new("Inline assembly", FindingSeverity.High,
            InlineAsm(), [".c", ".h", ".cpp", ".cc", ".cxx", ".hpp"]),
        new("x86/x64 architecture assumption", FindingSeverity.Medium,
            ArchMacro(), [".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".cs"]),
        new("P/Invoke to native library", FindingSeverity.Medium,
            PInvoke(), [".cs"]),
        new("Pointer-size assumption", FindingSeverity.Low,
            PointerSize(), [".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".cs"])
    ];

    public Task ContributeAsync(RepositorySnapshot repo, ReadinessManifest manifest, CancellationToken ct = default)
    {
        manifest.ArchitectureFindings = Scan(repo);
        return Task.CompletedTask;
    }

    public List<ArchitectureFinding> Scan(RepositorySnapshot snapshot)
    {
        var root = snapshot.LocalRepoPath;
        var findings = new List<ArchitectureFinding>();

        foreach (var path in RepoFiles.Enumerate(root))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();

            // A .sys/.asm file is itself a signal regardless of content.
            if (ext is ".asm" or ".s")
            {
                var relAsm = Path.GetRelativePath(root, path);
                findings.Add(Make("Assembly source file", relAsm, 1, Path.GetFileName(path), FindingSeverity.High));
                continue;
            }

            var applicable = Rules.Where(r => r.Extensions.Contains(ext)).ToArray();
            if (applicable.Length == 0) continue;

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { continue; }

            var rel = Path.GetRelativePath(root, path);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0) continue;
                foreach (var rule in applicable)
                {
                    if (rule.Pattern.IsMatch(line))
                    {
                        var snippet = line.Trim().Length > 200 ? line.Trim()[..200] : line.Trim();
                        findings.Add(Make(rule.Category, rel, i + 1, snippet, rule.Severity));
                    }
                }
            }
        }

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.File)
            .ThenBy(f => f.Line)
            .ToList();
    }

    private static ArchitectureFinding Make(string category, string file, int line, string? snippet, FindingSeverity severity)
        => new()
        {
            Id = FindingId.Compute(category, file, line.ToString()),
            Category = category,
            File = file,
            Line = line,
            Snippet = snippet,
            Severity = severity
        };

    [GeneratedRegex(@"\b(_mm_|_mm256_|_mm512_|__m128|__m256|__m512)|<(immintrin|emmintrin|xmmintrin|nmmintrin|smmintrin|tmmintrin|pmmintrin)\.h>", RegexOptions.Compiled)]
    private static partial Regex Sse();

    [GeneratedRegex(@"(\b__asm\b|\basm\s+volatile\b|\b__asm__\b)", RegexOptions.Compiled)]
    private static partial Regex InlineAsm();

    [GeneratedRegex(@"\b(_M_X64|_M_AMD64|_M_IX86|__x86_64__|__i386__|_AMD64_|_X86_)\b|\bAMD64\b", RegexOptions.Compiled)]
    private static partial Regex ArchMacro();

    [GeneratedRegex(@"\[\s*DllImport\s*\(", RegexOptions.Compiled)]
    private static partial Regex PInvoke();

    [GeneratedRegex(@"sizeof\s*\(\s*(void\s*\*|void\*)\s*\)\s*==\s*4|\(int\)\s*&|\bunsigned\s+long\b\s*\)\s*&", RegexOptions.Compiled)]
    private static partial Regex PointerSize();
}
