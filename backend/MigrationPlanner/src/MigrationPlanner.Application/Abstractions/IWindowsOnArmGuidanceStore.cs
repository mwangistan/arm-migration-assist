using MigrationPlanner.Domain.Guidance;

namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Read-only store for the curated Windows on Arm guidance corpus. Every
/// implementation must verify snippet integrity at startup and refuse to serve
/// content on mismatch.
/// </summary>
public interface IWindowsOnArmGuidanceStore
{
    string CorpusVersion { get; }

    IReadOnlyCollection<GuidanceSnippet> All();

    bool TryGet(string guidanceId, out GuidanceSnippet? snippet);

    IReadOnlyCollection<GuidanceSnippet> FindByTopic(Topic topic);
}
