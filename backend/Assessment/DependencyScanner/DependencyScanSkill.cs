using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;

namespace ArmMigrationAssist.Api.Assessment.DependencyScanner;

/// <summary>
/// Story 1.3 - Dependency Scanner (assessment skill).
/// Builds an ARM64 compatibility matrix from three signals, most authoritative first:
/// (1) the PE machine header of every binary in the repo,
/// (2) Microsoft component-detection (pip, npm, NuGet, Cargo, Go, Maven, RubyGems, Vcpkg, ...),
/// (3) a built-in manifest parser used as an offline fallback when component-detection is absent.
/// </summary>
public sealed class DependencyScanSkill : IAssessmentSkill
{
    private readonly ComponentDetectionScanner _componentDetection;
    private readonly DependencyRegistryVerifier _registry;

    public DependencyScanSkill(ComponentDetectionScanner componentDetection, DependencyRegistryVerifier registry)
    {
        _componentDetection = componentDetection;
        _registry = registry;
    }

    public string Name => "dependency-scan";
    public int Order => 30;
    public string Description => "Builds an ARM64 dependency compatibility matrix from PE machine headers, Microsoft component-detection (pip/npm/NuGet/Cargo/Go/Maven/Vcpkg), manifest parsing, and PyPI/npm/NuGet registry verification.";
    public IReadOnlyList<string> Outputs => ["dependencies"];

    public async Task ContributeAsync(RepositorySnapshot repo, ReadinessManifest manifest, CancellationToken ct = default)
    {
        var root = repo.LocalRepoPath;
        var findings = new List<DependencyFinding>();

        // 1. Authoritative: read the PE COFF machine field of every binary (a signal
        //    component-detection does not provide).
        foreach (var path in RepoFiles.Enumerate(root))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".dll" or ".exe" or ".pyd" or ".node" or ".sys")
            {
                try { findings.Add(InspectBinary(path, Path.GetRelativePath(root, path))); }
                catch { /* not a valid PE image */ }
            }
        }

        // 2. Package dependencies: prefer component-detection, fall back to built-in parsers.
        var cd = await _componentDetection.ScanAsync(root, repo.WorkspacePath, ct);
        if (cd.Succeeded)
        {
            foreach (var component in cd.Components.Where(c => !IsAuthoredNuGetPackage(root, c)))
            {
                var finding = MapComponent(component);
                var localPackage = component.Locations
                    .FirstOrDefault(location => location.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
                if (localPackage is not null)
                {
                    var packagePath = Path.Combine(root, localPackage.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(packagePath))
                    {
                        try
                        {
                            using var package = File.OpenRead(packagePath);
                            var verdict = DependencyRegistryVerifier.AnalyzeNuGetPackage(package);
                            finding = finding with
                            {
                                Classification = verdict.Classification,
                                Notes = $"Local package archive: {verdict.Note}",
                                AvailableArchitectures = verdict.AvailableArchitectures,
                                RegistryVerified = true
                            };
                        }
                        catch (InvalidDataException)
                        {
                            // Keep the declared-package finding when the local archive is malformed.
                        }
                        catch (IOException)
                        {
                            // Keep the declared-package finding when the local archive cannot be read.
                        }
                    }
                }
                findings.Add(finding);
            }
            manifest.ScannersCompleted.Add("component-detection");
        }
        else
        {
            if (cd.ToolAvailable && cd.Error is not null)
                manifest.ScannersFailed.Add("component-detection");
            findings.AddRange(ScanManifests(root));
        }

        var deduped = Dedupe(findings);

        // 3. Registry verification: replace heuristic package verdicts with registry-confirmed facts.
        await VerifyAgainstRegistriesAsync(deduped, manifest, ct);

        manifest.Dependencies = deduped;
    }

    /// <summary>
    /// Confirms heuristic pypi/npm/NuGet classifications against package registries. Runs with
    /// bounded concurrency; any per-package failure leaves the original heuristic verdict intact.
    /// </summary>
    private async Task VerifyAgainstRegistriesAsync(List<DependencyFinding> findings, ReadinessManifest manifest, CancellationToken ct)
    {
        var targets = findings
            .Select((f, i) => (f, i))
            .Where(x => !x.f.RegistryVerified &&
                        x.f.Machine is null &&
                        x.f.Source is "pypi" or "npm" or "nuget")
            .ToList();
        if (targets.Count == 0) return;

        using var gate = new SemaphoreSlim(4);
        int verified = 0, failed = 0;

        var tasks = targets.Select(async x =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var verdict = await _registry.VerifyAsync(x.f.Source, x.f.Name, x.f.Version, ct);
                if (verdict is null) { Interlocked.Increment(ref failed); return; }

                findings[x.i] = x.f with
                {
                    Classification = verdict.Classification,
                    Notes = verdict.Note,
                    AvailableArchitectures = verdict.AvailableArchitectures,
                    RegistryVerified = true
                };
                Interlocked.Increment(ref verified);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.Increment(ref failed);
            }
            finally { gate.Release(); }
        });

        await Task.WhenAll(tasks);

        if (verified > 0) manifest.ScannersCompleted.Add("registry-verification");
        else if (failed > 0) manifest.ScannersFailed.Add("registry-verification");

        // Re-sort now that classifications have changed.
        findings.Sort((a, b) => a.Classification != b.Classification
            ? a.Classification.CompareTo(b.Classification)
            : string.CompareOrdinal(a.Name, b.Name));
    }

    /// <summary>Built-in manifest fallback (no external tool required).</summary>
    public List<DependencyFinding> ScanManifests(string root)
    {
        var findings = new List<DependencyFinding>();

        foreach (var path in RepoFiles.Enumerate(root))
        {
            var rel = Path.GetRelativePath(root, path);
            var name = Path.GetFileName(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();

            try
            {
                if (name.Equals("packages.config", StringComparison.OrdinalIgnoreCase))
                    findings.AddRange(ParsePackagesConfig(path, rel));
                else if (ext is ".csproj" or ".vcxproj")
                    findings.AddRange(ParseMsBuildPackageRefs(path, rel));
                else if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
                    findings.AddRange(ParsePackageJson(path, rel));
                else if (name.Equals("vcpkg.json", StringComparison.OrdinalIgnoreCase))
                    findings.AddRange(ParseVcpkgJson(path, rel));
            }
            catch
            {
                // A malformed manifest should never abort the whole scan; skip it.
            }
        }

        return findings;
    }

    private static List<DependencyFinding> Dedupe(IEnumerable<DependencyFinding> findings) =>
        findings
            .GroupBy(f => (f.Source.ToLowerInvariant(), f.Name.ToLowerInvariant()))
            .Select(g => g
                .OrderByDescending(f => f.Machine != null)          // prefer inspected binaries
                .ThenByDescending(f => f.IsDirect)                   // preserve a direct declaration over a transitive occurrence
                .ThenBy(f => f.Classification)                      // then the most decided classification
                .First())
            .OrderBy(f => f.Classification)
            .ThenBy(f => f.Name)
            .ToList();

    private static bool IsAuthoredNuGetPackage(string root, ComponentRecord component)
    {
        if (component.Ecosystem != "nuget") return false;

        foreach (var location in component.Locations.Where(
                     path => path.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(root, location.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;

            try
            {
                var document = XDocument.Load(path);
                var id = document.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "id")
                    ?.Value;
                if (string.Equals(id, component.Name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (InvalidDataException)
            {
                // A malformed package definition is not enough evidence to suppress a finding.
            }
            catch (IOException)
            {
                // An unreadable package definition is not enough evidence to suppress a finding.
            }
            catch (System.Xml.XmlException)
            {
                // A malformed package definition is not enough evidence to suppress a finding.
            }
        }

        return false;
    }

    // --- component-detection component -> finding -------------------------------------------------

    /// <summary>Maps a component-detection component to a dependency finding. Public for testability.</summary>
    public static DependencyFinding MapComponent(ComponentRecord c)
    {
        var (classification, notes) = ClassifyComponent(c);
        var declaredArchitecture = c.Ecosystem == "nuget"
            ? DeclaredNuGetArchitecture(c.Name, c.Version)
            : null;
        var evidencePath = c.Locations.Count > 0 ? c.Locations[0] : $"({c.Ecosystem} dependency graph)";
        return new DependencyFinding
        {
            Id = FindingId.Compute(c.Ecosystem, c.Name, c.Version ?? string.Empty, evidencePath),
            Name = c.Name,
            Version = c.Version,
            Source = c.Ecosystem,
            Machine = null,
            Classification = classification,
            EvidencePath = evidencePath,
            Notes = notes,
            IsDevelopment = c.IsDevelopment,
            IsDirect = c.IsDirect,
            AvailableArchitectures = declaredArchitecture is null ? null : [declaredArchitecture]
        };
    }

    /// <summary>Deterministic, ecosystem-aware ARM64 classification for a declared package.</summary>
    private static (DependencyClassification, string?) ClassifyComponent(ComponentRecord c) => c.Ecosystem switch
    {
        "npm" => NativeNpmPackages.Contains(c.Name)
            ? (DependencyClassification.Unknown, "Ships native bindings (node-gyp/prebuilt .node); verify win32-arm64 prebuilds.")
            : (DependencyClassification.Arm64Ready, "Pure JavaScript is architecture-neutral."),
        "pypi" => NativePyPackages.Contains(NormalizePyName(c.Name))
            ? (DependencyClassification.Unknown, "Python package ships native code; verify a win_arm64 wheel exists on PyPI.")
            : (DependencyClassification.Arm64Ready, "Pure-Python packages are architecture-neutral."),
        "maven" => (DependencyClassification.Arm64Ready, "JVM bytecode is architecture-neutral."),
        "nuget" => ClassifyNuGet(c.Name, c.Version),
        "gem" => (DependencyClassification.Unknown, "Verify the gem has no native extension or ships an ARM64 build."),
        "cargo" or "go" => (DependencyClassification.Unknown, "Source dependency: rebuilds for ARM64 if the toolchain and any native crates/cgo support it."),
        "vcpkg" => (DependencyClassification.Unknown, "Verify the port supports the arm64-windows triplet."),
        "cocoapods" => (DependencyClassification.Blocked, "CocoaPods target Apple platforms; not applicable to Windows on Arm."),
        _ => (DependencyClassification.Unknown, "ARM64 availability not determined; verify an arm64 build/asset exists.")
    };

    private static (DependencyClassification, string) ClassifyNuGet(string name, string? version) =>
        DeclaredNuGetArchitecture(name, version) switch
        {
            "arm64" => (DependencyClassification.Arm64Ready,
                "Package identity explicitly declares an ARM64 build; registry archive verification is still preferred."),
            "arm" => (DependencyClassification.Blocked,
                "Package identity explicitly declares an ARM32 build; ARM32 binaries cannot be used as ARM64 binaries."),
            "x64" => (DependencyClassification.EmulationOnly,
                "Package identity explicitly declares an x64 build; no ARM64 package asset was available through CFS."),
            "x86" => (DependencyClassification.EmulationOnly,
                "Package identity explicitly declares an x86 build; no ARM64 package asset was available through CFS."),
            _ => (DependencyClassification.Unknown,
                "Verify that an AnyCPU assembly or win-arm64 runtime asset is available through CFS.")
        };

    private static string? DeclaredNuGetArchitecture(string name, string? version)
    {
        var value = $"{name} {version}".ToLowerInvariant();
        if (value.Contains("arm64", StringComparison.Ordinal)) return "arm64";
        if (value.Contains("arm32", StringComparison.Ordinal) ||
            value.Contains(".armfre", StringComparison.Ordinal)) return "arm";
        if (value.Contains("amd64", StringComparison.Ordinal) ||
            value.Contains("x64", StringComparison.Ordinal)) return "x64";
        if (value.Contains("x86fre", StringComparison.Ordinal) ||
            value.Contains("win-x86", StringComparison.Ordinal) ||
            value.Contains("win_x86", StringComparison.Ordinal)) return "x86";
        return null;
    }

    private static string NormalizePyName(string name) =>
        name.Trim().ToLowerInvariant().Replace('_', '-').Replace('.', '-');

    // --- binary + manifest parsing (built-in) ----------------------------------------------------

    private static DependencyFinding Make(string name, string? version, string source,
        string? machine, DependencyClassification classification, string evidencePath, string? notes,
        IReadOnlyList<string>? availableArchitectures = null)
        => new()
        {
            Id = FindingId.Compute(source, name, evidencePath),
            Name = name,
            Version = version,
            Source = source,
            Machine = machine,
            Classification = classification,
            EvidencePath = evidencePath,
            Notes = notes,
            AvailableArchitectures = availableArchitectures
        };

    /// <summary>Authoritative check: read the PE COFF machine field.</summary>
    private static DependencyFinding InspectBinary(string path, string rel)
    {
        string machineName = "Unknown";
        DependencyClassification classification = DependencyClassification.Unknown;
        string? notes = null;

        try
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            var machine = pe.PEHeaders.CoffHeader.Machine;
            machineName = machine switch
            {
                Machine.Arm64 => "ARM64",
                Machine.Amd64 => "x64",
                Machine.I386 => "x86",
                Machine.Arm => "ARM32",
                _ => machine.ToString()
            };

            classification = machine switch
            {
                Machine.Arm64 => DependencyClassification.Arm64Ready,
                Machine.Amd64 or Machine.I386 => DependencyClassification.EmulationOnly,
                Machine.Arm => DependencyClassification.Blocked,
                _ => DependencyClassification.Unknown
            };

            if (Path.GetExtension(path).Equals(".sys", StringComparison.OrdinalIgnoreCase))
            {
                classification = machine == Machine.Arm64
                    ? DependencyClassification.Arm64Ready
                    : DependencyClassification.Blocked;
                notes = "Kernel driver: requires a native ARM64 build; drivers cannot be emulated.";
            }
        }
        catch
        {
            notes = "Not a valid PE image or could not be read.";
        }

        return Make(Path.GetFileName(path), null, "binary", machineName, classification, rel, notes);
    }

    private static IEnumerable<DependencyFinding> ParsePackagesConfig(string path, string rel)
    {
        var doc = XDocument.Load(path);
        foreach (var pkg in doc.Descendants("package"))
        {
            var id = (string?)pkg.Attribute("id") ?? "(unknown)";
            var version = (string?)pkg.Attribute("version");
            var (classification, notes) = ClassifyNuGet(id, version);
            var architecture = DeclaredNuGetArchitecture(id, version);
            yield return Make(id, version, "nuget", null, classification, rel, notes,
                architecture is null ? null : [architecture]);
        }
    }

    private static IEnumerable<DependencyFinding> ParseMsBuildPackageRefs(string path, string rel)
    {
        var doc = XDocument.Load(path);
        foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            var id = (string?)pr.Attribute("Include") ?? (string?)pr.Attribute("Update");
            if (string.IsNullOrEmpty(id)) continue;
            var version = (string?)pr.Attribute("Version") ?? (string?)pr.Element(pr.Name.Namespace + "Version");
            var (classification, notes) = ClassifyNuGet(id, version);
            var architecture = DeclaredNuGetArchitecture(id, version);
            yield return Make(id, version, "nuget", null, classification, rel, notes,
                architecture is null ? null : [architecture]);
        }
    }

    private static IEnumerable<DependencyFinding> ParsePackageJson(string path, string rel)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var section in new[] { "dependencies", "devDependencies", "optionalDependencies" })
        {
            if (!doc.RootElement.TryGetProperty(section, out var deps) || deps.ValueKind != JsonValueKind.Object)
                continue;

            bool isDev = section == "devDependencies";
            foreach (var dep in deps.EnumerateObject())
            {
                bool nativeRisk = NativeNpmPackages.Contains(dep.Name);
                yield return new DependencyFinding
                {
                    Id = FindingId.Compute("npm", dep.Name, rel),
                    Name = dep.Name,
                    Version = dep.Value.GetString(),
                    Source = "npm",
                    Machine = null,
                    Classification = nativeRisk ? DependencyClassification.Unknown : DependencyClassification.Arm64Ready,
                    EvidencePath = rel,
                    Notes = nativeRisk
                        ? "Ships native bindings (node-gyp/prebuilt .node); verify win32-arm64 prebuilds."
                        : "Pure JavaScript is architecture-neutral.",
                    IsDevelopment = isDev
                };
            }
        }
    }

    private static IEnumerable<DependencyFinding> ParseVcpkgJson(string path, string rel)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("dependencies", out var deps) || deps.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var dep in deps.EnumerateArray())
        {
            string? depName = dep.ValueKind == JsonValueKind.String
                ? dep.GetString()
                : dep.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(depName)) continue;

            yield return Make(depName, null, "vcpkg", null, DependencyClassification.Unknown, rel,
                "Verify the port supports the arm64-windows triplet.");
        }
    }

    private static readonly HashSet<string> NativeNpmPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "node-sass", "sharp", "bcrypt", "canvas", "sqlite3", "better-sqlite3",
        "node-gyp", "grpc", "@grpc/grpc-js", "electron", "puppeteer", "playwright",
        "usb", "serialport", "robotjs", "fsevents", "esbuild", "swc", "@swc/core"
    };

    // Common PyPI packages that ship compiled extensions - an ARM64 wheel must exist.
    private static readonly HashSet<string> NativePyPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "numpy", "scipy", "pandas", "pillow", "cryptography", "lxml", "grpcio", "cffi",
        "psycopg2", "psycopg2-binary", "matplotlib", "torch", "tensorflow", "opencv-python",
        "pywin32", "pyzmq", "orjson", "ujson", "aiohttp", "greenlet", "msgpack", "regex",
        "bcrypt", "numba", "scikit-learn", "h5py", "pyarrow", "protobuf", "zstandard",
        "brotli", "pycryptodome", "wrapt", "markupsafe", "pynacl", "coincurve", "ruamel-yaml"
    };
}
