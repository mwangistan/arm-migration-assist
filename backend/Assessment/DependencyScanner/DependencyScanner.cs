using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ArmMigrationAssist.RepositoryDiscovery.Models;
using ArmMigrationAssist.RepositoryDiscovery.Scanning;

namespace ArmMigrationAssist.Assessment.DependencyScanner;

internal sealed partial class DependencyScanner
{
    private const int MaximumDependencies = 10_000;

    private static readonly HashSet<string> NativePackageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "better-sqlite3", "canvas", "grpc", "numpy", "onnxruntime", "opencv-python", "pillow",
        "sharp", "sqlite3", "tensorflow", "torch",
    };

    private static readonly HashSet<string> NativeBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".dylib", ".exe", ".node", ".so", ".sys",
    };

    public DependencyScanResult Scan(
        IReadOnlyList<RepositoryFile> files,
        CancellationToken cancellationToken = default)
    {
        var declarations = new List<DependencyDeclaration>();
        var binaryFindings = new List<DependencyFinding>();
        var malformedManifests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NativeBinaryExtensions.Contains(file.Extension)
                && TryCreateNativeBinaryFinding(file, out var binaryFinding))
            {
                binaryFindings.Add(binaryFinding);
            }

            if (file.Content is null)
            {
                continue;
            }

            var lowerPath = file.RelativePath.ToLowerInvariant();
            if (file.Extension is ".csproj" or ".fsproj" or ".vbproj")
            {
                ParseNuGetProject(file, declarations, malformedManifests);
            }
            else if (lowerPath.EndsWith("packages.config", StringComparison.Ordinal))
            {
                ParsePackagesConfig(file, declarations, malformedManifests);
            }
            else if (lowerPath.EndsWith("package.json", StringComparison.Ordinal))
            {
                ParsePackageJson(file, declarations, malformedManifests);
            }
            else if (lowerPath.EndsWith("requirements.txt", StringComparison.Ordinal))
            {
                ParsePythonRequirements(file, declarations);
            }
            else if (lowerPath.EndsWith("vcpkg.json", StringComparison.Ordinal))
            {
                ParseVcpkg(file, declarations, malformedManifests);
            }
            else if (lowerPath.EndsWith("cargo.toml", StringComparison.Ordinal))
            {
                ParseSimpleTomlSection(file, "dependencies", "cargo", declarations);
            }
            else if (lowerPath.EndsWith("pyproject.toml", StringComparison.Ordinal))
            {
                ParsePyProject(file, declarations);
            }
            else if (lowerPath.EndsWith("go.mod", StringComparison.Ordinal))
            {
                ParseGoModules(file, declarations);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var packageFindings = declarations
            .Where(declaration => !string.IsNullOrWhiteSpace(declaration.Name))
            .GroupBy(
                declaration => $"{declaration.Ecosystem}\0{declaration.Name}\0{declaration.Version}\0{declaration.Criticality}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateFinding(group.First(), group.Select(item => item.Path)))
            .OrderBy(finding => finding.Ecosystem, StringComparer.Ordinal)
            .ThenBy(finding => finding.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumDependencies)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var findings = packageFindings
            .Concat(binaryFindings)
            .OrderBy(finding => finding.Ecosystem, StringComparer.Ordinal)
            .ThenBy(finding => finding.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumDependencies)
            .ToArray();

        var classified = findings.Count(finding => finding.ArchitectureStatus != "unknown");
        var resolutionRate = findings.Length == 0
            ? 1m
            : decimal.Round((decimal)classified / findings.Length, 4, MidpointRounding.AwayFromZero);

        return new DependencyScanResult(
            findings,
            resolutionRate,
            malformedManifests.OrderBy(path => path, StringComparer.Ordinal).ToArray());
    }

    private static DependencyFinding CreateFinding(
        DependencyDeclaration declaration,
        IEnumerable<string> sourcePaths)
    {
        var classification = Classify(declaration.Name, declaration.Ecosystem);
        var paths = sourcePaths.Distinct(StringComparer.Ordinal).Take(20).ToArray();
        var evidence = paths
            .Select(path => new Evidence(
                "manifest",
                path,
                null,
                declaration.Version is null
                    ? $"Declares the {declaration.Ecosystem} dependency {declaration.Name}."
                    : $"Declares the {declaration.Ecosystem} dependency {declaration.Name} at version {declaration.Version}."))
            .ToArray();
        var identity = $"{declaration.Ecosystem}|{declaration.Name}|{declaration.Version}|{declaration.Criticality}";

        return new DependencyFinding(
            StableId("dependency", identity),
            declaration.Name,
            declaration.Version,
            declaration.Ecosystem,
            classification.Type,
            declaration.Criticality,
            classification.Status,
            classification.Architectures,
            [],
            evidence,
            classification.Confidence);
    }

    private static ArchitectureClassification Classify(string name, string ecosystem)
    {
        var normalized = name.ToLowerInvariant();
        var isNative = ecosystem is "vcpkg" or "conan" or "native-binary"
            || NativePackageNames.Contains(name);
        var type = ecosystem == "nuget" && !isNative ? "managed" : isNative ? "native" : "unknown";

        if (Arm64EcTokenRegex().IsMatch(normalized))
        {
            return new(type, "ready", ["arm64ec"], 0.95m);
        }

        if (Arm64TokenRegex().IsMatch(normalized))
        {
            return new(type, "ready", ["arm64"], 0.9m);
        }

        if (X64TokenRegex().IsMatch(normalized))
        {
            return new(type, "emulation-only", ["x64"], 0.9m);
        }

        if (X86TokenRegex().IsMatch(normalized))
        {
            return new(type, "emulation-only", ["x86"], 0.9m);
        }

        return new(type, "unknown", ["unknown"], 0.55m);
    }

    private static void ParseNuGetProject(
        RepositoryFile file,
        ICollection<DependencyDeclaration> declarations,
        ICollection<string> malformedManifests)
    {
        var document = TryParseXml(file.Content!);
        if (document is null)
        {
            malformedManifests.Add(file.RelativePath);
            return;
        }

        foreach (var reference in document.Descendants()
                     .Where(element => element.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase)))
        {
            var name = reference.Attribute("Include")?.Value ?? reference.Attribute("Update")?.Value;
            var version = reference.Attribute("Version")?.Value
                ?? reference.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("Version", StringComparison.OrdinalIgnoreCase))?.Value;
            Add(declarations, name, version, "nuget", "required", file.RelativePath);
        }
    }

    private static void ParsePackagesConfig(
        RepositoryFile file,
        ICollection<DependencyDeclaration> declarations,
        ICollection<string> malformedManifests)
    {
        var document = TryParseXml(file.Content!);
        if (document is null)
        {
            malformedManifests.Add(file.RelativePath);
            return;
        }

        foreach (var package in document.Descendants()
                     .Where(element => element.Name.LocalName.Equals("package", StringComparison.OrdinalIgnoreCase)))
        {
            Add(
                declarations,
                package.Attribute("id")?.Value,
                package.Attribute("version")?.Value,
                "nuget",
                "required",
                file.RelativePath);
        }
    }

    private static void ParsePackageJson(
        RepositoryFile file,
        ICollection<DependencyDeclaration> declarations,
        ICollection<string> malformedManifests)
    {
        try
        {
            using var document = JsonDocument.Parse(file.Content!, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                malformedManifests.Add(file.RelativePath);
                return;
            }

            ParseJsonDependencyObject(document.RootElement, "dependencies", "npm", "required", file.RelativePath, declarations);
            ParseJsonDependencyObject(document.RootElement, "optionalDependencies", "npm", "optional", file.RelativePath, declarations);
            ParseJsonDependencyObject(document.RootElement, "devDependencies", "npm", "optional", file.RelativePath, declarations);
        }
        catch (JsonException)
        {
            malformedManifests.Add(file.RelativePath);
        }
    }

    private static void ParseVcpkg(
        RepositoryFile file,
        ICollection<DependencyDeclaration> declarations,
        ICollection<string> malformedManifests)
    {
        try
        {
            using var document = JsonDocument.Parse(file.Content!, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                malformedManifests.Add(file.RelativePath);
                return;
            }

            if (!document.RootElement.TryGetProperty("dependencies", out var dependencies)
                || dependencies.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var dependency in dependencies.EnumerateArray())
            {
                if (dependency.ValueKind == JsonValueKind.String)
                {
                    Add(declarations, dependency.GetString(), null, "vcpkg", "required", file.RelativePath);
                }
                else if (dependency.ValueKind == JsonValueKind.Object
                         && dependency.TryGetProperty("name", out var name))
                {
                    var version = dependency.TryGetProperty("version>=", out var minimum)
                        ? $">={minimum.GetString()}"
                        : null;
                    Add(declarations, name.GetString(), version, "vcpkg", "required", file.RelativePath);
                }
            }
        }
        catch (JsonException)
        {
            malformedManifests.Add(file.RelativePath);
        }
    }

    private static void ParseJsonDependencyObject(
        JsonElement root,
        string propertyName,
        string ecosystem,
        string criticality,
        string path,
        ICollection<DependencyDeclaration> declarations)
    {
        if (!root.TryGetProperty(propertyName, out var dependencies)
            || dependencies.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var dependency in dependencies.EnumerateObject())
        {
            Add(
                declarations,
                dependency.Name,
                dependency.Value.ValueKind == JsonValueKind.String ? dependency.Value.GetString() : null,
                ecosystem,
                criticality,
                path);
        }
    }

    private static void ParsePythonRequirements(RepositoryFile file, ICollection<DependencyDeclaration> declarations)
    {
        foreach (var rawLine in file.Content!.Split('\n'))
        {
            var line = rawLine.Split('#', 2)[0].Trim();
            if (line.Length == 0 || line.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var match = PythonRequirementRegex().Match(line);
            if (match.Success)
            {
                Add(
                    declarations,
                    match.Groups["name"].Value,
                    NullIfEmpty(match.Groups["version"].Value),
                    "pypi",
                    "required",
                    file.RelativePath);
            }
        }
    }

    private static void ParsePyProject(RepositoryFile file, ICollection<DependencyDeclaration> declarations)
    {
        string? section = null;
        var collectingProjectDependencies = false;
        var collectingOptionalDependencies = false;
        foreach (var rawLine in file.Content!.Split('\n'))
        {
            var line = rawLine.Split('#', 2)[0].Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                section = line.Trim('[', ']').Trim();
                collectingProjectDependencies = false;
                collectingOptionalDependencies = false;
                continue;
            }

            if (section?.Equals("project", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!collectingProjectDependencies
                    && line.StartsWith("dependencies", StringComparison.OrdinalIgnoreCase))
                {
                    collectingProjectDependencies = true;
                }

                if (collectingProjectDependencies)
                {
                    AddPythonRequirements(line, "required", file.RelativePath, declarations);
                    collectingProjectDependencies = !line.Contains(']', StringComparison.Ordinal);
                }

                continue;
            }

            if (section?.Equals("project.optional-dependencies", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!collectingOptionalDependencies && line.Contains('=', StringComparison.Ordinal))
                {
                    collectingOptionalDependencies = true;
                }

                if (collectingOptionalDependencies)
                {
                    AddPythonRequirements(line, "optional", file.RelativePath, declarations);
                    collectingOptionalDependencies = !line.Contains(']', StringComparison.Ordinal);
                }

                continue;
            }

            if (section?.Equals("tool.poetry.dependencies", StringComparison.OrdinalIgnoreCase) == true)
            {
                var separator = line.IndexOf('=');
                if (separator > 0)
                {
                    var name = line[..separator].Trim().Trim('"', '\'');
                    if (!name.Equals("python", StringComparison.OrdinalIgnoreCase))
                    {
                        Add(
                            declarations,
                            name,
                            line[(separator + 1)..].Trim().Trim('"', '\''),
                            "pypi",
                            "required",
                            file.RelativePath);
                    }
                }
            }
        }
    }

    private static void AddPythonRequirements(
        string text,
        string criticality,
        string path,
        ICollection<DependencyDeclaration> declarations)
    {
        foreach (Match match in QuotedValueRegex().Matches(text))
        {
            var requirement = PythonRequirementRegex().Match(match.Groups["value"].Value);
            if (requirement.Success)
            {
                Add(
                    declarations,
                    requirement.Groups["name"].Value,
                    NullIfEmpty(requirement.Groups["version"].Value),
                    "pypi",
                    criticality,
                    path);
            }
        }
    }

    private static bool TryCreateNativeBinaryFinding(
        RepositoryFile file,
        out DependencyFinding finding)
    {
        finding = null!;
        try
        {
            using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                512,
                FileOptions.SequentialScan);
            Span<byte> header = stackalloc byte[256];
            var bytesRead = stream.Read(header);
            var architecture = DetectBinaryArchitecture(header[..bytesRead], stream);
            if (architecture is null)
            {
                return false;
            }

            var (format, status, availableArchitecture) = architecture.Value;
            var type = file.Extension.Equals(".sys", StringComparison.OrdinalIgnoreCase)
                ? "driver"
                : "native";
            var evidence = new Evidence(
                "binary",
                file.RelativePath,
                null,
                $"The {format} header identifies the binary architecture as {availableArchitecture}.");
            finding = new DependencyFinding(
                StableId("dependency", $"native-binary|{file.RelativePath}|{availableArchitecture}"),
                file.FileName,
                null,
                "native-binary",
                type,
                "optional",
                status,
                [availableArchitecture],
                [],
                [evidence],
                0.99m);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static (string Format, string Status, string Architecture)? DetectBinaryArchitecture(
        ReadOnlySpan<byte> header,
        Stream stream)
    {
        if (header.Length >= 64 && header[0] == 'M' && header[1] == 'Z')
        {
            var peOffset = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(0x3c, 4));
            if (peOffset < 0x40 || peOffset > 1_048_576 || peOffset > stream.Length - 6)
            {
                return null;
            }

            Span<byte> peHeader = stackalloc byte[6];
            stream.Position = peOffset;
            if (stream.Read(peHeader) != peHeader.Length
                || peHeader[0] != 'P'
                || peHeader[1] != 'E'
                || peHeader[2] != 0
                || peHeader[3] != 0)
            {
                return null;
            }

            return BinaryPrimitives.ReadUInt16LittleEndian(peHeader[4..]) switch
            {
                0x014c => ("PE", "emulation-only", "x86"),
                0x8664 => ("PE", "emulation-only", "x64"),
                0x01c0 or 0x01c4 => ("PE", "unknown", "arm"),
                0xaa64 => ("PE", "ready", "arm64"),
                0xa641 => ("PE", "ready", "arm64ec"),
                _ => null,
            };
        }

        if (header.Length >= 20 && header[0] == 0x7f && header[1] == 'E' && header[2] == 'L' && header[3] == 'F')
        {
            var littleEndian = header[5] == 1;
            var machine = littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(header[18..])
                : BinaryPrimitives.ReadUInt16BigEndian(header[18..]);
            return machine switch
            {
                3 => ("ELF", "emulation-only", "x86"),
                40 => ("ELF", "unknown", "arm"),
                62 => ("ELF", "emulation-only", "x64"),
                183 => ("ELF", "ready", "arm64"),
                _ => null,
            };
        }

        return null;
    }

    private static void ParseSimpleTomlSection(
        RepositoryFile file,
        string section,
        string ecosystem,
        ICollection<DependencyDeclaration> declarations)
    {
        var active = false;
        foreach (var rawLine in file.Content!.Split('\n'))
        {
            var line = rawLine.Split('#', 2)[0].Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                active = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!active || line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                Add(
                    declarations,
                    line[..separator].Trim().Trim('"', '\''),
                    line[(separator + 1)..].Trim().Trim('"', '\''),
                    ecosystem,
                    "required",
                    file.RelativePath);
            }
        }
    }

    private static void ParseGoModules(RepositoryFile file, ICollection<DependencyDeclaration> declarations)
    {
        var inBlock = false;
        foreach (var rawLine in file.Content!.Split('\n'))
        {
            var line = rawLine.Split("//", 2)[0].Trim();
            if (line.Equals("require (", StringComparison.Ordinal))
            {
                inBlock = true;
                continue;
            }

            if (inBlock && line == ")")
            {
                inBlock = false;
                continue;
            }

            if (line.StartsWith("require ", StringComparison.Ordinal))
            {
                line = line["require ".Length..].Trim();
            }
            else if (!inBlock)
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                Add(declarations, parts[0], parts[1], "go", "required", file.RelativePath);
            }
        }
    }

    private static XDocument? TryParseXml(string content)
    {
        try
        {
            using var textReader = new StringReader(content);
            using var reader = XmlReader.Create(textReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            });
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static void Add(
        ICollection<DependencyDeclaration> declarations,
        string? name,
        string? version,
        string ecosystem,
        string criticality,
        string path)
    {
        name = Sanitize(name, 300);
        version = Sanitize(version, 100);
        if (name is not null)
        {
            declarations.Add(new(name, version, ecosystem, criticality, path));
        }
    }

    private static string? Sanitize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength];
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string StableId(string prefix, string identity)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return $"{prefix}-{hash[..20]}";
    }

    [GeneratedRegex(@"(?<![a-z0-9])arm64ec(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Arm64EcTokenRegex();

    [GeneratedRegex(@"(?<![a-z0-9])(arm64|aarch64)(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Arm64TokenRegex();

    [GeneratedRegex(@"(?<![a-z0-9])(x64|amd64)(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex X64TokenRegex();

    [GeneratedRegex(@"(?<![a-z0-9])(x86|i386|ia32)(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex X86TokenRegex();

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)(?:\[[^\]]+\])?\s*(?<version>(?:===|==|~=|!=|<=|>=|<|>).+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PythonRequirementRegex();

    [GeneratedRegex("[\"'](?<value>[^\"']+)[\"']", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedValueRegex();

    private sealed record DependencyDeclaration(
        string Name,
        string? Version,
        string Ecosystem,
        string Criticality,
        string Path);

    private sealed record ArchitectureClassification(
        string Type,
        string Status,
        IReadOnlyList<string> Architectures,
        decimal Confidence);
}

internal sealed record DependencyScanResult(
    IReadOnlyList<DependencyFinding> Findings,
    decimal ResolutionRate,
    IReadOnlyList<string> MalformedManifestPaths);