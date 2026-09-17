using ArmMigrationAssist.WinUI.Models;
using ArmMigrationAssist.WinUI.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ArmMigrationAssist.WinUI;

public sealed partial class MainWindow : Window
{
    private static readonly string[] BlockedDependencyStatuses = ["blocked", "emulation-only", "unknown"];
    private static readonly string[] HighCodeSeverities = ["critical", "high"];

    private readonly AssessmentApiClient _apiClient = new();
    private CancellationTokenSource? _assessmentCancellation;
    private RepositoryAssessment? _assessment;
    private string? _rawJson;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));
    }

    private async void AssessButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorInfoBar.IsOpen = false;

        if (!TryGetHttpUri(ApiBaseUrlTextBox.Text, out var serviceBaseUri))
        {
            ShowError("Enter a valid HTTP or HTTPS API base URL.");
            return;
        }

        if (!TryGetGitHubRepositoryUri(RepositoryUrlTextBox.Text, out var repositoryUri))
        {
            ShowError("Enter a public repository URL such as https://github.com/owner/repository.");
            return;
        }

        var selectedTarget = (TargetComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Arm64Native";
        SetBusy(true);
        _assessmentCancellation = new CancellationTokenSource();

        try
        {
            var result = await _apiClient.AssessAsync(
                serviceBaseUri,
                repositoryUri,
                selectedTarget,
                _assessmentCancellation.Token);

            _assessment = result.Assessment;
            _rawJson = result.RawJson;
            ShowAssessment(result.Assessment, result.RawJson);
        }
        catch (OperationCanceledException)
        {
            ShowError("The assessment was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            ShowError($"Could not reach the assessment service. {ex.Message}");
        }
        catch (AssessmentApiException ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _assessmentCancellation?.Dispose();
            _assessmentCancellation = null;
            SetBusy(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _assessmentCancellation?.Cancel();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_assessment is null || _rawJson is null)
            return;

        var picker = new FileSavePicker
        {
            SuggestedFileName = $"{SanitizeFileName(_assessment.Repository.Name)}-arm-assessment"
        };
        picker.FileTypeChoices.Add("JSON document", [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is not null)
            await FileIO.WriteTextAsync(file, _rawJson);
    }

    private void ShowAssessment(RepositoryAssessment assessment, string rawJson)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ResultsView.Visibility = Visibility.Visible;

        var dependencyBlockers = assessment.Dependencies.Count(dependency =>
            BlockedDependencyStatuses.Contains(dependency.ArchitectureStatus, StringComparer.OrdinalIgnoreCase));
        var highCodeFindings = assessment.CodeFindings.Count(finding =>
            HighCodeSeverities.Contains(finding.Severity, StringComparer.OrdinalIgnoreCase));
        var readyDependencies = assessment.Dependencies.Count(dependency =>
            dependency.ArchitectureStatus.Equals("ready", StringComparison.OrdinalIgnoreCase));

        RepositoryNameText.Text = assessment.Repository.Name;
        RepositoryMetadataText.Text =
            $"{assessment.Repository.DefaultBranch}  •  {ShortSha(assessment.Repository.CommitSha)}  •  {FormatDate(assessment.GeneratedAt)}";
        BlockerCountText.Text = (dependencyBlockers + highCodeFindings).ToString();
        ReadyDependencyCountText.Text = $"{readyDependencies} / {assessment.Dependencies.Count}";
        CodeFindingCountText.Text = assessment.CodeFindings.Count.ToString();
        ResolutionRateText.Text = assessment.Dependencies.Count == 0
            ? "N/A"
            : $"{assessment.ScanCoverage.DependencyResolutionRate:P0}";
        TechnologyText.Text = string.IsNullOrWhiteSpace(assessment.Technology.Summary)
            ? "No technology signals were detected."
            : assessment.Technology.Summary;
        BuildSignalsText.Text = string.Join(
            Environment.NewLine,
            BuildSignal("ARM64 target", assessment.BuildFindings.Arm64TargetExists),
            BuildSignal("Arm64EC target", assessment.BuildFindings.Arm64EcTargetExists),
            BuildSignal("ARM64 CI job", assessment.BuildFindings.Arm64CiJobExists),
            BuildSignal("ARM64 packaging", assessment.BuildFindings.PackagingSupportsArm64),
            BuildSignal("Tests", assessment.BuildFindings.TestsExist));
        CoverageText.Text =
            $"{assessment.ScanCoverage.FilesScanned:N0} / {assessment.ScanCoverage.FilesTotal:N0} files scanned" +
            Environment.NewLine +
            $"{assessment.ScanCoverage.ScannersCompleted.Count} scanners completed" +
            (assessment.ScanCoverage.ScannersFailed.Count > 0
                ? Environment.NewLine + $"{assessment.ScanCoverage.ScannersFailed.Count} scanners failed"
                : "");

        DependenciesList.ItemsSource = assessment.Dependencies;
        CodeFindingsList.ItemsSource = assessment.CodeFindings;
        RawJsonTextBox.Text = rawJson;
    }

    private void SetBusy(bool isBusy)
    {
        AssessButton.IsEnabled = !isBusy;
        CancelButton.IsEnabled = isBusy;
        ApiBaseUrlTextBox.IsEnabled = !isBusy;
        RepositoryUrlTextBox.IsEnabled = !isBusy;
        TargetComboBox.IsEnabled = !isBusy;
        ProgressPanel.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private static bool TryGetHttpUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var candidate) &&
            (candidate.Scheme == Uri.UriSchemeHttp || candidate.Scheme == Uri.UriSchemeHttps))
        {
            uri = candidate.AbsoluteUri.EndsWith('/')
                ? candidate
                : new Uri(candidate.AbsoluteUri + "/");
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool TryGetGitHubRepositoryUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value.Trim().TrimEnd('/'), UriKind.Absolute, out var candidate) &&
            candidate.Scheme == Uri.UriSchemeHttps &&
            candidate.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            candidate.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 2)
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    private static string BuildSignal(string label, bool detected) =>
        $"{(detected ? "✓" : "–")}  {label}: {(detected ? "Detected" : "Not found")}";

    private static string ShortSha(string sha) => sha.Length <= 8 ? sha : sha[..8];

    private static string FormatDate(string value) =>
        DateTimeOffset.TryParse(value, out var date)
            ? date.ToLocalTime().ToString("g")
            : "Unknown date";

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character =>
            invalidCharacters.Contains(character) ? '-' : character));
    }
}
