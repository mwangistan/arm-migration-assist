using System.Text.RegularExpressions;
using ArmMigrationAssist.RepositoryDiscovery.Models;
using ArmMigrationAssist.RepositoryDiscovery.Scanning;

namespace ArmMigrationAssist.Assessment.DependencyScanner;

// COM (Component Object Model) detection.
//
// COM servers are native binaries activated in-process by CLSID. An x86/x64-only COM server
// cannot load into an ARM64-native process (it only survives under x64 emulation), so any COM
// activation is a potential ARM64 blocker that a migration must re-provision - by confirming an
// ARM64 CLSID registration exists - before it can run natively.
//
// The scan is source-based: it detects the repository-visible signals that an app depends on a
// COM server (ProgID/CLSID activation, .NET interop declarations, project COM references,
// registration tooling, and type libraries) rather than the registered server itself, which is
// machine-global and never lives in the repository. Detection stays read-only, offline, and
// deterministic, emitting first-class dependency findings of type "com".
internal sealed partial class DependencyScanner
{
    private const int MaximumComFindings = 2_000;

    private const string ComAdvice =
        "COM servers are activated in-process by CLSID; an x86/x64-only server cannot load into an "
        + "ARM64-native process and works only under x64 emulation. Confirm an ARM64 registration exists.";

    private static readonly HashSet<string> ComTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".c", ".cc", ".cjs", ".cmd", ".cpp", ".cs", ".cxx", ".fs", ".fsproj", ".h",
        ".hpp", ".hta", ".htm", ".html", ".hxx", ".idl", ".js", ".jsx", ".manifest", ".mjs",
        ".props", ".proj", ".ps1", ".py", ".rc", ".targets", ".ts", ".vb", ".vbproj", ".vbs",
        ".vcxproj", ".csproj", ".wxs",
    };

    private static IReadOnlyList<DependencyFinding> CollectComFindings(
        IReadOnlyList<RepositoryFile> files,
        CancellationToken cancellationToken)
    {
        var byName = new Dictionary<string, DependencyFinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (byName.Count >= MaximumComFindings)
            {
                break;
            }

            if (file.Extension is ".tlb" or ".olb")
            {
                AddComFinding(
                    byName,
                    file.FileName,
                    "type-library",
                    $"COM type library present ({file.RelativePath}).",
                    file.RelativePath,
                    null,
                    0.8m);
                continue;
            }

            if (file.Content is null || !ComTextExtensions.Contains(file.Extension))
            {
                continue;
            }

            foreach (var signal in DetectComSignals(file.Content))
            {
                AddComFinding(
                    byName,
                    signal.Name,
                    signal.Category,
                    $"{signal.Category}: '{signal.Detail}' at {file.RelativePath}:{signal.Line}.",
                    file.RelativePath,
                    signal.Line,
                    signal.Confidence);
                if (byName.Count >= MaximumComFindings)
                {
                    break;
                }
            }
        }

        return byName.Values
            .OrderBy(finding => finding.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddComFinding(
        IDictionary<string, DependencyFinding> byName,
        string? name,
        string category,
        string observation,
        string path,
        int? line,
        decimal confidence)
    {
        name = Sanitize(name, 300);
        if (name is null || byName.ContainsKey(name))
        {
            return;
        }

        var identity = $"com|{name}|{path}|{line}";
        var evidence = new Evidence("file", path, null, $"{observation} {ComAdvice}");
        byName[name] = new DependencyFinding(
            StableId("dependency", identity),
            name,
            null,
            "com",
            "com",
            "required",
            "unknown",
            ["unknown"],
            [],
            [evidence],
            confidence);
    }

    private static IEnumerable<ComSignal> DetectComSignals(string content)
    {
        foreach (Match match in ProgIdActivationRegex().Matches(content))
        {
            var progId = match.Groups[1].Value.Trim();
            if (progId.Length > 0)
            {
                yield return new ComSignal(progId, "ProgID activation", progId, LineAt(content, match.Index), 0.6m);
            }
        }

        foreach (Match match in ActiveXObjectRegex().Matches(content))
        {
            var progId = match.Groups[1].Value.Trim();
            if (progId.Length > 0)
            {
                yield return new ComSignal(progId, "ActiveX activation", progId, LineAt(content, match.Index), 0.6m);
            }
        }

        foreach (Match match in ClsidActivationRegex().Matches(content))
        {
            var clsid = match.Groups[1].Value.Trim();
            yield return new ComSignal($"CLSID {clsid}", "CLSID activation", clsid, LineAt(content, match.Index), 0.7m);
        }

        foreach (Match match in CoCreateRegex().Matches(content))
        {
            var token = match.Groups[1].Value.Trim();
            var name = token.Length > 0 ? $"CoCreateInstance {token}" : "CoCreateInstance";
            var detail = token.Length > 0 ? token : "CoCreateInstance";
            yield return new ComSignal(name, "COM activation (C/C++)", detail, LineAt(content, match.Index), 0.6m);
        }

        foreach (Match match in ImportTlbRegex().Matches(content))
        {
            var lib = match.Groups[1].Value.Trim();
            yield return new ComSignal(lib, "Type-library import", lib, LineAt(content, match.Index), 0.7m);
        }

        foreach (Match match in ComImportRegex().Matches(content))
        {
            var type = match.Groups[1].Value.Trim();
            yield return new ComSignal($"COM interop type {type}", ".NET COM interop", type, LineAt(content, match.Index), 0.7m);
        }

        foreach (Match match in ComReferenceIncludeRegex().Matches(content))
        {
            var include = match.Groups[1].Value.Trim();
            yield return new ComSignal(include, "Project COM reference", include, LineAt(content, match.Index), 0.8m);
        }

        foreach (Match match in EmbedInteropRegex().Matches(content))
        {
            yield return new ComSignal(
                "Embedded COM interop assembly",
                ".NET COM interop",
                "EmbedInteropTypes=true",
                LineAt(content, match.Index),
                0.6m);
        }

        foreach (Match match in Regsvr32Regex().Matches(content))
        {
            var target = match.Groups[1].Value.Trim();
            var name = target.Length > 0 ? target : "regsvr32 registration";
            var detail = target.Length > 0 ? target : "regsvr32";
            yield return new ComSignal(name, "COM registration (regsvr32)", detail, LineAt(content, match.Index), 0.6m);
        }
    }

    private static int LineAt(string content, int index)
    {
        var line = 1;
        var bound = Math.Min(index, content.Length);
        for (var i = 0; i < bound; i++)
        {
            if (content[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private readonly record struct ComSignal(string Name, string Category, string Detail, int Line, decimal Confidence);

    [GeneratedRegex(@"\b(?:GetTypeFromProgID|CreateObject|CLSIDFromProgID)\s*\(\s*L?[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProgIdActivationRegex();

    [GeneratedRegex(@"\bnew\s+ActiveXObject\s*\(\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActiveXObjectRegex();

    [GeneratedRegex(@"\bGetTypeFromCLSID\s*\(\s*new\s+Guid\s*\(\s*[""']([0-9A-Fa-f{}\-]{32,38})[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClsidActivationRegex();

    [GeneratedRegex(@"\bCo(?:CreateInstance(?:Ex)?|GetClassObject)\s*\(\s*(?:&\s*)?([A-Za-z_][A-Za-z0-9_]*)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CoCreateRegex();

    [GeneratedRegex(@"#import\s+[""<]([^"">]+\.(?:tlb|olb|dll))["">]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImportTlbRegex();

    [GeneratedRegex(@"\[\s*ComImport\s*\][\s\S]{0,600}?(?:interface|class)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ComImportRegex();

    [GeneratedRegex(@"<COMReference\b[^>]*?\bInclude\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ComReferenceIncludeRegex();

    [GeneratedRegex(@"<EmbedInteropTypes>\s*true\s*</EmbedInteropTypes>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmbedInteropRegex();

    [GeneratedRegex(@"\bregsvr32(?:\.exe)?\b(?:\s+/\w+)*(?:\s+""?([^\s""']+\.(?:dll|ocx))""?)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Regsvr32Regex();
}
