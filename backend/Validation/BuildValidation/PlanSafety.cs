using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Validation.BuildValidation;

public static class PlanSafety
{
    // Canonical object-key ordering makes approval independent of JSON/dictionary formatting.
    public static string Fingerprint(PreparedValidation plan)
    {
        using var document = JsonDocument.Parse(ValidationJson.Serialize(plan));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, document.RootElement);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }

    public static void Validate(PreparedValidation plan)
    {
        Require(plan.SchemaVersion == "1.0", "Unsupported validation plan version.");
        Require(!string.IsNullOrWhiteSpace(plan.PlanId) && !string.IsNullOrWhiteSpace(plan.MigrationPlanId),
            "Plan IDs are required.");
        Require(plan.Repository is not null && Path.IsPathFullyQualified(plan.Repository.RootPath),
            "An absolute repository root is required.");
        Require(Regex.IsMatch(plan.Repository!.CommitSha ?? "", @"\A(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})\z"),
            "An exact commit SHA is required.");
        Require(plan.Repository.Artifacts is not null && plan.Repository.Notices is not null &&
                plan.Repository.Artifacts.All(artifact => artifact is not null && !string.IsNullOrWhiteSpace(artifact.Path) &&
                    !string.IsNullOrWhiteSpace(artifact.Sha256) && artifact.Content is not null),
            "Repository artifact metadata is required.");
        Require(Path.IsPathFullyQualified(plan.EvidenceDirectory), "An absolute evidence directory is required.");
        RepositoryPaths.RejectLinks(plan.EvidenceDirectory);
        Require(plan.Criteria is not null && plan.Commands is not null && plan.ManualChecks is not null &&
                plan.TargetDevices is not null && plan.Notices is not null, "Plan collections are required.");
        Require(plan.Criteria!.All(c => c is not null && !string.IsNullOrWhiteSpace(c.Key) &&
                    !string.IsNullOrWhiteSpace(c.Description) && !string.IsNullOrWhiteSpace(c.ExpectedOutcome)),
            "Criteria must have keys, descriptions and expected outcomes.");
        Require(plan.Criteria.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() == plan.Criteria.Count,
            "Duplicate criterion keys.");
        var keys = plan.Criteria.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var commands = new HashSet<string>(StringComparer.Ordinal);
        var proofPaths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var command in plan.Commands!)
        {
            Require(command is not null && command.Id is not null && Regex.IsMatch(command.Id, @"\A[a-zA-Z0-9_-]{1,100}\z"),
                "Command IDs must be unique simple identifiers.");
            Require(commands.Add(command!.Id), $"Duplicate command: {command.Id}");
            Require(!string.IsNullOrWhiteSpace(command.Executable) && !command.Executable.Contains('\0'),
                $"Missing or invalid executable: {command.Id}");
            Require(command.Arguments is not null && command.Arguments.All(a => a is not null && !a.Contains('\0')),
                $"Invalid arguments: {command.Id}");
            Require(command.Environment is not null && command.Environment.All(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) && !pair.Key.Contains('=') && !pair.Key.Contains('\0') &&
                pair.Value is not null && !pair.Value.Contains('\0')), $"Invalid environment: {command.Id}");
            Require(command.TimeoutSeconds is > 0 and <= 3600, $"Timeout must be 1–3600 seconds: {command.Id}");
            var expectedSurface = command.Kind switch
            {
                CommandKind.DotNetBuild or CommandKind.NativeBuild => ExecutionSurface.WindowsArm64Build,
                CommandKind.DotNetTest or CommandKind.NativeSmoke => ExecutionSurface.WindowsArm64Runtime,
                CommandKind.ContainerBuild or CommandKind.ContainerInspect or CommandKind.ContainerSmoke => ExecutionSurface.LinuxArm64Container,
                _ => ExecutionSurface.Unspecified
            };
            Require(command.Surface == expectedSurface, $"Execution surface does not match command kind: {command.Id}");
            Require(command.Kind != CommandKind.DotNetTest || command.Proof == ProofKind.Trx,
                "Dotnet tests always require TRX proof.");
            Require(command.Kind != CommandKind.ContainerInspect || command.Proof == ProofKind.ContainerArchitecture,
                "Container architecture inspection always requires metadata proof.");
            Require(command.CriterionKeys is { Count: > 0 } && command.CriterionKeys.All(keys.Contains),
                $"Unknown or missing criterion mapping: {command.Id}");
            Require(!command.CriterionKeys.Any(key => key.StartsWith("discovered:target-", StringComparison.Ordinal)),
                "Target-device and baseline criteria require future typed device evidence; commands cannot satisfy them.");
            if (command.CriterionKeys.Contains("discovered:windows-arm64-runtime"))
                Require(command.Kind is CommandKind.DotNetTest or CommandKind.NativeSmoke &&
                        command.Surface == ExecutionSurface.WindowsArm64Runtime,
                    "Only detected native ARM64 runtime commands can satisfy Windows ARM64 runtime coverage.");
            if (command.CriterionKeys.Contains("discovered:linux-arm64-container-runtime"))
                Require(command.Kind == CommandKind.ContainerSmoke && command.Surface == ExecutionSurface.LinuxArm64Container,
                    "Only container smoke commands can satisfy Linux ARM64 container runtime coverage.");
            // Plans are topologically ordered. Forward/cyclic dependencies cannot slip through execution.
            Require(command.DependsOn is not null && command.DependsOn.All(id => id != command.Id && commands.Contains(id)),
                $"Unknown, forward or cyclic dependency: {command.Id}");
            Require(!string.IsNullOrWhiteSpace(command.WorkingDirectory), $"Missing cwd: {command.Id}");
            RepositoryPaths.ResolveWithin(plan.Repository.RootPath, command.WorkingDirectory);
            if (command.Proof == ProofKind.Trx)
            {
                Require(!string.IsNullOrWhiteSpace(command.ProofPath) && Path.IsPathFullyQualified(command.ProofPath),
                    $"Absolute TRX path is required: {command.Id}");
                string relative = Path.GetRelativePath(plan.EvidenceDirectory, command.ProofPath!);
                string resolved = RepositoryPaths.ResolveWithin(plan.EvidenceDirectory, relative);
                Require(proofPaths.Add(resolved), "Commands must not share proof files.");
            }
            if (command.Kind == CommandKind.DotNetTest)
            {
                var frameworkArguments = command.Arguments!.Select((value, index) => (value, index))
                    .Where(argument => argument.value == "--framework").ToArray();
                Require(frameworkArguments.Length == 1 && frameworkArguments[0].index + 1 < command.Arguments.Count &&
                        IsLiteralFramework(command.Arguments[frameworkArguments[0].index + 1]),
                    "Each dotnet test command must select exactly one explicit target framework with --framework.");
            }
            Require(command.Kind != CommandKind.Custom || command.Surface == ExecutionSurface.Unspecified,
                "Custom commands cannot claim detected ARM64 coverage.");
        }
        Require(plan.ManualChecks!.All(check => keys.Contains(check.CriterionKey)),
            "Manual checks must reference known criteria.");
        foreach (var build in plan.Commands.Where(command => command.Kind == CommandKind.ContainerBuild))
            Require(plan.Commands.Any(command => command.Kind == CommandKind.ContainerInspect &&
                command.DependsOn.Contains(build.Id) && build.CriterionKeys.All(command.CriterionKeys.Contains)),
                "Every container build criterion must also require successful ARM64 image inspection.");
    }

    public static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    internal static string StableId(string prefix, string path) =>
        prefix + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..12].ToLowerInvariant();

    internal static bool IsLiteralFramework(string framework) =>
        Regex.IsMatch(framework, @"\A[a-zA-Z][a-zA-Z0-9]*(?:[.-][a-zA-Z0-9]+)*\z");
}

internal static class SafeXml
{
    public static XDocument Parse(string content)
    {
        using var reader = XmlReader.Create(new StringReader(content), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 10_485_760
        });
        return XDocument.Load(reader);
    }
}
