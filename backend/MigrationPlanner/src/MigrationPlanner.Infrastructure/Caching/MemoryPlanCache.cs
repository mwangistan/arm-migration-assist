using Microsoft.Extensions.Caching.Memory;
using MigrationPlanner.Application.Abstractions;
using MigrationPlanner.Domain.Plan;

namespace MigrationPlanner.Infrastructure.Caching;

public sealed class MemoryPlanCache : IPlanCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public MemoryPlanCache(IMemoryCache cache, MemoryPlanCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        _cache = cache;
        _ttl = options.Ttl;
    }

    public bool TryGet(string scoreDigest, out CachedPlan? cached)
    {
        if (string.IsNullOrEmpty(scoreDigest) || _ttl <= TimeSpan.Zero)
        {
            cached = null;
            return false;
        }

        if (_cache.TryGetValue<CachedPlan>(Key(scoreDigest), out var entry) && entry is not null)
        {
            cached = entry;
            return true;
        }
        cached = null;
        return false;
    }

    public void Store(string scoreDigest, MigrationPlanV1 plan, ReadinessScoreV1 score, IReadOnlyList<string> observations)
    {
        if (string.IsNullOrEmpty(scoreDigest) || _ttl <= TimeSpan.Zero)
        {
            return;
        }
        var entry = new CachedPlan(plan, score, DateTimeOffset.UtcNow, observations ?? Array.Empty<string>());
        _cache.Set(Key(scoreDigest), entry, _ttl);
    }

    private static string Key(string scoreDigest) => "plan-cache:" + scoreDigest;
}

public sealed class MemoryPlanCacheOptions
{
    public TimeSpan Ttl { get; init; } = TimeSpan.FromHours(1);
}
