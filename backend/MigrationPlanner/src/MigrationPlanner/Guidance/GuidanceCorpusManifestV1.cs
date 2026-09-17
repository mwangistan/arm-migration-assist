using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MigrationPlanner.Assessment;

namespace MigrationPlanner.Guidance;

[JsonConverter(typeof(EnumMemberJsonConverter<Topic>))]
public enum Topic
{
    [EnumMember(Value = "woa-overview")] WoaOverview,
    [EnumMember(Value = "add-arm-support")] AddArmSupport,
    [EnumMember(Value = "native-arm64-build")] NativeArm64Build,
    [EnumMember(Value = "arm64ec-overview")] Arm64EcOverview,
    [EnumMember(Value = "arm64ec-abi")] Arm64EcAbi,
    [EnumMember(Value = "arm32-to-arm64-migration")] Arm32ToArm64Migration,
    [EnumMember(Value = "winui3-porting")] WinUi3Porting,
    [EnumMember(Value = "msix-arm64-packaging")] MsixArm64Packaging,
    [EnumMember(Value = "windows-app-sdk")] WindowsAppSdk,
    [EnumMember(Value = "performance-optimization")] PerformanceOptimization,
    [EnumMember(Value = "compatibility-troubleshooting")] CompatibilityTroubleshooting,
}

[JsonConverter(typeof(EnumMemberJsonConverter<SourceKind>))]
public enum SourceKind
{
    [EnumMember(Value = "hand-authored-prototype")] HandAuthoredPrototype,
    [EnumMember(Value = "microsoft-learn-snapshot")] MicrosoftLearnSnapshot,
}

public sealed record CorpusProducer(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("sourceKind")] SourceKind SourceKind,
    [property: JsonPropertyName("toolCommit")] string? ToolCommit = null);

public sealed record GuidanceSnippet(
    [property: JsonPropertyName("guidanceId")] string GuidanceId,
    [property: JsonPropertyName("sourceUrl")] string SourceUrl,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("section")] string Section,
    [property: JsonPropertyName("retrievedAt")] DateTimeOffset RetrievedAt,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("topics")] IReadOnlyList<Topic> Topics,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("byteLength")] long? ByteLength = null);

public sealed record GuidanceCorpusManifestV1(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("corpusVersion")] string CorpusVersion,
    [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("producer")] CorpusProducer Producer,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("snippets")] IReadOnlyList<GuidanceSnippet> Snippets,
    [property: JsonPropertyName("notes")] string? Notes = null);
