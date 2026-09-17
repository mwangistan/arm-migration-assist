using MigrationPlanner.Application.Abstractions;

namespace MigrationPlanner.Infrastructure.Guidance;

public sealed class BoundedGuidanceLookupFactory : IGuidanceLookupFactory
{
    private readonly IWindowsOnArmGuidanceStore _store;
    private readonly GuidanceLookupOptions _options;

    public BoundedGuidanceLookupFactory(IWindowsOnArmGuidanceStore store, GuidanceLookupOptions options)
    {
        _store = store;
        _options = options;
    }

    public IGuidanceLookup Create() => new BoundedGuidanceLookup(_store, _options);
}
