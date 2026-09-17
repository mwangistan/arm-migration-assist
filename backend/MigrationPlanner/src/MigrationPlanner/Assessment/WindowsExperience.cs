using System.Text.Json.Serialization;

namespace MigrationPlanner.Assessment;

public sealed record WindowsExperience(
    [property: JsonPropertyName("windowsVersionExists")] bool WindowsVersionExists,
    [property: JsonPropertyName("uiTechnology")] UiTechnology UiTechnology,
    [property: JsonPropertyName("installerExists")] bool InstallerExists,
    [property: JsonPropertyName("offlineCapable")] bool OfflineCapable,
    [property: JsonPropertyName("accessibilityEvidence")] AccessibilityEvidenceLevel AccessibilityEvidence,
    [property: JsonPropertyName("notificationsIntegrated")] bool NotificationsIntegrated,
    [property: JsonPropertyName("lifecycleIntegrated")] bool LifecycleIntegrated,
    [property: JsonPropertyName("evidence")] IReadOnlyList<Evidence> Evidence,
    [property: JsonPropertyName("accessibilityNotes")] string? AccessibilityNotes = null);
