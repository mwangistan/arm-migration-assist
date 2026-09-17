namespace MigrationPlanner.Application.Abstractions;

/// <summary>Values copied verbatim into plan.modelProvenance by the model.</summary>
public sealed record PlannerProvenance(string Provider, string Name, string Version);
