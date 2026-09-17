using MigrationPlanner.Domain.Guidance;

namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Compact metadata about a guidance snippet, safe to embed in a model prompt.
/// Carries no excerpt content so the model must retrieve full snippets through
/// <see cref="IGuidanceLookup"/>.
/// </summary>
public sealed record GuidanceIndexEntry(
    string GuidanceId,
    string Title,
    string Section,
    IReadOnlyList<Topic> Topics,
    string Summary);
