namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Creates a fresh <see cref="IGuidanceLookup"/> for each planning run. The
/// per-run lifetime keeps budget accounting and the retrieved-ID set scoped to
/// a single audit event.
/// </summary>
public interface IGuidanceLookupFactory
{
    IGuidanceLookup Create();
}
