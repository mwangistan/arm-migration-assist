namespace AutomatedMigration.Api.Publication;

public sealed class PublicationOptions
{
    public const string SectionName = "Publication";

    // Personal access token used to fork, push branches, and open pull requests.
    // Read once at startup from AUTOMATION_PUBLISH_TOKEN (or GITHUB_TOKEN as fallback);
    // never surfaced back to clients.
    public string? Token { get; set; }

    // Displayed on commits published by this service. Not tied to a real GitHub user.
    public string CommitAuthorName { get; set; } = "arm-migration-assist";
    public string CommitAuthorEmail { get; set; } = "arm-migration-assist@users.noreply.github.com";

    // How long git subprocess calls are allowed to run before the publisher gives up.
    public int GitTimeoutSeconds { get; set; } = 120;
    // How long each REST call to api.github.com is allowed to run.
    public int ApiTimeoutSeconds { get; set; } = 30;

    // How long to wait for a freshly-created fork to become clonable.
    public int ForkPollTimeoutSeconds { get; set; } = 60;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token);
}
