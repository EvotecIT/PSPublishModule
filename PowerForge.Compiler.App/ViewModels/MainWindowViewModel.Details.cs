using System.Collections.ObjectModel;
using PowerForge.Compiler.App.Engine;

namespace PowerForge.Compiler.App.ViewModels;

/// <summary>The details panel: which files go into the output, and saved settings.</summary>
public sealed partial class MainWindowViewModel
{
    private PowerShellCompilationResourceMode _fileMode = PowerShellCompilationResourceMode.Declared;
    private string _newInclude = string.Empty;
    private string _newExclude = string.Empty;
    private bool _isDetailsOpen;
    private FileRules? _rulesWhenOpened;
    private string _settingsPath = string.Empty;
    private string _settingsInputPath = string.Empty;
    private string _settingsMessage = string.Empty;
    private SavedTarget? _pendingSavedTarget;

    private RelayCommand? _addIncludeCommand;
    private RelayCommand? _addExcludeCommand;

    /// <summary>Adds the typed include pattern.</summary>
    public RelayCommand AddIncludeCommand => _addIncludeCommand ??= new RelayCommand(AddInclude);

    /// <summary>Adds the typed exclude pattern.</summary>
    public RelayCommand AddExcludeCommand => _addExcludeCommand ??= new RelayCommand(AddExclude);

    /// <summary>Asks the window for a place to save settings; returns null when cancelled.</summary>
    public Func<string, Task<string?>>? PickSettingsSavePath { get; set; }

    /// <summary>Extra file patterns to include.</summary>
    public ObservableCollection<PatternItemViewModel> IncludePatterns { get; } = new();

    /// <summary>File patterns to leave out.</summary>
    public ObservableCollection<PatternItemViewModel> ExcludePatterns { get; } = new();

    /// <summary>True while the details panel is open.</summary>
    public bool IsDetailsOpen
    {
        get => _isDetailsOpen;
        private set => Set(ref _isDetailsOpen, value);
    }

    /// <summary>Include the script and the files it loads (the engine default).</summary>
    public bool FilesLoadedOnly
    {
        get => _fileMode == PowerShellCompilationResourceMode.Declared;
        set { if (value) SetFileMode(PowerShellCompilationResourceMode.Declared); }
    }

    /// <summary>Include everything in the script's folder.</summary>
    public bool FilesWholeFolder
    {
        get => _fileMode == PowerShellCompilationResourceMode.CompleteModule;
        set { if (value) SetFileMode(PowerShellCompilationResourceMode.CompleteModule); }
    }

    /// <summary>Include only the script itself.</summary>
    public bool FilesScriptOnly
    {
        get => _fileMode == PowerShellCompilationResourceMode.None;
        set { if (value) SetFileMode(PowerShellCompilationResourceMode.None); }
    }

    /// <summary>Pattern being typed for the include list.</summary>
    public string NewInclude
    {
        get => _newInclude;
        set => Set(ref _newInclude, value);
    }

    /// <summary>Pattern being typed for the exclude list.</summary>
    public string NewExclude
    {
        get => _newExclude;
        set => Set(ref _newExclude, value);
    }

    /// <summary>The file rules the next check and build use.</summary>
    public FileRules CurrentFileRules => new(
        _fileMode,
        IncludePatterns.Select(static item => item.Pattern).ToArray(),
        ExcludePatterns.Select(static item => item.Pattern).ToArray());

    /// <summary>Path of the settings file in use, or empty.</summary>
    public string SettingsPath
    {
        get => _settingsPath;
        private set
        {
            if (!Set(ref _settingsPath, value)) return;
            OnPropertyChanged(nameof(HasSettingsFile));
            OnPropertyChanged(nameof(SettingsSummary));
        }
    }

    /// <summary>True when the current settings came from or went to a file.</summary>
    public bool HasSettingsFile => !string.IsNullOrEmpty(SettingsPath);

    /// <summary>Result of the last open or save, shown in the panel.</summary>
    public string SettingsMessage
    {
        get => _settingsMessage;
        private set => Set(ref _settingsMessage, value);
    }

    /// <summary>One line about the file rules for the build panel.</summary>
    public string FilesSummary
    {
        get
        {
            var extra = IncludePatterns.Count + ExcludePatterns.Count;
            var basis = _fileMode switch
            {
                PowerShellCompilationResourceMode.CompleteModule => "Files: the whole folder",
                PowerShellCompilationResourceMode.None => "Files: only the script",
                _ => "Files: the script and what it loads"
            };
            return extra == 0 ? basis : $"{basis}, {extra} extra rule{(extra == 1 ? string.Empty : "s")}";
        }
    }

    /// <summary>One line about saved settings for the build panel.</summary>
    public string SettingsSummary => HasSettingsFile ? $"Settings: {Path.GetFileName(SettingsPath)}" : "Settings: not saved";

    /// <summary>Opens the details panel.</summary>
    public void OpenDetails()
    {
        _rulesWhenOpened = CurrentFileRules;
        SettingsMessage = string.Empty;
        IsDetailsOpen = true;
    }

    /// <summary>Closes the details panel and checks again when the file rules changed.</summary>
    public async Task CloseDetailsAsync()
    {
        IsDetailsOpen = false;
        if (HasInput && _rulesWhenOpened is { } before && !SameRules(before, CurrentFileRules))
            await CheckAsync(InputPath);
    }

    /// <summary>Adds the typed include pattern.</summary>
    public void AddInclude()
    {
        AddPattern(IncludePatterns, NewInclude);
        NewInclude = string.Empty;
    }

    /// <summary>Adds the typed exclude pattern.</summary>
    public void AddExclude()
    {
        AddPattern(ExcludePatterns, NewExclude);
        NewExclude = string.Empty;
    }

    /// <summary>Opens a settings file and checks the script it names.</summary>
    public async Task OpenSettingsAsync(string path)
    {
        if (IsCompiling)
        {
            SettingsMessage = "Wait for the build to finish, then open the settings.";
            StatusText = SettingsMessage;
            return;
        }

        var result = _engine.OpenSettings(path);
        if (result.Settings is not { } settings)
        {
            StatusText = result.Problem?.Message ?? "These settings can't be opened.";
            SettingsMessage = StatusText;
            return;
        }

        ApplyFileRules(settings.Files);
        // When opened from the details panel, the loaded rules are the new baseline, so Done doesn't check again
        // and replace the saved mode and target with the recommendation.
        if (IsDetailsOpen) _rulesWhenOpened = CurrentFileRules;
        SettingsPath = settings.SettingsPath;
        _settingsInputPath = settings.InputPath;
        _pendingSavedTarget = settings.Targets.FirstOrDefault();
        if (await CheckAsync(settings.InputPath))
            StatusText = $"Opened {Path.GetFileName(settings.SettingsPath)}.";
    }

    /// <summary>Saves the current selection to the settings file in use, or to <c>powerforge.psproject.json</c> next to the script.</summary>
    public Task SaveSettingsAsync() => SaveSettingsCoreAsync(askForPath: false);

    /// <summary>Saves the current selection to a new settings file.</summary>
    public Task SaveSettingsAsAsync() => SaveSettingsCoreAsync(askForPath: true);

    private async Task SaveSettingsCoreAsync(bool askForPath)
    {
        if (!HasInput || SelectedMode is not { } card || SelectedTarget is not { } target)
        {
            SettingsMessage = "Check a script first, then save its settings.";
            return;
        }

        var folder = Directory.Exists(InputPath) ? InputPath : Path.GetDirectoryName(InputPath) ?? string.Empty;
        var suggested = HasSettingsFile ? SettingsPath : Path.Combine(folder, InterimCompileEngine.DefaultSettingsFileName);
        var path = suggested;
        if (askForPath)
        {
            path = PickSettingsSavePath is null ? null : await PickSettingsSavePath(suggested);
            if (string.IsNullOrEmpty(path))
            {
                SettingsMessage = "Not saved.";
                return;
            }
        }

        var result = _engine.SaveSettings(new SaveSettingsRequest(
            path,
            InputPath,
            CurrentFileRules,
            card.Mode,
            target,
            string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory.Trim()));
        if (result.Settings is { } settings)
        {
            SettingsPath = settings.SettingsPath;
            _settingsInputPath = settings.InputPath;
            SettingsMessage = $"Saved to {settings.SettingsPath}. It holds {settings.Targets.Count} target{(settings.Targets.Count == 1 ? string.Empty : "s")}.";
            StatusText = $"Saved settings to {Path.GetFileName(settings.SettingsPath)}.";
        }
        else
        {
            SettingsMessage = result.Problem?.Message ?? "The settings weren't saved.";
        }
    }

    private SavedTarget? TakePendingSavedTarget()
    {
        var saved = _pendingSavedTarget;
        _pendingSavedTarget = null;
        return saved;
    }

    private void ApplySavedTarget(SavedTarget saved)
    {
        if (saved.ExactContract is { } contract && SelectedMode?.Mode == saved.Mode)
        {
            var target = new CompileTarget(saved.Name, saved.Mode, saved.TargetFramework, saved.RuntimeIdentifier,
                saved.Deployment, contract.SupportLevel, contract.SupportLevel is "Supported" or "PortableManaged", contract);
            Targets.Insert(0, target);
            SelectedTarget = target;
        }
        else
        {
            SelectedTarget = Targets.FirstOrDefault(target =>
                             target.TargetFramework.Equals(saved.TargetFramework, StringComparison.OrdinalIgnoreCase) &&
                             target.RuntimeIdentifier.Equals(saved.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase) &&
                             target.Deployment == saved.Deployment)
                             ?? SelectedTarget;
        }
        OutputDirectory = saved.OutputDirectory;
    }

    // Settings belong to one script; checking a different one starts from defaults.
    private void ForgetSettingsFor(string path)
    {
        if (!HasSettingsFile || SamePath(path, _settingsInputPath)) return;
        _pendingSavedTarget = null;
        SettingsPath = string.Empty;
        _settingsInputPath = string.Empty;
        OutputDirectory = string.Empty;
        ApplyFileRules(FileRules.Default);
    }

    private void ApplyFileRules(FileRules rules)
    {
        IncludePatterns.Clear();
        ExcludePatterns.Clear();
        foreach (var pattern in rules.Include) AddPattern(IncludePatterns, pattern);
        foreach (var pattern in rules.Exclude) AddPattern(ExcludePatterns, pattern);
        SetFileMode(rules.Mode);
    }

    private void SetFileMode(PowerShellCompilationResourceMode mode)
    {
        if (_fileMode == mode) return;
        _fileMode = mode;
        InvalidateReview();
        OnPropertyChanged(nameof(FilesLoadedOnly));
        OnPropertyChanged(nameof(FilesWholeFolder));
        OnPropertyChanged(nameof(FilesScriptOnly));
        OnPropertyChanged(nameof(FilesSummary));
    }

    private void AddPattern(ObservableCollection<PatternItemViewModel> list, string pattern)
    {
        var trimmed = pattern.Trim();
        if (trimmed.Length == 0 || list.Any(item => item.Pattern.Equals(trimmed, StringComparison.OrdinalIgnoreCase))) return;
        PatternItemViewModel? item = null;
        item = new PatternItemViewModel(trimmed, () =>
        {
            list.Remove(item!);
            OnPropertyChanged(nameof(FilesSummary));
        });
        list.Add(item);
        OnPropertyChanged(nameof(FilesSummary));
    }

    private static bool SameRules(FileRules left, FileRules right) =>
        left.Mode == right.Mode &&
        left.Include.SequenceEqual(right.Include, StringComparer.OrdinalIgnoreCase) &&
        left.Exclude.SequenceEqual(right.Exclude, StringComparer.OrdinalIgnoreCase);

    private static bool SamePath(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Path.GetFullPath(left).TrimEnd('\\', '/').Equals(Path.GetFullPath(right).TrimEnd('\\', '/'), comparison);
    }
}

/// <summary>One include or exclude pattern with a remove action.</summary>
public sealed class PatternItemViewModel
{
    /// <summary>Creates an item.</summary>
    public PatternItemViewModel(string pattern, Action remove)
    {
        Pattern = pattern;
        RemoveCommand = new RelayCommand(remove);
    }

    /// <summary>The pattern, relative to the script's folder.</summary>
    public string Pattern { get; }

    /// <summary>Removes the pattern.</summary>
    public RelayCommand RemoveCommand { get; }
}
