namespace ArmMigrationAssist.Api.Assessment;

/// <summary>
/// A reusable assessment skill (plan Section 5 "agent/skill framework"). Each skill is a
/// deterministic scanner - it contributes facts to the manifest from a repository snapshot.
/// The orchestrator (and, later, an AI agent) invokes skills through this uniform contract.
/// Deterministic inside; "skill/tool" on the outside.
/// </summary>
public interface IAssessmentSkill
{
    /// <summary>Kebab-case skill identifier, e.g. "technology-discovery" (schema Skill.name pattern).</summary>
    string Name { get; }

    /// <summary>Execution order within the assessment pipeline (lower runs first).</summary>
    int Order { get; }

    /// <summary>Short description of what the skill accepts and produces (schema Skill.description).</summary>
    string Description { get; }

    /// <summary>Named outputs the skill contributes (schema Skill.supportedOutputs).</summary>
    IReadOnlyList<string> Outputs { get; }

    /// <summary>Contribute this skill's findings into the manifest.</summary>
    Task ContributeAsync(RepositorySnapshot repo, ReadinessManifest manifest, CancellationToken ct = default);
}
