using System.Text.RegularExpressions;

namespace ArmMigrationAssist.Api.Assessment.DependencyScanner;

/// <summary>
/// Story 1.3 acceptance criterion "Identify COM components".
///
/// COM (Component Object Model) servers are <b>native</b> binaries activated in-process by CLSID.
/// An x86/x64-only COM server cannot be loaded into an ARM64-native process - there is no JIT or
/// emulation rescue for an in-proc server - so any COM activation is a potential ARM64 blocker that
/// Feature 2 must re-provision (verify an arm64 CLSID registration exists) before migrating.
///
/// This scanner is source-based: it detects the <i>signals that an app depends on a COM server</i>
/// (ProgID/CLSID activation, .NET interop declarations, project COM references, registration tooling,
/// type libraries) rather than the registered server itself, which is machine-global and never lives
/// in the repository. Detection logic is pure and static for unit testing.
/// </summary>
public static class ComComponentScanner
{
    /// <summary>Advice appended to every COM finding - the reason it is a potential ARM64 blocker.</summary>
    internal const string Advice =
        "COM servers are activated in-process by CLSID; an x86/x64-only server cannot load into an " +
        "ARM64-native process (works only under x64 emulation). Verify an ARM64 registration exists.";

    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Text file kinds worth scanning for COM signals; anything else is skipped to avoid reading binaries.
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".fs", ".c", ".cpp", ".cxx", ".cc", ".h", ".hpp", ".hxx", ".idl",
        ".vbs", ".ps1", ".bat", ".cmd", ".js", ".jsx", ".ts", ".mjs", ".cjs", ".hta",
        ".htm", ".html", ".py", ".csproj", ".vbproj", ".fsproj", ".vcxproj", ".proj",
        ".targets", ".props", ".rc", ".manifest", ".wxs"
    };

    private const long MaxTextFileBytes = 2 * 1024 * 1024;

    // ProgID string activation (managed + scripting): Type.GetTypeFromProgID("Excel.Application"),
    // CreateObject("Scripting.FileSystemObject") (VB/VBA/VBScript), CLSIDFromProgID(L"...").
    private static readonly Regex ProgIdActivation =
        new(@"\b(?:GetTypeFromProgID|CreateObject|CLSIDFromProgID)\s*\(\s*L?[""']([^""']+)[""']", Opts);

    // JScript / HTA ActiveX activation: new ActiveXObject("Excel.Application").
    private static readonly Regex ActiveXObject =
        new(@"\bnew\s+ActiveXObject\s*\(\s*[""']([^""']+)[""']", Opts);

    // Direct CLSID activation in .NET: Type.GetTypeFromCLSID(new Guid("....")).
    private static readonly Regex ClsidActivation =
        new(@"\bGetTypeFromCLSID\s*\(\s*new\s+Guid\s*\(\s*[""']([0-9A-Fa-f{}\-]{32,38})[""']", Opts);

    // C/C++ COM activation: CoCreateInstance(CLSID_Foo, ...), CoCreateInstanceEx, CoGetClassObject.
    private static readonly Regex CoCreate =
        new(@"\bCo(?:CreateInstance(?:Ex)?|GetClassObject)\s*\(\s*(?:&\s*)?([A-Za-z_][A-Za-z0-9_]*)?", Opts);

    // C/C++ type-library import: #import "foo.tlb".
    private static readonly Regex ImportTlb =
        new(@"#import\s+[""<]([^"">]+\.(?:tlb|olb|dll))["">]", Opts);

    // .NET COM interop declaration: [ComImport] ... interface/class Foo.
    private static readonly Regex ComImport =
        new(@"\[\s*ComImport\s*\][\s\S]{0,600}?(?:interface|class)\s+([A-Za-z_][A-Za-z0-9_]*)",
            Opts | RegexOptions.Singleline);

    // Project COM reference: <COMReference Include="Microsoft.Office.Interop.Excel">.
    private static readonly Regex ComReferenceInclude =
        new(@"<COMReference\b[^>]*?\bInclude\s*=\s*[""']([^""']+)[""']", Opts);

    // Embedded interop assembly (implies a COM type library dependency).
    private static readonly Regex EmbedInterop =
        new(@"<EmbedInteropTypes>\s*true\s*</EmbedInteropTypes>", Opts);

    // COM registration tooling in scripts/build: regsvr32 [/s] foo.ocx.
    private static readonly Regex Regsvr32 =
        new(@"\bregsvr32(?:\.exe)?\b(?:\s+/\w+)*(?:\s+""?([^\s""']+\.(?:dll|ocx))""?)?", Opts);

    /// <summary>A single detected COM signal (name is the dedup key; detail/line locate it).</summary>
    public readonly record struct ComSignal(string Name, string Category, string Detail, int Line);

    /// <summary>
    /// Detects COM signals in a single file's text. Pure - no filesystem. The returned signals still
    /// need a repository path attached (done by <see cref="Scan"/>) before they become findings.
    /// </summary>
    public static IEnumerable<ComSignal> DetectSignals(string content)
    {
        if (string.IsNullOrEmpty(content)) yield break;

        foreach (Match m in ProgIdActivation.Matches(content))
        {
            var progId = m.Groups[1].Value.Trim();
            if (progId.Length == 0) continue;
            yield return new ComSignal(progId, "ProgID activation", progId, LineAt(content, m.Index));
        }

        foreach (Match m in ActiveXObject.Matches(content))
        {
            var progId = m.Groups[1].Value.Trim();
            if (progId.Length == 0) continue;
            yield return new ComSignal(progId, "ActiveX activation", progId, LineAt(content, m.Index));
        }

        foreach (Match m in ClsidActivation.Matches(content))
        {
            var clsid = m.Groups[1].Value.Trim();
            yield return new ComSignal($"CLSID {clsid}", "CLSID activation", clsid, LineAt(content, m.Index));
        }

        foreach (Match m in CoCreate.Matches(content))
        {
            var token = m.Groups[1].Value.Trim();
            var name = token.Length > 0 ? $"CoCreateInstance {token}" : "CoCreateInstance";
            yield return new ComSignal(name, "COM activation (C/C++)",
                token.Length > 0 ? token : "CoCreateInstance", LineAt(content, m.Index));
        }

        foreach (Match m in ImportTlb.Matches(content))
        {
            var lib = m.Groups[1].Value.Trim();
            yield return new ComSignal(lib, "Type-library import", lib, LineAt(content, m.Index));
        }

        foreach (Match m in ComImport.Matches(content))
        {
            var type = m.Groups[1].Value.Trim();
            yield return new ComSignal($"COM interop type {type}", ".NET COM interop", type, LineAt(content, m.Index));
        }

        foreach (Match m in ComReferenceInclude.Matches(content))
        {
            var include = m.Groups[1].Value.Trim();
            yield return new ComSignal(include, "Project COM reference", include, LineAt(content, m.Index));
        }

        foreach (Match m in EmbedInterop.Matches(content))
            yield return new ComSignal("Embedded COM interop assembly", ".NET COM interop",
                "EmbedInteropTypes=true", LineAt(content, m.Index));

        foreach (Match m in Regsvr32.Matches(content))
        {
            var target = m.Groups[1].Value.Trim();
            var name = target.Length > 0 ? target : "regsvr32 registration";
            yield return new ComSignal(name, "COM registration (regsvr32)",
                target.Length > 0 ? target : "regsvr32", LineAt(content, m.Index));
        }
    }

    /// <summary>
    /// Scans a working tree and returns one dependency finding per distinct COM component. Every COM
    /// finding is <see cref="DependencyClassification.Unknown"/> - a potential blocker whose ARM64
    /// registration cannot be confirmed offline - carrying the file/line evidence for Feature 2.
    /// </summary>
    public static List<DependencyFinding> Scan(string root)
    {
        // Keyed by component name so repeated activations collapse to a single matrix entry.
        var byName = new Dictionary<string, DependencyFinding>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in RepoFiles.Enumerate(root))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var rel = Path.GetRelativePath(root, path);

            // Type libraries / ActiveX controls present as files are themselves COM artifacts.
            if (ext is ".tlb" or ".olb")
            {
                Add(byName, new DependencyFinding
                {
                    Id = FindingId.Compute("com", Path.GetFileName(path), rel),
                    Name = Path.GetFileName(path),
                    Source = "com",
                    Classification = DependencyClassification.Unknown,
                    EvidencePath = rel,
                    Notes = $"COM type library present ({rel}). {Advice}",
                    IsDirect = true
                });
                continue;
            }

            if (!TextExtensions.Contains(ext)) continue;

            string content;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxTextFileBytes) continue;
                content = File.ReadAllText(path);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var sig in DetectSignals(content))
            {
                Add(byName, new DependencyFinding
                {
                    Id = FindingId.Compute("com", sig.Name, rel, sig.Line.ToString()),
                    Name = sig.Name,
                    Source = "com",
                    Classification = DependencyClassification.Unknown,
                    EvidencePath = rel,
                    Notes = $"{sig.Category}: '{sig.Detail}' at {rel}:{sig.Line}. {Advice}",
                    IsDirect = true
                });
            }
        }

        return byName.Values
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Add(Dictionary<string, DependencyFinding> byName, DependencyFinding finding)
    {
        // First occurrence wins (keeps the earliest evidence path/line); later duplicates are dropped.
        byName.TryAdd(finding.Name, finding);
    }

    private static int LineAt(string content, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < content.Length; i++)
            if (content[i] == '\n') line++;
        return line;
    }
}
