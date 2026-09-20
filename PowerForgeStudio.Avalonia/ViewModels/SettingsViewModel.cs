using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IWorkspaceRootCatalogService? _catalog;
    private readonly IWorkspacePreferenceService? _preferences;
    private bool _applying;
    private int _saveVersion;

    public SettingsViewModel(IWorkspaceRootCatalogService? catalog, IWorkspacePreferenceService? preferences)
    {
        _catalog = catalog;
        _preferences = preferences;
        ConfigurationPath = preferences?.ConfigurationPath ?? "No writable preference store is connected.";
        AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";
        RuntimeDescription = RuntimeInformation.FrameworkDescription;
        OperatingSystemDescription = RuntimeInformation.OSDescription;
    }

    public ObservableCollection<string> WorkspaceRoots { get; } = [];
    public string ConfigurationPath { get; }
    public string AppVersion { get; }
    public string RuntimeDescription { get; }
    public string OperatingSystemDescription { get; }
    public bool HasPreferenceStore => _preferences is not null;
    public bool CanSave => HasPreferenceStore && IsDirty && !IsSaving;
    public bool CanReload => !IsDirty && !IsSaving;
    public bool CanDiscardChanges => IsDirty && !IsSaving;
    public bool IsGeneralSection => Section == "General";
    public bool IsWorkspaceSection => Section == "Workspace";
    public bool IsExecutionSection => Section == "Execution";
    public bool IsIntegrationsSection => Section == "Integrations";
    public bool IsDiagnosticsSection => Section == "Diagnostics";
    public bool IsEditableSection => IsGeneralSection || IsWorkspaceSection;

    [ObservableProperty] private string _section = "Workspace";
    [ObservableProperty] private string _workspaceRoot = "";
    [ObservableProperty] private bool _restoreOpenDocuments = true;
    [ObservableProperty] private decimal _activityMaxGitHubRepositories = 8;
    [ObservableProperty] private decimal _activityMaxIssuesPerRepository = 3;
    [ObservableProperty] private decimal _activityMaxEntries = 120;
    [ObservableProperty] private decimal _activityGitHubTimeoutSeconds = 60;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _status = "Settings use the machine-local Studio catalog.";
    [ObservableProperty] private string _output = "No settings were changed.";
    [ObservableProperty] private WorkspaceStudioPreferences _savedPreferences = WorkspaceStudioPreferences.Default;

    partial void OnSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsWorkspaceSection));
        OnPropertyChanged(nameof(IsExecutionSection));
        OnPropertyChanged(nameof(IsIntegrationsSection));
        OnPropertyChanged(nameof(IsDiagnosticsSection));
        OnPropertyChanged(nameof(IsEditableSection));
    }

    partial void OnRestoreOpenDocumentsChanged(bool value) => MarkDirty();
    partial void OnActivityMaxGitHubRepositoriesChanged(decimal value) => MarkDirty();
    partial void OnActivityMaxIssuesPerRepositoryChanged(decimal value) => MarkDirty();
    partial void OnActivityMaxEntriesChanged(decimal value) => MarkDirty();
    partial void OnActivityGitHubTimeoutSecondsChanged(decimal value) => MarkDirty();
    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanReload));
        OnPropertyChanged(nameof(CanDiscardChanges));
        SaveCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanReload));
        OnPropertyChanged(nameof(CanDiscardChanges));
        SaveCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
    }

    public void SetWorkspace(string root)
    {
        var full = Path.GetFullPath(root);
        WorkspaceRoot = full;
        if (IsDirty)
        {
            Status = "Unsaved settings were preserved while the workspace changed.";
            return;
        }

        ReloadCore();
    }

    [RelayCommand(CanExecute = nameof(CanReload))]
    public void Reload()
        => ReloadCore();

    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    private void DiscardChanges()
        => ReloadCore();

    private void ReloadCore()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRoot)) return;
        try
        {
            var catalog = _catalog?.Load(WorkspaceRoot)
                          ?? new WorkspaceRootCatalog(WorkspaceRoot, [WorkspaceRoot], null, [], [], WorkspaceStudioPreferences.Default);
            ApplyCatalog(catalog);
            Status = HasPreferenceStore ? "Loaded machine-local Studio settings." : "Settings are read-only in this host.";
            Output = "Settings loaded. No credentials or provider runtime state were read.";
        }
        catch (Exception ex)
        {
            Status = "Could not load Studio settings.";
            Output = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_preferences is null)
        {
            Status = "No writable preference store is connected.";
            return;
        }
        if (!TryBuildPreferences(out var requested, out var validation))
        {
            Status = validation!;
            return;
        }

        var root = WorkspaceRoot;
        var version = ++_saveVersion;
        IsSaving = true;
        Status = "Saving machine-local Studio settings…";
        try
        {
            var catalog = await Task.Run(() => _preferences.SavePreferences(requested!, root));
            if (version != _saveVersion || !SamePath(root, WorkspaceRoot)) return;
            ApplyCatalog(catalog);
            Status = "Studio settings saved.";
            Output = $"Saved local preferences to {ConfigurationPath}. No repository file or credential was changed.";
        }
        catch (Exception ex)
        {
            if (version == _saveVersion)
            {
                Status = "Could not save Studio settings.";
                Output = ex.Message;
            }
        }
        finally
        {
            if (version == _saveVersion) IsSaving = false;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        ApplyFields(WorkspaceStudioPreferences.Default, markClean: false, updateSavedPreferences: false);
        Status = IsDirty ? "Defaults are previewed. Save to apply them." : "Settings already use the defaults.";
        Output = IsDirty ? "No setting was saved yet." : "No settings change is required.";
    }

    [RelayCommand] private void ShowGeneral() => Section = "General";
    [RelayCommand] private void ShowWorkspace() => Section = "Workspace";
    [RelayCommand] private void ShowExecution() => Section = "Execution";
    [RelayCommand] private void ShowIntegrations() => Section = "Integrations";
    [RelayCommand] private void ShowDiagnostics() => Section = "Diagnostics";

    [RelayCommand]
    private void OpenConfigurationFolder()
    {
        if (_preferences is null) return;
        var directory = Path.GetDirectoryName(ConfigurationPath);
        if (directory is null || !Directory.Exists(directory)) return;
        try
        {
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            Output = "Opened the machine-local Studio configuration folder.";
        }
        catch (Exception ex)
        {
            Output = "Could not open the configuration folder: " + ex.Message;
        }
    }

    private void ApplyCatalog(WorkspaceRootCatalog catalog)
    {
        WorkspaceRoots.Clear();
        foreach (var root in catalog.RecentWorkspaceRoots) WorkspaceRoots.Add(root);
        ApplyFields(catalog.Preferences ?? WorkspaceStudioPreferences.Default, markClean: true, updateSavedPreferences: true);
    }

    private void ApplyFields(
        WorkspaceStudioPreferences preferences,
        bool markClean,
        bool updateSavedPreferences)
    {
        _applying = true;
        RestoreOpenDocuments = preferences.RestoreOpenDocuments;
        ActivityMaxGitHubRepositories = preferences.ActivityMaxGitHubRepositories;
        ActivityMaxIssuesPerRepository = preferences.ActivityMaxIssuesPerRepository;
        ActivityMaxEntries = preferences.ActivityMaxEntries;
        ActivityGitHubTimeoutSeconds = preferences.ActivityGitHubTimeoutSeconds;
        if (updateSavedPreferences) SavedPreferences = preferences;
        _applying = false;
        if (markClean) IsDirty = false;
        else MarkDirty();
    }

    private bool TryBuildPreferences(out WorkspaceStudioPreferences? preferences, out string? error)
    {
        preferences = null;
        error = null;
        if (ActivityMaxGitHubRepositories is < 0 or > 50) error = "GitHub repository limit must be between 0 and 50.";
        else if (ActivityMaxIssuesPerRepository is < 0 or > 50) error = "Issue rows per repository must be between 0 and 50.";
        else if (ActivityMaxEntries is < 20 or > 1000) error = "Activity row limit must be between 20 and 1000.";
        else if (ActivityGitHubTimeoutSeconds is < 5 or > 300) error = "GitHub timeout must be between 5 and 300 seconds.";
        if (error is not null) return false;
        preferences = new WorkspaceStudioPreferences(
            RestoreOpenDocuments,
            decimal.ToInt32(ActivityMaxGitHubRepositories),
            decimal.ToInt32(ActivityMaxIssuesPerRepository),
            decimal.ToInt32(ActivityMaxEntries),
            decimal.ToInt32(ActivityGitHubTimeoutSeconds));
        return true;
    }

    private void MarkDirty()
    {
        if (_applying) return;
        IsDirty = !TryBuildPreferences(out var current, out _) || current != SavedPreferences;
    }

    private static bool SamePath(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
