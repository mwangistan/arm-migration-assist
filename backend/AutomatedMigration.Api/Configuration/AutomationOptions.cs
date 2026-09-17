namespace AutomatedMigration.Api.Configuration;

public sealed class AutomationOptions
{
    public const string SectionName = "Automation";

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    public int MaxDiffBytes { get; set; } = 262144;

    public JobRetentionOptions JobRetention { get; set; } = new();
}

public sealed class JobRetentionOptions
{
    public int MaxJobs { get; set; } = 100;
    public int MaxAgeMinutes { get; set; } = 60;
}
