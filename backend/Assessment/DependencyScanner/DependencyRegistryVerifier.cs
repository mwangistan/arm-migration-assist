using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ArmMigrationAssist.Api.Assessment.DependencyScanner;

/// <summary>The verified ARM64 outcome for a single package, resolved from a public registry.</summary>
public sealed record RegistryVerdict(
    DependencyClassification Classification,
    IReadOnlyList<string> AvailableArchitectures,
    string Note);

/// <summary>
/// Turns heuristic dependency verdicts into registry-verified facts by asking the public
/// package registries whether an ARM64 (or architecture-neutral) artifact actually exists.
/// PyPI is authoritative (wheel filenames carry platform tags); npm is best-effort (cpu field
/// and platform-specific optional dependencies); NuGet package archives are inspected for
/// managed IL and Windows RID-specific native assets. All network failures degrade gracefully
/// to <c>null</c> so the caller keeps its heuristic classification.
/// </summary>
public sealed class DependencyRegistryVerifier
{
    private readonly HttpClient _http;
    private readonly ILogger<DependencyRegistryVerifier>? _log;
    private readonly IReadOnlyList<string> _npmBases;
    private readonly IReadOnlyList<string> _nugetBases;
    private const long MaxNuGetPackageBytes = 100 * 1024 * 1024;

    public DependencyRegistryVerifier(HttpClient http, ILogger<DependencyRegistryVerifier>? log = null)
    {
        _http = http;
        _log = log;
        _npmBases = ResolveNpmBases();
        _nugetBases = ResolveNuGetBases();
    }

    /// <summary>
    /// Builds the ordered list of npm registry base URLs to try. An explicit override
    /// (<c>NPM_REGISTRY</c> or the standard <c>NPM_CONFIG_REGISTRY</c>) is tried first, then the
    /// Microsoft PackageFeedProxy (which mirrors npmjs and is reachable on Microsoft-managed
    /// devices where <c>registry.npmjs.org</c> is blocked), then the public registry as a final
    /// fallback. The first base that returns a package document wins; the rest are ignored.
    /// </summary>
    private static IReadOnlyList<string> ResolveNpmBases()
    {
        var bases = new List<string>();
        void Add(string? b)
        {
            if (string.IsNullOrWhiteSpace(b)) return;
            var trimmed = b.TrimEnd('/');
            if (!bases.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) bases.Add(trimmed);
        }

        Add(Environment.GetEnvironmentVariable("NPM_REGISTRY"));
        Add(Environment.GetEnvironmentVariable("NPM_CONFIG_REGISTRY"));
        Add("https://packagefeedproxy.microsoft.io/npm");
        Add("https://registry.npmjs.org");
        return bases;
    }

    /// <summary>Verifies a single dependency; returns null when the ecosystem is unsupported or the lookup fails.</summary>
    public async Task<RegistryVerdict?> VerifyAsync(string ecosystem, string name, string? version, CancellationToken ct = default)
    {
        try
        {
            return ecosystem switch
            {
                "pypi" => await VerifyPyPiAsync(name, version, ct),
                "npm" => await VerifyNpmAsync(name, version, ct),
                "nuget" => await VerifyNuGetAsync(name, version, ct),
                _ => null
            };
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _log?.LogDebug(ex, "Registry verification timed out for {Ecosystem} {Name}", ecosystem, name);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.LogDebug(ex, "Registry verification failed for {Ecosystem} {Name}", ecosystem, name);
            return null;
        }
    }

    // --- NuGet ----------------------------------------------------------------------------------

    private static IReadOnlyList<string> ResolveNuGetBases()
    {
        var bases = new List<string>();
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var trimmed = value.TrimEnd('/');
            if (!bases.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) bases.Add(trimmed);
        }

        Add(Environment.GetEnvironmentVariable("NUGET_FLAT_CONTAINER"));
        Add("https://packagefeedproxy.microsoft.io/nuget/v3/flat2");
        return bases;
    }

    private async Task<RegistryVerdict?> VerifyNuGetAsync(
        string name, string? version, CancellationToken ct)
    {
        var exactVersion = NormalizeNuGetVersion(version);
        if (exactVersion is null) return null;

        var id = Uri.EscapeDataString(name.ToLowerInvariant());
        var normalizedVersion = Uri.EscapeDataString(exactVersion.ToLowerInvariant());
        var file = $"{id}.{normalizedVersion}.nupkg";

        foreach (var packageBase in _nugetBases)
        {
            try
            {
                using var response = await _http.GetAsync(
                    $"{packageBase}/{id}/{normalizedVersion}/{file}",
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);
                if (!response.IsSuccessStatusCode) continue;

                if (response.Content.Headers.ContentLength is > MaxNuGetPackageBytes)
                    return InspectionLimitVerdict();

                await using var input = await response.Content.ReadAsStreamAsync(ct);
                using var package = new MemoryStream();
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > MaxNuGetPackageBytes) return InspectionLimitVerdict();
                    await package.WriteAsync(buffer.AsMemory(0, read), ct);
                }

                package.Position = 0;
                return AnalyzeNuGetPackage(package);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log?.LogDebug(ex, "NuGet package base {Base} unreachable for {Name} {Version}",
                    packageBase, name, exactVersion);
            }
        }

        return null;
    }

    private static RegistryVerdict InspectionLimitVerdict() => new(
        DependencyClassification.Unknown,
        ["unknown"],
        $"NuGet package exceeds the {MaxNuGetPackageBytes / 1024 / 1024} MB inspection limit.");

    private static string? NormalizeNuGetVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;

        var value = version.Trim();
        if (value.Length > 2 && value[0] == '[' && value[^1] == ']' && !value.Contains(','))
            value = value[1..^1].Trim();

        if (value.Length == 0 ||
            value.Contains("$(", StringComparison.Ordinal) ||
            value.IndexOfAny(['*', ',', '[', ']', '(', ')']) >= 0)
            return null;

        return value;
    }

    /// <summary>
    /// Inspects a NuGet archive without extracting or executing it. Windows runtime folders are
    /// the strongest signal; otherwise managed assemblies are inspected through their PE/CLR
    /// headers to distinguish IL-only AnyCPU from architecture-specific binaries.
    /// </summary>
    public static RegistryVerdict AnalyzeNuGetPackage(Stream packageStream)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        var entries = archive.Entries.Where(e => e.Length > 0).ToList();

        var runtimeArchitectures = entries
            .Select(e => RuntimeArchitecture(e.FullName))
            .Where(a => a is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (runtimeArchitectures.Contains("arm64") || runtimeArchitectures.Contains("arm64ec"))
        {
            var architectures = new List<string>();
            if (runtimeArchitectures.Contains("arm64")) architectures.Add("arm64");
            if (runtimeArchitectures.Contains("arm64ec")) architectures.Add("arm64ec");
            if (runtimeArchitectures.Contains("x64")) architectures.Add("x64");
            if (runtimeArchitectures.Contains("x86")) architectures.Add("x86");
            return new RegistryVerdict(
                DependencyClassification.Arm64Ready,
                architectures,
                "NuGet package contains a Windows ARM64 runtime asset.");
        }

        if (runtimeArchitectures.Contains("arm"))
            return new RegistryVerdict(
                DependencyClassification.Blocked,
                ["arm"],
                "NuGet package contains a Windows ARM32 runtime asset but no Windows ARM64 runtime asset.");

        if (runtimeArchitectures.Contains("x64") || runtimeArchitectures.Contains("x86"))
        {
            var architectures = runtimeArchitectures
                .Where(a => a is "x64" or "x86")
                .OrderBy(a => a)
                .ToArray();
            return new RegistryVerdict(
                DependencyClassification.EmulationOnly,
                architectures,
                "NuGet package contains Windows x64/x86 runtime assets but no Windows ARM64 runtime asset.");
        }

        var assemblyArchitectures = entries
            .Where(e => IsPeAssetPath(e.FullName))
            .Select(InspectAssembly)
            .Where(a => a is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (assemblyArchitectures.Contains("arm64"))
        {
            var architectures = assemblyArchitectures
                .Where(a => a is "arm64" or "x64" or "x86" or "any-cpu")
                .OrderBy(a => a switch
                {
                    "arm64" => 0,
                    "x64" => 1,
                    "x86" => 2,
                    _ => 3
                })
                .ToArray();
            return new RegistryVerdict(
                DependencyClassification.Arm64Ready,
                architectures,
                "NuGet package contains an ARM64 PE binary.");
        }

        if (assemblyArchitectures.Count > 0 &&
            assemblyArchitectures.All(a => a == "any-cpu"))
            return new RegistryVerdict(
                DependencyClassification.Arm64Ready,
                ["any-cpu"],
                "NuGet package contains IL-only managed assemblies with no Windows native runtime assets.");

        if (assemblyArchitectures.Contains("x64") || assemblyArchitectures.Contains("x86"))
            return new RegistryVerdict(
                DependencyClassification.EmulationOnly,
                assemblyArchitectures.Where(a => a is "x64" or "x86").OrderBy(a => a).ToArray(),
                "NuGet package contains x64/x86 PE binaries and no ARM64 binary.");

        if (entries.Any(e => IsPortableSourceAsset(e.FullName)))
            return new RegistryVerdict(
                DependencyClassification.Arm64Ready,
                ["any-cpu"],
                "NuGet package contains portable C/C++ source or headers and no architecture-specific Windows binary.");

        return new RegistryVerdict(
            DependencyClassification.Unknown,
            ["unknown"],
            "NuGet package has no inspectable managed assembly or Windows runtime asset; it may be a meta-package or build-only package.");
    }

    private static string? RuntimeArchitecture(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[0].Equals("runtimes", StringComparison.OrdinalIgnoreCase))
            return null;

        var rid = parts[1].ToLowerInvariant();
        if (!rid.StartsWith("win", StringComparison.Ordinal)) return null;
        if (rid.Contains("arm64ec", StringComparison.Ordinal)) return "arm64ec";
        if (rid.Contains("arm64", StringComparison.Ordinal)) return "arm64";
        if (rid.Contains("arm", StringComparison.Ordinal)) return "arm";
        if (rid.Contains("x64", StringComparison.Ordinal)) return "x64";
        if (rid.Contains("x86", StringComparison.Ordinal)) return "x86";
        return null;
    }

    private static bool IsPeAssetPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return (normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) &&
               !normalized.Contains("/native/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPortableSourceAsset(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".h", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".hpp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".c", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cc", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase);
    }

    private static string? InspectAssembly(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = new MemoryStream();
            using (var entryStream = entry.Open())
                entryStream.CopyTo(stream);
            stream.Position = 0;
            using var pe = new PEReader(stream);
            var cor = pe.PEHeaders.CorHeader;
            if (cor is null) return MachineArchitecture(pe.PEHeaders.CoffHeader.Machine);

            if (cor.Flags.HasFlag(CorFlags.ILOnly) && !cor.Flags.HasFlag(CorFlags.Requires32Bit))
                return "any-cpu";

            return MachineArchitecture(pe.PEHeaders.CoffHeader.Machine);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? MachineArchitecture(Machine machine) => machine switch
    {
        Machine.Arm64 => "arm64",
        Machine.Amd64 => "x64",
        Machine.I386 => "x86",
        _ => null
    };

    // --- PyPI ------------------------------------------------------------------------------------

    private async Task<RegistryVerdict?> VerifyPyPiAsync(string name, string? version, CancellationToken ct)
    {
        var url = string.IsNullOrWhiteSpace(version)
            ? $"https://pypi.org/pypi/{Uri.EscapeDataString(name)}/json"
            : $"https://pypi.org/pypi/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}/json";

        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var filenames = new List<string>();
        if (doc.RootElement.TryGetProperty("urls", out var urls) && urls.ValueKind == JsonValueKind.Array)
            foreach (var f in urls.EnumerateArray())
                if (f.TryGetProperty("filename", out var fn) && fn.GetString() is { } s)
                    filenames.Add(s);

        return filenames.Count == 0 ? null : PyPiVerdict(filenames);
    }

    /// <summary>
    /// Decides an ARM64 verdict from a release's distribution filenames. Pure and static so it can
    /// be unit-tested without the network. Precedence: pure-Python wheel &gt; win_arm64 wheel &gt;
    /// x64/x86-only wheels &gt; source-only.
    /// </summary>
    public static RegistryVerdict PyPiVerdict(IReadOnlyCollection<string> filenames)
    {
        bool pure = filenames.Any(f => f.EndsWith("-none-any.whl", StringComparison.OrdinalIgnoreCase));
        bool winArm64 = filenames.Any(f => f.Contains("win_arm64", StringComparison.OrdinalIgnoreCase));
        bool winX64 = filenames.Any(f => f.Contains("win_amd64", StringComparison.OrdinalIgnoreCase)
                                      || f.Contains("win32", StringComparison.OrdinalIgnoreCase));
        bool anyWheel = filenames.Any(f => f.EndsWith(".whl", StringComparison.OrdinalIgnoreCase));

        if (pure)
            return new RegistryVerdict(DependencyClassification.Arm64Ready, ["any-cpu"],
                "PyPI publishes a pure-Python wheel (py3-none-any); architecture-neutral.");

        if (winArm64)
            return new RegistryVerdict(DependencyClassification.Arm64Ready,
                winX64 ? ["arm64", "x64"] : ["arm64"],
                "PyPI publishes a Windows ARM64 wheel (win_arm64).");

        if (winX64)
            return new RegistryVerdict(DependencyClassification.EmulationOnly, ["x64"],
                "PyPI publishes only x64/x86 Windows wheels - no ARM64 wheel; runs under emulation unless built from source.");

        // No wheels at all (or only non-Windows wheels) - a source distribution that must be compiled.
        return new RegistryVerdict(DependencyClassification.Unknown, ["unknown"],
            anyWheel
                ? "No Windows wheel published on PyPI; ARM64 availability could not be confirmed."
                : "PyPI ships a source-only distribution (sdist); ARM64 support depends on building native extensions from source.");
    }

    // --- npm -------------------------------------------------------------------------------------

    private async Task<RegistryVerdict?> VerifyNpmAsync(string name, string? version, CancellationToken ct)
    {
        // Scoped names (@scope/pkg) must have the slash encoded for the package-document endpoint.
        var encoded = name.StartsWith('@') ? name.Replace("/", "%2f") : Uri.EscapeDataString(name);

        JsonDocument? doc = null;
        foreach (var b in _npmBases)
        {
            try
            {
                using var resp = await _http.GetAsync($"{b}/{encoded}", ct);
                if (!resp.IsSuccessStatusCode) continue;
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log?.LogDebug(ex, "npm registry base {Base} unreachable for {Name}", b, name);
            }
        }
        if (doc is null) return null;

        using (doc)
        {
            var root = doc.RootElement;

            // Resolve the version manifest: requested version, else dist-tags.latest.
            JsonElement manifest = default;
            bool found = false;
            if (root.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Object)
            {
                if (!string.IsNullOrWhiteSpace(version) && versions.TryGetProperty(version, out manifest))
                    found = true;
                else if (root.TryGetProperty("dist-tags", out var tags) &&
                         tags.TryGetProperty("latest", out var latest) && latest.GetString() is { } lv &&
                         versions.TryGetProperty(lv, out manifest))
                    found = true;
            }
            if (!found) return null;

            List<string>? cpu = null;
            if (manifest.TryGetProperty("cpu", out var cpuEl) && cpuEl.ValueKind == JsonValueKind.Array)
                cpu = cpuEl.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();

            var optionalDeps = new List<string>();
            if (manifest.TryGetProperty("optionalDependencies", out var opt) && opt.ValueKind == JsonValueKind.Object)
                optionalDeps.AddRange(opt.EnumerateObject().Select(p => p.Name));

            return NpmVerdict(cpu, optionalDeps);
        }
    }

    /// <summary>
    /// Decides an ARM64 verdict from an npm version manifest's <c>cpu</c> constraint and any
    /// platform-specific optional dependencies (the common prebuilt-binary pattern). Pure and
    /// static for unit testing.
    /// </summary>
    public static RegistryVerdict NpmVerdict(IReadOnlyList<string>? cpu, IReadOnlyCollection<string> optionalDepNames)
    {
        // An explicit cpu allow-list is the strongest signal.
        if (cpu is { Count: > 0 })
        {
            bool arm64 = cpu.Any(c => c.Contains("arm64", StringComparison.OrdinalIgnoreCase));
            return arm64
                ? new RegistryVerdict(DependencyClassification.Arm64Ready, ["arm64"],
                    "npm manifest declares cpu support for arm64.")
                : new RegistryVerdict(DependencyClassification.EmulationOnly, ["x64"],
                    $"npm manifest restricts cpu to [{string.Join(", ", cpu)}]; no arm64 - runs under emulation.");
        }

        // Platform-specific prebuilt binaries shipped as optional dependencies (e.g. sharp, esbuild).
        var platformDeps = optionalDepNames
            .Where(d => d.Contains("arm64") || d.Contains("x64") || d.Contains("win32")
                     || d.Contains("linux") || d.Contains("darwin") || d.Contains("android"))
            .ToList();

        if (platformDeps.Count > 0)
        {
            bool arm64 = platformDeps.Any(d => d.Contains("arm64", StringComparison.OrdinalIgnoreCase)
                                            || d.Contains("arm", StringComparison.OrdinalIgnoreCase));
            return arm64
                ? new RegistryVerdict(DependencyClassification.Arm64Ready, ["arm64"],
                    "npm package ships an arm64 prebuilt binary as a platform optional dependency.")
                : new RegistryVerdict(DependencyClassification.EmulationOnly, ["x64"],
                    "npm package ships platform-specific prebuilt binaries but none for arm64; runs under emulation.");
        }

        // No cpu restriction and no native platform packages: pure JavaScript.
        return new RegistryVerdict(DependencyClassification.Arm64Ready, ["any-cpu"],
            "npm package declares no cpu restriction or native platform binaries; pure JavaScript is architecture-neutral.");
    }
}
