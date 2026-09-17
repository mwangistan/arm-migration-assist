namespace MigrationPlanner.Api.Automation;

public sealed class AutomationApiOptions
{
    public const string SectionName = "AutomationApi";

    public string? BaseUrl { get; set; }

    // POST timeout is short by design — we only wait for F3's 202 ack, not for the actual patch work.
    public int TimeoutSeconds { get; set; } = 15;
}
