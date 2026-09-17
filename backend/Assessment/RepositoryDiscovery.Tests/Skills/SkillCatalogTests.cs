using ArmMigrationAssist.RepositoryDiscovery.Skills;
using Xunit;

namespace RepositoryDiscovery.Tests.Skills;

// Guards the embedded catalog contract used by F1's BuildAvailableSkills gating.
// If the catalog schema or contents change in a way that breaks these
// invariants, F1 will start declaring skills the planner can cite but no runner
// can execute.
public sealed class SkillCatalogTests
{
    [Fact]
    public void Default_loads_embedded_catalog()
    {
        var catalog = SkillCatalog.Default;
        Assert.False(string.IsNullOrWhiteSpace(catalog.CatalogVersion));
        Assert.NotEmpty(catalog.RunnableSkillNames);
    }

    [Theory]
    [InlineData("assessment/repository-discovery")]
    [InlineData("assessment/dependency-scan")]
    [InlineData("assessment/code-compatibility-scan")]
    [InlineData("build/add-arm64-target")]
    [InlineData("pipeline/github-actions-arm64-job")]
    public void Runnable_skills_used_by_F1_are_present(string skillName)
    {
        Assert.True(SkillCatalog.Default.IsRunnable(skillName),
            $"Catalog must mark '{skillName}' runnable so F1 can declare it available. " +
            "Update knowledge/skills/catalog.json or F1's BuildAvailableSkills().");
    }

    [Theory]
    [InlineData("build/add-arm64ec-target")]
    [InlineData("packaging/add-arm64-msix")]
    [InlineData("dependency/replace-x64-only")]
    public void Declared_only_skills_are_rejected(string skillName)
    {
        Assert.False(SkillCatalog.Default.IsRunnable(skillName),
            $"'{skillName}' has no runner in this repo; the catalog must not mark it runnable.");
    }

    [Fact]
    public void No_stale_pre_reconciliation_names_remain_runnable()
    {
        // Reconciliation moved F1/F3 to catalog-form namespaced skill names.
        var stale = new[] { "build-config-generator", "ci-pipeline-generator", "code-transformer" };
        foreach (var name in stale)
        {
            Assert.False(SkillCatalog.Default.IsRunnable(name),
                $"Pre-reconciliation name '{name}' must not appear in the catalog.");
        }
    }
}
