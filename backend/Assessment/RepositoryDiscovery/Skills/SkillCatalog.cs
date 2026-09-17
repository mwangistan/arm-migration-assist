using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace ArmMigrationAssist.RepositoryDiscovery.Skills;

// Loads knowledge/skills/catalog.json (embedded at build time) once and exposes
// the set of skill names whose implementationStatus is "runnable". F1 uses this
// to filter its declared availableSkills[] so the planner cannot cite a skill
// that no runner in this repository can execute.
public sealed class SkillCatalog
{
    private const string ResourceName = "ArmMigrationAssist.Knowledge.SkillCatalog.json";

    public static SkillCatalog Default { get; } = LoadEmbedded();

    public string CatalogVersion { get; }

    public ImmutableHashSet<string> RunnableSkillNames { get; }

    private SkillCatalog(string catalogVersion, ImmutableHashSet<string> runnable)
    {
        CatalogVersion = catalogVersion;
        RunnableSkillNames = runnable;
    }

    public bool IsRunnable(string skillName) => RunnableSkillNames.Contains(skillName);

    private static SkillCatalog LoadEmbedded()
    {
        var assembly = typeof(SkillCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded skill catalog '{ResourceName}' not found in {assembly.GetName().Name}.");

        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var version = root.GetProperty("catalogVersion").GetString()
            ?? throw new InvalidOperationException("Skill catalog is missing catalogVersion.");

        var runnable = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var skill in root.GetProperty("skills").EnumerateArray())
        {
            var status = skill.GetProperty("implementationStatus").GetString();
            if (status != "runnable")
            {
                continue;
            }
            var name = skill.GetProperty("name").GetString();
            if (!string.IsNullOrEmpty(name))
            {
                runnable.Add(name);
            }
        }

        return new SkillCatalog(version, runnable.ToImmutable());
    }
}
