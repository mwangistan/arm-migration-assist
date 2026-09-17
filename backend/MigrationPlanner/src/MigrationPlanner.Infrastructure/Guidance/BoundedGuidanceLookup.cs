using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Guidance;

namespace MigrationPlanner.Infrastructure.Guidance;

/// <summary>
/// Per-run wrapper over <see cref="IWindowsOnArmGuidanceStore"/>. Enforces a
/// deterministic call budget, deduplicates the retrieved-id set, and refuses
/// any query outside the two supported shapes. Not thread-safe by design.
/// </summary>
public sealed class BoundedGuidanceLookup : IGuidanceLookup
{
    private readonly IWindowsOnArmGuidanceStore _store;
    private readonly HashSet<string> _retrieved = new(StringComparer.Ordinal);
    private readonly int _budget;
    private int _remaining;

    public BoundedGuidanceLookup(IWindowsOnArmGuidanceStore store, GuidanceLookupOptions options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxCalls <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.MaxCalls, "GuidanceLookup MaxCalls must be positive.");
        }

        _store = store;
        _budget = options.MaxCalls;
        _remaining = options.MaxCalls;
    }

    public string CorpusVersion => _store.CorpusVersion;

    public int RemainingBudget => _remaining;

    public IReadOnlyCollection<string> RetrievedGuidanceIds => _retrieved;

    public IReadOnlyList<GuidanceIndexEntry> ListIndex() =>
        _store.All()
            .Select(s => new GuidanceIndexEntry(s.GuidanceId, s.Title, s.Section, s.Topics, s.Summary))
            .ToArray();

    public Task<GuidanceSnippet?> LookupByIdAsync(string guidanceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConsumeBudget();

        if (string.IsNullOrWhiteSpace(guidanceId))
        {
            return Task.FromResult<GuidanceSnippet?>(null);
        }

        if (_store.TryGet(guidanceId, out var snippet) && snippet is not null)
        {
            _retrieved.Add(snippet.GuidanceId);
            return Task.FromResult<GuidanceSnippet?>(snippet);
        }

        return Task.FromResult<GuidanceSnippet?>(null);
    }

    public Task<IReadOnlyList<GuidanceSnippet>> LookupByTopicAsync(Topic topic, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConsumeBudget();

        var matches = _store.FindByTopic(topic).ToArray();
        foreach (var snippet in matches)
        {
            _retrieved.Add(snippet.GuidanceId);
        }

        return Task.FromResult<IReadOnlyList<GuidanceSnippet>>(matches);
    }

    private void ConsumeBudget()
    {
        if (_remaining <= 0)
        {
            throw new GuidanceLookupBudgetExceededException(_budget);
        }

        _remaining--;
    }
}
