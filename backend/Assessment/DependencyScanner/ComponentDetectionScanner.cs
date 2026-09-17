using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ArmMigrationAssist.Api.Assessment.DependencyScanner;

/// <summary>A single component resolved by Microsoft component-detection.</summary>
public sealed record ComponentRecord(
    string Name,
    string? Version,
    string Ecosystem,       // canonical purl-style token: npm, pypi, nuget, cargo, go, maven, gem, cocoapods, vcpkg, conda
    string DetectorId,
    IReadOnlyList<string> Locations,
    bool? IsDevelopment,
    bool IsDirect);

/// <summary>Outcome of a component-detection run.</summary>
public sealed class ComponentDetectionResult
{
    /// <summary>True when the executable was located and could be launched.</summary>
    public bool ToolAvailable { get; init; }

    /// <summary>True when the scan ran to completion and the manifest was parsed.</summary>
    public bool Succeeded { get; init; }

    public string? Error { get; init; }
    public IReadOnlyList<ComponentRecord> Components { get; init; } = [];

    public static ComponentDetectionResult NotAvailable(string reason) =>
        new() { ToolAvailable = false, Succeeded = false, Error = reason };
}

/// <summary>
/// Enriches the dependency matrix with Microsoft's component-detection
/// (https://github.com/microsoft/component-detection) - an SBOM scanner that resolves
/// pip, npm, NuGet, Cargo, Go, Maven, RubyGems, CocoaPods, Vcpkg and more, including
/// transitive graphs. It is an optional external tool: when the executable is not present
/// the caller falls back to the built-in manifest parsers, so assessments still work offline.
/// </summary>
public sealed class ComponentDetectionScanner
{
    /// <summary>Environment variable holding an explicit path to the component-detection executable.</summary>
    public const string PathEnvVar = "COMPONENT_DETECTION_PATH";

    private readonly ILogger<ComponentDetectionScanner>? _log;

    public ComponentDetectionScanner(ILogger<ComponentDetectionScanner>? log = null) => _log = log;

    /// <summary>Overall time budget for a single scan before the process is killed.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(6);

    /// <summary>
    /// Locates the component-detection executable. Search order:
    /// 1. <see cref="PathEnvVar"/> environment variable, 2. a bundled <c>tools/</c> folder next to
    /// the app or up the directory tree, 3. the executable name on PATH.
    /// </summary>
    public string? ResolveExecutable()
    {
        var configured = Environment.GetEnvironmentVariable(PathEnvVar);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var exeName = OperatingSystem.IsWindows() ? "component-detection.exe" : "component-detection";

        // Walk up from the app base directory looking for tools/<exe> (repo layout: <root>/tools/...).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tools", exeName);
            if (File.Exists(candidate)) return candidate;
        }

        // Fall back to PATH.
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var p in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(p.Trim(), exeName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* malformed PATH entry */ }
        }

        return null;
    }

    /// <summary>Runs component-detection over <paramref name="sourceDir"/> and returns the resolved components.</summary>
    /// <param name="sourceDir">Repository root to scan.</param>
    /// <param name="workDir">Writable directory for the manifest file and single-file extraction.</param>
    public async Task<ComponentDetectionResult> ScanAsync(string sourceDir, string workDir, CancellationToken ct = default)
    {
        var exe = ResolveExecutable();
        if (exe is null)
            return ComponentDetectionResult.NotAvailable(
                $"component-detection executable not found (set {PathEnvVar} or place it under tools/).");

        Directory.CreateDirectory(workDir);
        var manifestFile = Path.Combine(workDir, $"component-detection-{Guid.NewGuid():N}.json");
        var extractDir = Path.Combine(workDir, ".cd-extract");
        Directory.CreateDirectory(extractDir);

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir
        };
        psi.ArgumentList.Add("scan");
        psi.ArgumentList.Add("--SourceDirectory");
        psi.ArgumentList.Add(sourceDir);
        psi.ArgumentList.Add("--ManifestFile");
        psi.ArgumentList.Add(manifestFile);
        psi.ArgumentList.Add("--LogLevel");
        psi.ArgumentList.Add("Error");
        // Redirect single-file bundle extraction to a writable location (default %TMP% may be sandboxed).
        psi.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = extractDir;

        try
        {
            using var proc = new Process { StartInfo = psi };
            var stderr = new StringBuilder();
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            if (!proc.Start())
                return ComponentDetectionResult.NotAvailable("Failed to start component-detection process.");

            proc.BeginErrorReadLine();
            _ = proc.StandardOutput.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Timeout);

            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(proc);
                return new ComponentDetectionResult
                {
                    ToolAvailable = true,
                    Succeeded = false,
                    Error = $"component-detection timed out after {Timeout.TotalSeconds:0}s."
                };
            }

            if (!File.Exists(manifestFile))
                return new ComponentDetectionResult
                {
                    ToolAvailable = true,
                    Succeeded = false,
                    Error = $"component-detection exited {proc.ExitCode} without a manifest. {Trim(stderr.ToString())}"
                };

            var json = await File.ReadAllTextAsync(manifestFile, ct);
            var components = ParseManifest(json);
            return new ComponentDetectionResult
            {
                ToolAvailable = true,
                Succeeded = true,
                Components = components
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.LogWarning(ex, "component-detection scan failed");
            return new ComponentDetectionResult { ToolAvailable = true, Succeeded = false, Error = ex.Message };
        }
        finally
        {
            TryDelete(manifestFile);
            try { Directory.Delete(extractDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Parses a component-detection manifest (the <c>--ManifestFile</c> output) into a flat
    /// component list. Public and static so it can be unit-tested without the executable.
    /// </summary>
    public static IReadOnlyList<ComponentRecord> ParseManifest(string json)
    {
        var records = new List<ComponentRecord>();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("componentsFound", out var found) ||
            found.ValueKind != JsonValueKind.Array)
            return records;

        foreach (var item in found.EnumerateArray())
        {
            if (!item.TryGetProperty("component", out var comp)) continue;

            var name = GetString(comp, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var version = GetString(comp, "version");
            var ecosystem = ResolveEcosystem(comp);
            var detectorId = GetString(item, "detectorId") ?? ecosystem;

            var locations = new List<string>();
            if (item.TryGetProperty("locationsFoundAt", out var locs) && locs.ValueKind == JsonValueKind.Array)
                foreach (var l in locs.EnumerateArray())
                    if (l.GetString() is { } s)
                        locations.Add(s.TrimStart('/'));

            bool? isDev = null;
            if (item.TryGetProperty("isDevelopmentDependency", out var dev) &&
                (dev.ValueKind == JsonValueKind.True || dev.ValueKind == JsonValueKind.False))
                isDev = dev.GetBoolean();

            // A component with no top-level referrers above it is directly referenced by the project.
            bool isDirect = !item.TryGetProperty("topLevelReferrers", out var refs) ||
                            refs.ValueKind != JsonValueKind.Array ||
                            refs.GetArrayLength() == 0;

            records.Add(new ComponentRecord(name!, version, ecosystem, detectorId!, locations, isDev, isDirect));
        }

        return records;
    }

    /// <summary>Maps a component's type / packageUrl to a canonical ecosystem token.</summary>
    private static string ResolveEcosystem(JsonElement comp)
    {
        // Prefer the Package URL type (pypi, npm, nuget, cargo, golang, maven, gem, cocoapods...).
        if (comp.TryGetProperty("packageUrl", out var purl) &&
            purl.ValueKind == JsonValueKind.Object &&
            purl.TryGetProperty("Type", out var t) && t.GetString() is { Length: > 0 } purlType)
        {
            return purlType.ToLowerInvariant() switch
            {
                "golang" => "go",
                var x => x
            };
        }

        return (GetString(comp, "type") ?? "unknown").ToLowerInvariant() switch
        {
            "pip" => "pypi",
            "npm" => "npm",
            "nuget" => "nuget",
            "cargo" => "cargo",
            "go" => "go",
            "maven" or "gradle" => "maven",
            "rubygems" => "gem",
            "pod" or "cocoapods" => "cocoapods",
            "vcpkg" => "vcpkg",
            "conda" => "conda",
            var other => other
        };
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private static string Trim(string s) =>
        s.Length > 400 ? s[..400] + "..." : s.Trim();
}
