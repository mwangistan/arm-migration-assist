using System.Text.Json;
using FluentAssertions;
using Json.Schema;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Catalog;

// Guards the on-disk knowledge/skills/catalog.json against SkillCatalogV1.schema.json.
// The catalog is the source of truth for which skills Feature 3 can actually execute;
// misclassifying a skill as runnable when no runner exists causes the demo to emit
// work items that no downstream tool can honor.
public sealed class SkillCatalogAuditTests
{
    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    [Fact]
    public void Catalog_matches_schema()
    {
        var schema = LoadSchema();
        var catalog = LoadCatalogElement();

        var result = schema.Evaluate(catalog, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });

        result.IsValid.Should().BeTrue(
            "catalog.json must satisfy SkillCatalogV1.schema.json. Failures: {0}",
            FormatErrors(result));
    }

    [Fact]
    public void Every_skill_declares_implementation_status()
    {
        foreach (var skill in EnumerateSkills())
        {
            skill.TryGetProperty("implementationStatus", out var status)
                .Should().BeTrue($"skill '{Name(skill)}' must set implementationStatus");
            status.ValueKind.Should().Be(JsonValueKind.String);
            new[] { "runnable", "declared-only", "proposed" }
                .Should().Contain(status.GetString());
        }
    }

    [Fact]
    public void Runnable_skills_declare_implementedBy()
    {
        foreach (var skill in EnumerateSkills())
        {
            if (skill.GetProperty("implementationStatus").GetString() != "runnable")
            {
                continue;
            }

            skill.TryGetProperty("implementedBy", out var implementedBy)
                .Should().BeTrue($"runnable skill '{Name(skill)}' must set implementedBy");
            implementedBy.ValueKind.Should().Be(JsonValueKind.String);
            implementedBy.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void NonRunnable_skills_omit_implementedBy()
    {
        foreach (var skill in EnumerateSkills())
        {
            var status = skill.GetProperty("implementationStatus").GetString();
            if (status == "runnable")
            {
                continue;
            }

            skill.TryGetProperty("implementedBy", out _)
                .Should().BeFalse(
                    $"skill '{Name(skill)}' has implementationStatus '{status}' and must omit implementedBy");
        }
    }

    private static IEnumerable<JsonElement> EnumerateSkills()
    {
        var catalog = LoadCatalogElement();
        foreach (var skill in catalog.GetProperty("skills").EnumerateArray())
        {
            yield return skill;
        }
    }

    private static string Name(JsonElement skill) =>
        skill.TryGetProperty("name", out var n) ? n.GetString() ?? "(unnamed)" : "(unnamed)";

    private static JsonSchema LoadSchema()
    {
        var path = Path.Combine(RepoRoot.Value,
            "backend", "MigrationPlanner", "contracts", "SkillCatalogV1.schema.json");
        return JsonSchema.FromFile(path);
    }

    private static JsonElement LoadCatalogElement()
    {
        var path = Path.Combine(RepoRoot.Value, "knowledge", "skills", "catalog.json");
        var json = File.ReadAllText(path);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // Walks up from the test binary until we find the repository root (marked by .git/).
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repository root (.git) from " + AppContext.BaseDirectory);
    }

    private static string FormatErrors(EvaluationResults result)
    {
        var details = result.Details
            .Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key} = {e.Value}"));
        return string.Join(Environment.NewLine, details);
    }
}
