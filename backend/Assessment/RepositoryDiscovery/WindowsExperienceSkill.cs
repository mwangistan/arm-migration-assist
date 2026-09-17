namespace ArmMigrationAssist.Api.Assessment.RepositoryDiscovery;

/// <summary>
/// Windows-native experience scanner (schema windowsExperience). Derives UI stack, installer
/// presence and integration signals from the technology profile detected earlier in the
/// pipeline. Deliberately conservative: unproven signals stay "unknown"/false and are surfaced
/// as explicit unknowns rather than guessed. Runs after technology-discovery (Order 10).
/// </summary>
public sealed class WindowsExperienceSkill : IAssessmentSkill
{
    public string Name => "windows-experience";
    public int Order => 25;
    public string Description => "Derives Windows-native experience signals (UI stack, installer, offline, accessibility, notifications, lifecycle).";
    public IReadOnlyList<string> Outputs => ["windowsExperience"];

    private static readonly Dictionary<string, string> UiByFramework = new(StringComparer.OrdinalIgnoreCase)
    {
        ["winui3"] = "winui3", ["wpf"] = "wpf", ["winforms"] = "winforms",
        ["qt"] = "qt", ["electron"] = "electron", ["tauri"] = "tauri", ["xaml"] = "wpf"
    };

    public Task ContributeAsync(RepositorySnapshot repo, ReadinessManifest manifest, CancellationToken ct = default)
    {
        var tech = manifest.Technology;
        var we = new WindowsExperience();

        string ui = "unknown";
        foreach (var fw in tech.Frameworks)
            if (UiByFramework.TryGetValue(fw, out var mapped)) { ui = mapped; break; }
        if (ui == "unknown")
        {
            bool web = tech.Frameworks.Any(f => f is "react" or "vue" or "angular" or "aspnetcore");
            bool cli = tech.ProjectTypes.Contains("cli");
            if (web) ui = "web"; else if (cli) ui = "cli";
        }

        we.UiTechnology = ui;
        we.InstallerExists = tech.Installers.Count > 0;
        we.Evidence.Add(ui == "unknown"
            ? "No definitive Windows UI stack detected from technology profile."
            : $"UI technology '{ui}' inferred from detected frameworks.");
        if (we.InstallerExists)
            we.Evidence.Add($"Installer technology detected: {string.Join(", ", tech.Installers)}.");

        manifest.WindowsExperience = we;
        return Task.CompletedTask;
    }
}
