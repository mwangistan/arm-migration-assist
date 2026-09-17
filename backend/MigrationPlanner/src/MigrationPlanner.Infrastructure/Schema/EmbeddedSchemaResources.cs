using System.Reflection;

namespace MigrationPlanner.Infrastructure.Schema;

/// <summary>
/// Locates the embedded RepositoryAssessmentV1 JSON Schema resource bundled with
/// the infrastructure assembly. Fails fast when the resource is missing so a
/// misconfigured build cannot silently skip schema validation.
/// </summary>
internal static class EmbeddedSchemaResources
{
    public const string RepositoryAssessmentV1ResourceName =
        "MigrationPlanner.Infrastructure.Resources.RepositoryAssessmentV1.schema.json";

    public const string MigrationPlanV1ResourceName =
        "MigrationPlanner.Infrastructure.Resources.MigrationPlanV1.schema.json";

    public static string ReadText(string resourceName)
    {
        var assembly = typeof(EmbeddedSchemaResources).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded schema resource '{resourceName}' not found in {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
