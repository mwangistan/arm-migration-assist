using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ArmMigrationAssist.RepositoryDiscovery.Models;

namespace ArmMigrationAssist.Assessment.DependencyScanner;

// Enriches deterministic dependency findings with live registry facts about real ARM64 artifact
// availability. This trades the scanner's offline/deterministic guarantee for fewer "unknown"
// dependencies, so it is composed only into the running backend/CLI - never into the bare service
// used by unit tests. Every network failure degrades gracefully to "keep the heuristic finding".
internal interface IDependencyArchitectureVerifier
{
    Task<IReadOnlyList<DependencyFinding>> VerifyAsync(
        IReadOnlyList<DependencyFinding> findings,
        CancellationToken cancellationToken = default);
}

// Default no-op used by the bare service (and unit tests): returns findings unchanged, offline.
internal sealed class NullDependencyArchitectureVerifier : IDependencyArchitectureVerifier
{
    public Task<IReadOnlyList<DependencyFinding>> VerifyAsync(
        IReadOnlyList<DependencyFinding> findings,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(findings);
}

// A verified ARM64 outcome for a single package, resolved from a public registry.
internal sealed record RegistryVerdict(
    string ArchitectureStatus,
    IReadOnlyList<string> AvailableArchitectures,
    string Note);

// Turns heuristic dependency verdicts into registry-verified facts by asking the public package
// registries whether an ARM64 (or architecture-neutral) artifact actually exists. PyPI is
// authoritative (wheel filenames carry platform tags); npm is best-effort (cpu field and
// platform-specific optional dependencies); NuGet package archives are inspected for managed IL and
// Windows RID-specific native assets. All network failures degrade gracefully to null so the caller
// keeps its heuristic classification.
internal sealed class DependencyRegistryVerifier : IDependencyArchitectureVerifier
{
    private const int MaximumParallelVerifications = 8;
    private const int MaximumVerifications = 1_000;
    private const long MaxNuGetPackageBytes = 100 * 1024 * 1024;

    private static readonly HashSet<string> SupportedEcosystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "pypi", "npm", "nuget",
    };

    private readonly HttpClient http;
    private readonly IReadOnlyList<string> npmBases;
    private readonly IReadOnlyList<string> nugetBases;

    public DependencyRegistryVerifier(HttpClient http)
    {
        this.http = http;
        this.npmBases = ResolveNpmBases();
        this.nugetBases = ResolveNuGetBases();
    }

    public async Task<IReadOnlyList<DependencyFinding>> VerifyAsync(
        IReadOnlyList<DependencyFinding> findings,
        CancellationToken cancellationToken = default)
    {
        var results = findings.ToArray();
        using var gate = new SemaphoreSlim(MaximumParallelVerifications);
        var tasks = new List<Task>();
        var scheduled = 0;
        for (var index = 0; index < results.Length; index++)
        {
            if (!SupportedEcosystems.Contains(results[index].Ecosystem))
            {
                continue;
            }

            if (scheduled >= MaximumVerifications)
            {
                break;
            }

            scheduled++;
            var slot = index;
            tasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var current = results[slot];
                    var verdict = await VerifyAsync(current.Ecosystem, current.Name, current.Version, cancellationToken);
                    if (verdict is not null && ShouldApply(current, verdict))
                    {
                        results[slot] = Apply(current, verdict);
                    }
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        return results;
    }

    // Do not overwrite a decisive scanner result with a less certain registry "unknown".
    private static bool ShouldApply(DependencyFinding finding, RegistryVerdict verdict) =>
        verdict.ArchitectureStatus != "unknown" || finding.ArchitectureStatus == "unknown";

    private static DependencyFinding Apply(DependencyFinding finding, RegistryVerdict verdict)
    {
        var evidence = finding.Evidence
            .Append(new Evidence("artifact", null, $"{finding.Ecosystem}-registry", verdict.Note))
            .ToArray();
        return finding with
        {
            ArchitectureStatus = verdict.ArchitectureStatus,
            AvailableArchitectures = verdict.AvailableArchitectures,
            Evidence = evidence,
            Confidence = Math.Max(finding.Confidence, 0.9m),
        };
    }

    private async Task<RegistryVerdict?> VerifyAsync(
        string ecosystem,
        string name,
        string? version,
        CancellationToken cancellationToken)
    {
        try
        {
            return ecosystem.ToLowerInvariant() switch
            {
                "pypi" => await VerifyPyPiAsync(name, version, cancellationToken),
                "npm" => await VerifyNpmAsync(name, version, cancellationToken),
                "nuget" => await VerifyNuGetAsync(name, version, cancellationToken),
                _ => null,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    // --- NuGet ----------------------------------------------------------------------------------

    private static IReadOnlyList<string> ResolveNuGetBases()
    {
        var bases = new List<string>();
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.TrimEnd('/');
            if (!bases.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                bases.Add(trimmed);
            }
        }

        Add(Environment.GetEnvironmentVariable("NUGET_FLAT_CONTAINER"));
        // The Microsoft PackageFeedProxy mirrors nuget.org and is reachable on Microsoft-managed
        // devices where api.nuget.org is blocked; the public flat container is the final fallback.
        Add("https://packagefeedproxy.microsoft.io/nuget/v3/flat2");
        Add("https://api.nuget.org/v3-flatcontainer");
        return bases;
    }

    private async Task<RegistryVerdict?> VerifyNuGetAsync(string name, string? version, CancellationToken cancellationToken)
    {
        var exactVersion = NormalizeNuGetVersion(version);
        if (exactVersion is null)
        {
            return null;
        }

        var id = Uri.EscapeDataString(name.ToLowerInvariant());
        var normalizedVersion = Uri.EscapeDataString(exactVersion.ToLowerInvariant());
        var file = $"{id}.{normalizedVersion}.nupkg";

        foreach (var packageBase in nugetBases)
        {
            try
            {
                using var response = await http.GetAsync(
                    $"{packageBase}/{id}/{normalizedVersion}/{file}",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                if (response.Content.Headers.ContentLength is > MaxNuGetPackageBytes)
                {
                    return InspectionLimitVerdict();
                }

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var package = new MemoryStream();
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxNuGetPackageBytes)
                    {
                        return InspectionLimitVerdict();
                    }

                    await package.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                package.Position = 0;
                return AnalyzeNuGetPackage(package);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Try the next base URL.
            }
        }

        return null;
    }

    private static RegistryVerdict InspectionLimitVerdict() => new(
        "unknown",
        ["unknown"],
        $"NuGet package exceeds the {MaxNuGetPackageBytes / 1024 / 1024} MB inspection limit.");

    private static string? NormalizeNuGetVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var value = version.Trim();
        if (value.Length > 2 && value[0] == '[' && value[^1] == ']' && !value.Contains(','))
        {
            value = value[1..^1].Trim();
        }

        if (value.Length == 0
            || value.Contains("$(", StringComparison.Ordinal)
            || value.IndexOfAny(['*', ',', '[', ']', '(', ')']) >= 0)
        {
            return null;
        }

        return value;
    }

    // Inspects a NuGet archive without extracting or executing it. Windows runtime folders are the
    // strongest signal; otherwise managed assemblies are inspected through their PE/CLR headers to
    // distinguish IL-only AnyCPU from architecture-specific binaries.
    public static RegistryVerdict AnalyzeNuGetPackage(Stream packageStream)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        var entries = archive.Entries.Where(entry => entry.Length > 0).ToList();

        var runtimeArchitectures = entries
            .Select(entry => RuntimeArchitecture(entry.FullName))
            .Where(architecture => architecture is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (runtimeArchitectures.Contains("arm64") || runtimeArchitectures.Contains("arm64ec"))
        {
            var architectures = new List<string>();
            if (runtimeArchitectures.Contains("arm64"))
            {
                architectures.Add("arm64");
            }

            if (runtimeArchitectures.Contains("arm64ec"))
            {
                architectures.Add("arm64ec");
            }

            if (runtimeArchitectures.Contains("x64"))
            {
                architectures.Add("x64");
            }

            if (runtimeArchitectures.Contains("x86"))
            {
                architectures.Add("x86");
            }

            return new RegistryVerdict("ready", architectures, "NuGet package contains a Windows ARM64 runtime asset.");
        }

        if (runtimeArchitectures.Contains("arm"))
        {
            return new RegistryVerdict(
                "blocked",
                ["arm"],
                "NuGet package contains a Windows ARM32 runtime asset but no Windows ARM64 runtime asset.");
        }

        if (runtimeArchitectures.Contains("x64") || runtimeArchitectures.Contains("x86"))
        {
            var architectures = runtimeArchitectures
                .Where(architecture => architecture is "x64" or "x86")
                .OrderBy(architecture => architecture, StringComparer.Ordinal)
                .ToArray();
            return new RegistryVerdict(
                "emulation-only",
                architectures,
                "NuGet package contains Windows x64/x86 runtime assets but no Windows ARM64 runtime asset.");
        }

        var assemblyArchitectures = entries
            .Where(entry => IsPeAssetPath(entry.FullName))
            .Select(InspectAssembly)
            .Where(architecture => architecture is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (assemblyArchitectures.Contains("arm64"))
        {
            var architectures = assemblyArchitectures
                .Where(architecture => architecture is "arm64" or "x64" or "x86" or "any-cpu")
                .OrderBy(architecture => architecture switch
                {
                    "arm64" => 0,
                    "x64" => 1,
                    "x86" => 2,
                    _ => 3,
                })
                .ToArray();
            return new RegistryVerdict("ready", architectures, "NuGet package contains an ARM64 PE binary.");
        }

        if (assemblyArchitectures.Count > 0 && assemblyArchitectures.All(architecture => architecture == "any-cpu"))
        {
            return new RegistryVerdict(
                "ready",
                ["any-cpu"],
                "NuGet package contains IL-only managed assemblies with no Windows native runtime assets.");
        }

        if (assemblyArchitectures.Contains("x64") || assemblyArchitectures.Contains("x86"))
        {
            return new RegistryVerdict(
                "emulation-only",
                assemblyArchitectures.Where(architecture => architecture is "x64" or "x86").OrderBy(architecture => architecture, StringComparer.Ordinal).ToArray(),
                "NuGet package contains x64/x86 PE binaries and no ARM64 binary.");
        }

        if (entries.Any(entry => IsPortableSourceAsset(entry.FullName)))
        {
            return new RegistryVerdict(
                "ready",
                ["any-cpu"],
                "NuGet package contains portable C/C++ source or headers and no architecture-specific Windows binary.");
        }

        return new RegistryVerdict(
            "unknown",
            ["unknown"],
            "NuGet package has no inspectable managed assembly or Windows runtime asset; it may be a meta-package or build-only package.");
    }

    private static string? RuntimeArchitecture(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[0].Equals("runtimes", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rid = parts[1].ToLowerInvariant();
        if (!rid.StartsWith("win", StringComparison.Ordinal))
        {
            return null;
        }

        if (rid.Contains("arm64ec", StringComparison.Ordinal))
        {
            return "arm64ec";
        }

        if (rid.Contains("arm64", StringComparison.Ordinal))
        {
            return "arm64";
        }

        if (rid.Contains("arm", StringComparison.Ordinal))
        {
            return "arm";
        }

        if (rid.Contains("x64", StringComparison.Ordinal))
        {
            return "x64";
        }

        if (rid.Contains("x86", StringComparison.Ordinal))
        {
            return "x86";
        }

        return null;
    }

    private static bool IsPeAssetPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return (normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            && !normalized.Contains("/native/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPortableSourceAsset(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".h", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".hpp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".c", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cc", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase);
    }

    private static string? InspectAssembly(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = new MemoryStream();
            using (var entryStream = entry.Open())
            {
                entryStream.CopyTo(stream);
            }

            stream.Position = 0;
            using var pe = new PEReader(stream);
            var cor = pe.PEHeaders.CorHeader;
            if (cor is null)
            {
                return MachineArchitecture(pe.PEHeaders.CoffHeader.Machine);
            }

            if (cor.Flags.HasFlag(CorFlags.ILOnly) && !cor.Flags.HasFlag(CorFlags.Requires32Bit))
            {
                return "any-cpu";
            }

            return MachineArchitecture(pe.PEHeaders.CoffHeader.Machine);
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException)
        {
            return null;
        }
    }

    private static string? MachineArchitecture(Machine machine) => machine switch
    {
        Machine.Arm64 => "arm64",
        Machine.Amd64 => "x64",
        Machine.I386 => "x86",
        _ => null,
    };

    // --- PyPI ------------------------------------------------------------------------------------

    private async Task<RegistryVerdict?> VerifyPyPiAsync(string name, string? version, CancellationToken cancellationToken)
    {
        var exactVersion = NormalizePyPiVersion(version);
        var encodedName = Uri.EscapeDataString(name);

        if (exactVersion is not null)
        {
            var pinned = await FetchPyPiVerdictAsync(
                $"https://pypi.org/pypi/{encodedName}/{Uri.EscapeDataString(exactVersion)}/json",
                cancellationToken);
            if (pinned is not null)
            {
                return pinned;
            }
        }

        // No exact pin, or the pinned release is unavailable: fall back to the latest release so a
        // declared range (e.g. ">=2023.5.7") still resolves real ARM64 wheel availability.
        return await FetchPyPiVerdictAsync(
            $"https://pypi.org/pypi/{encodedName}/json",
            cancellationToken);
    }

    private async Task<RegistryVerdict?> FetchPyPiVerdictAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var filenames = new List<string>();
        if (document.RootElement.TryGetProperty("urls", out var urls) && urls.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in urls.EnumerateArray())
            {
                if (entry.TryGetProperty("filename", out var filename) && filename.GetString() is { } value)
                {
                    filenames.Add(value);
                }
            }
        }

        return filenames.Count == 0 ? null : PyPiVerdict(filenames);
    }

    // Returns a version only when the declaration pins one exactly (bare "1.2.3" or "==1.2.3").
    // Ranges, wildcards, and compound specifiers resolve to the latest release instead.
    private static string? NormalizePyPiVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var value = version.Trim();
        if (value.StartsWith("==", StringComparison.Ordinal))
        {
            value = value[2..].Trim();
        }

        if (value.Length == 0 || value.IndexOfAny(['<', '>', '=', '!', '~', '*', ',', ' ', '^']) >= 0)
        {
            return null;
        }

        return value;
    }

    // Decides an ARM64 verdict from a release's distribution filenames. Pure and static so it can be
    // unit-tested without the network. Precedence: pure-Python wheel > win_arm64 wheel > x64/x86-only
    // wheels > source-only.
    public static RegistryVerdict PyPiVerdict(IReadOnlyCollection<string> filenames)
    {
        var pure = filenames.Any(file => file.EndsWith("-none-any.whl", StringComparison.OrdinalIgnoreCase));
        var winArm64 = filenames.Any(file => file.Contains("win_arm64", StringComparison.OrdinalIgnoreCase));
        var winX64 = filenames.Any(file => file.Contains("win_amd64", StringComparison.OrdinalIgnoreCase)
            || file.Contains("win32", StringComparison.OrdinalIgnoreCase));
        var anyWheel = filenames.Any(file => file.EndsWith(".whl", StringComparison.OrdinalIgnoreCase));

        if (pure)
        {
            return new RegistryVerdict("ready", ["any-cpu"], "PyPI publishes a pure-Python wheel (py3-none-any); architecture-neutral.");
        }

        if (winArm64)
        {
            return new RegistryVerdict(
                "ready",
                winX64 ? ["arm64", "x64"] : ["arm64"],
                "PyPI publishes a Windows ARM64 wheel (win_arm64).");
        }

        if (winX64)
        {
            return new RegistryVerdict(
                "emulation-only",
                ["x64"],
                "PyPI publishes only x64/x86 Windows wheels - no ARM64 wheel; runs under emulation unless built from source.");
        }

        return new RegistryVerdict(
            "unknown",
            ["unknown"],
            anyWheel
                ? "No Windows wheel published on PyPI; ARM64 availability could not be confirmed."
                : "PyPI ships a source-only distribution (sdist); ARM64 support depends on building native extensions from source.");
    }

    // --- npm -------------------------------------------------------------------------------------

    private static IReadOnlyList<string> ResolveNpmBases()
    {
        var bases = new List<string>();
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.TrimEnd('/');
            if (!bases.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                bases.Add(trimmed);
            }
        }

        Add(Environment.GetEnvironmentVariable("NPM_REGISTRY"));
        Add(Environment.GetEnvironmentVariable("NPM_CONFIG_REGISTRY"));
        // The Microsoft PackageFeedProxy mirrors npmjs and is reachable on Microsoft-managed
        // devices where registry.npmjs.org is blocked; the public registry is the final fallback.
        Add("https://packagefeedproxy.microsoft.io/npm");
        Add("https://registry.npmjs.org");
        return bases;
    }

    private async Task<RegistryVerdict?> VerifyNpmAsync(string name, string? version, CancellationToken cancellationToken)
    {
        var encoded = name.StartsWith('@') ? name.Replace("/", "%2f") : Uri.EscapeDataString(name);

        JsonDocument? document = null;
        foreach (var packageBase in npmBases)
        {
            try
            {
                using var response = await http.GetAsync($"{packageBase}/{encoded}", cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Try the next base URL.
            }
        }

        if (document is null)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            JsonElement manifest = default;
            var found = false;
            if (root.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Object)
            {
                if (!string.IsNullOrWhiteSpace(version) && versions.TryGetProperty(version, out manifest))
                {
                    found = true;
                }
                else if (root.TryGetProperty("dist-tags", out var tags)
                    && tags.TryGetProperty("latest", out var latest)
                    && latest.GetString() is { } latestVersion
                    && versions.TryGetProperty(latestVersion, out manifest))
                {
                    found = true;
                }
            }

            if (!found)
            {
                return null;
            }

            List<string>? cpu = null;
            if (manifest.TryGetProperty("cpu", out var cpuElement) && cpuElement.ValueKind == JsonValueKind.Array)
            {
                cpu = cpuElement.EnumerateArray().Select(element => element.GetString() ?? string.Empty).ToList();
            }

            List<string>? os = null;
            if (manifest.TryGetProperty("os", out var osElement) && osElement.ValueKind == JsonValueKind.Array)
            {
                os = osElement.EnumerateArray().Select(element => element.GetString() ?? string.Empty).ToList();
            }

            var optionalDependencies = new List<string>();
            if (manifest.TryGetProperty("optionalDependencies", out var optional) && optional.ValueKind == JsonValueKind.Object)
            {
                optionalDependencies.AddRange(optional.EnumerateObject().Select(property => property.Name));
            }

            return NpmVerdict(cpu, optionalDependencies, os, HasNativeBuildSignals(manifest));
        }
    }

    private static readonly string[] NativeBuildTooling =
    [
        "node-gyp", "node-pre-gyp", "@mapbox/node-pre-gyp", "prebuild-install",
        "node-gyp-build", "prebuildify", "node-addon-api", "nan", "bindings", "cmake-js",
    ];

    private static bool HasNativeBuildSignals(JsonElement manifest)
    {
        if (manifest.TryGetProperty("gypfile", out var gyp) && gyp.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        foreach (var section in new[] { "dependencies", "optionalDependencies", "peerDependencies" })
        {
            if (manifest.TryGetProperty(section, out var obj) && obj.ValueKind == JsonValueKind.Object
                && obj.EnumerateObject().Any(property => NativeBuildTooling.Contains(property.Name, StringComparer.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (manifest.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object)
        {
            foreach (var script in scripts.EnumerateObject())
            {
                var body = script.Value.GetString() ?? string.Empty;
                if (body.Contains("node-gyp", StringComparison.OrdinalIgnoreCase)
                    || body.Contains("prebuild-install", StringComparison.OrdinalIgnoreCase)
                    || body.Contains("node-gyp-build", StringComparison.OrdinalIgnoreCase)
                    || body.Contains("node-pre-gyp", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPlatformPackage(string dependency) =>
        (dependency.Contains("win32", StringComparison.OrdinalIgnoreCase)
            || dependency.Contains("windows", StringComparison.OrdinalIgnoreCase)
            || dependency.Contains("linux", StringComparison.OrdinalIgnoreCase)
            || dependency.Contains("darwin", StringComparison.OrdinalIgnoreCase)
            || dependency.Contains("android", StringComparison.OrdinalIgnoreCase)
            || dependency.Contains("freebsd", StringComparison.OrdinalIgnoreCase))
        && (dependency.Contains("arm64", StringComparison.OrdinalIgnoreCase)
            || dependency.Contains("x64", StringComparison.OrdinalIgnoreCase)
            || dependency.Contains("ia32", StringComparison.OrdinalIgnoreCase)
            || dependency.Contains("x86", StringComparison.OrdinalIgnoreCase)
            || dependency.Contains("arm", StringComparison.OrdinalIgnoreCase));

    private static bool IsWindowsPackage(string dependency) =>
        dependency.Contains("win32", StringComparison.OrdinalIgnoreCase)
        || dependency.Contains("windows", StringComparison.OrdinalIgnoreCase);

    // Decides an ARM64 verdict from an npm version manifest's cpu/os constraints, platform-specific
    // optional dependencies (the prebuilt-binary pattern), and native build signals. Windows-on-Arm
    // scoped: a non-Windows arm64 prebuilt does not by itself make a package ready. Pure and static
    // for unit testing.
    public static RegistryVerdict NpmVerdict(
        IReadOnlyList<string>? cpu,
        IReadOnlyCollection<string> optionalDependencyNames,
        IReadOnlyList<string>? os = null,
        bool hasNativeBuildSignals = false)
    {
        if (cpu is { Count: > 0 })
        {
            var arm64 = cpu.Any(value => value.Contains("arm64", StringComparison.OrdinalIgnoreCase));
            if (!arm64)
            {
                return new RegistryVerdict(
                    "emulation-only",
                    ["x64"],
                    $"npm manifest restricts cpu to [{string.Join(", ", cpu)}]; no arm64 - runs under emulation.");
            }

            var windows = os is null || os.Count == 0 || os.Any(value => value.Contains("win32", StringComparison.OrdinalIgnoreCase));
            return windows
                ? new RegistryVerdict("ready", ["arm64"], "npm manifest declares cpu support for arm64.")
                : new RegistryVerdict(
                    "unknown",
                    ["unknown"],
                    $"npm manifest declares arm64 cpu but os is restricted to [{string.Join(", ", os!)}]; verify Windows on Arm support.");
        }

        var platformDependencies = optionalDependencyNames.Where(IsPlatformPackage).ToList();
        if (platformDependencies.Count > 0)
        {
            var windowsDependencies = platformDependencies.Where(IsWindowsPackage).ToList();
            if (windowsDependencies.Count > 0)
            {
                var winArm64 = windowsDependencies.Any(dependency => dependency.Contains("arm64", StringComparison.OrdinalIgnoreCase));
                return winArm64
                    ? new RegistryVerdict("ready", ["arm64"], "npm package ships a win32-arm64 prebuilt binary as a platform optional dependency.")
                    : new RegistryVerdict("emulation-only", ["x64"], "npm package ships Windows prebuilt binaries but none for win32-arm64; runs under emulation.");
            }

            return new RegistryVerdict(
                "unknown",
                ["unknown"],
                "npm package ships platform-specific prebuilt binaries but none targeting Windows; verify a win32-arm64 build exists.");
        }

        if (hasNativeBuildSignals)
        {
            return new RegistryVerdict(
                "unknown",
                ["unknown"],
                "npm package builds a native addon at install time (node-gyp/prebuilds) with no arm64 metadata; verify a win32-arm64 prebuilt exists.");
        }

        return new RegistryVerdict(
            "ready",
            ["any-cpu"],
            "npm package declares no cpu restriction or native platform binaries; pure JavaScript is architecture-neutral.");
    }
}
