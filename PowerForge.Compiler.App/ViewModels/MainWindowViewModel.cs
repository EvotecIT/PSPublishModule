using System.Collections.ObjectModel;
using PowerForge.Compiler.App.Engine;

namespace PowerForge.Compiler.App.ViewModels;

/// <summary>State and actions for the single window.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int MaximumLogLines = 400;

    private readonly ICompileEngine _engine;
    private CancellationTokenSource? _operation;
    private CheckResult? _check;
    private ModeCardViewModel? _selectedMode;
    private CompileTarget? _selectedTarget;
    private string _inputPath = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _statusText = string.Empty;
    private bool _isChecking;
    private bool _isCompiling;
    private CompileResult? _lastResult;
    private int _checkGeneration;
    private bool _signOutput;
    private SigningCertificate? _selectedCertificate;
    private string _signingHint = string.Empty;

    /// <summary>Creates the view model.</summary>
    public MainWindowViewModel(ICompileEngine engine)
    {
        _engine = engine;
        CompileCommand = new RelayCommand(() => _ = CompileAsync(), () => CanCompile);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        RecheckCommand = new RelayCommand(() => _ = CheckAsync(InputPath), () => HasInput && !IsBusy);
        Log.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLog));
    }

    /// <summary>Opens a folder in the system file manager; supplied by the window.</summary>
    public Func<string, Task>? OpenFolder { get; set; }

    /// <summary>Mode cards for Package, Hybrid and Strict.</summary>
    public ObservableCollection<ModeCardViewModel> Modes { get; } = new();

    /// <summary>Findings for the selected mode, plus input problems.</summary>
    public ObservableCollection<DiagnosticItemViewModel> Diagnostics { get; } = new();

    /// <summary>Dependencies for the selected mode.</summary>
    public ObservableCollection<CompileDependency> Dependencies { get; } = new();

    /// <summary>Targets for the selected mode.</summary>
    public ObservableCollection<CompileTarget> Targets { get; } = new();

    /// <summary>Build output lines.</summary>
    public ObservableCollection<LogLineViewModel> Log { get; } = new();

    /// <summary>Builds the selected mode for the selected target.</summary>
    public RelayCommand CompileCommand { get; }

    /// <summary>Cancels the running check or build.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Checks the current input again.</summary>
    public RelayCommand RecheckCommand { get; }

    /// <summary>The checked path.</summary>
    public string InputPath
    {
        get => _inputPath;
        private set
        {
            if (!Set(ref _inputPath, value)) return;
            OnPropertyChanged(nameof(HasInput));
            OnPropertyChanged(nameof(InputName));
        }
    }

    /// <summary>The checked file or folder name.</summary>
    public string InputName => HasInput ? Path.GetFileName(InputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : string.Empty;

    /// <summary>True once something has been dropped or chosen.</summary>
    public bool HasInput => !string.IsNullOrEmpty(InputPath);

    /// <summary>True when a check produced mode results.</summary>
    public bool HasCheck => _check is not null && _check.Modes.Count > 0;

    /// <summary>Kind and file count of the checked input.</summary>
    public string InputSummary => _check?.Kind is { } kind
        ? $"{DescribeKind(kind)} from {_check.SourceFiles.Count} source file{(_check.SourceFiles.Count == 1 ? string.Empty : "s")}, checked in {_check.Elapsed.TotalSeconds:0.0} s"
        : string.Empty;

    /// <summary>Output folder; empty means the engine default next to the input.</summary>
    public string OutputDirectory
    {
        get => _outputDirectory;
        set => Set(ref _outputDirectory, value);
    }

    /// <summary>Code-signing certificates, loaded when signing is turned on.</summary>
    public ObservableCollection<SigningCertificate> Certificates { get; } = new();

    /// <summary>Sign the output with a code-signing certificate.</summary>
    public bool SignOutput
    {
        get => _signOutput;
        set
        {
            if (!Set(ref _signOutput, value)) return;
            if (value && Certificates.Count == 0) LoadCertificates();
            RaiseCommandStates();
        }
    }

    /// <summary>The certificate to sign with.</summary>
    public SigningCertificate? SelectedCertificate
    {
        get => _selectedCertificate;
        set
        {
            if (Set(ref _selectedCertificate, value)) RaiseCommandStates();
        }
    }

    /// <summary>Explains the signing state under the picker.</summary>
    public string SigningHint
    {
        get => _signingHint;
        private set => Set(ref _signingHint, value);
    }

    /// <summary>One line describing what is happening or what to do next.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    /// <summary>The mode the user will build.</summary>
    public ModeCardViewModel? SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!Set(ref _selectedMode, value)) return;
            foreach (var card in Modes) card.IsSelected = ReferenceEquals(card, value);
            OnPropertyChanged(nameof(SelectedVerdict));
            OnPropertyChanged(nameof(CompileButtonText));
            RefreshSelectedMode();
        }
    }

    /// <summary>The target the user will build for.</summary>
    public CompileTarget? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (Set(ref _selectedTarget, value)) RaiseCommandStates();
        }
    }

    /// <summary>True while checking.</summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (!Set(ref _isChecking, value)) return;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(ShowInputProblems));
            RaiseCommandStates();
        }
    }

    /// <summary>True while building.</summary>
    public bool IsCompiling
    {
        get => _isCompiling;
        private set
        {
            if (!Set(ref _isCompiling, value)) return;
            OnPropertyChanged(nameof(IsBusy));
            RaiseCommandStates();
        }
    }

    /// <summary>True while checking or building.</summary>
    public bool IsBusy => IsChecking || IsCompiling;

    /// <summary>True when the selected mode can be built now.</summary>
    public bool CanCompile => !IsBusy && SelectedMode is { IsBlocked: false } && SelectedTarget is not null &&
                              (!SignOutput || SelectedCertificate is not null);

    /// <summary>Number of errors and warnings shown.</summary>
    public string ProblemsHeader
    {
        get
        {
            var errors = Diagnostics.Count(static item => item.Diagnostic.Severity == CompileSeverity.Error);
            var warnings = Diagnostics.Count(static item => item.Diagnostic.Severity == CompileSeverity.Warning);
            var notes = Diagnostics.Count - errors - warnings;
            return errors + warnings == 0 ? $"Problems ({notes})" : $"Problems ({errors + warnings})";
        }
    }

    /// <summary>Header for the dependency list.</summary>
    public string DependenciesHeader => $"Dependencies ({Dependencies.Count})";

    /// <summary>True when the problems list has entries.</summary>
    public bool HasDiagnostics => Diagnostics.Count > 0;

    /// <summary>True when the dependency list has entries.</summary>
    public bool HasDependencies => Dependencies.Count > 0;

    /// <summary>True when the last build succeeded.</summary>
    public bool HasResult => _lastResult is { Succeeded: true };

    /// <summary>Headline for the last build.</summary>
    public string ResultTitle => _lastResult is { Succeeded: true } result ? $"Compiled {Path.GetFileName(result.ArtifactPath)}" : string.Empty;

    /// <summary>Plain description of what the last build contains.</summary>
    public string ResultSummary => _lastResult is { Succeeded: true } result ? DescribeOutcome(result.Summary, result.TypedUnits, result.RuntimeUnits) : string.Empty;

    /// <summary>Full path of the last artifact.</summary>
    public string ResultPath => _lastResult?.ArtifactPath ?? string.Empty;

    /// <summary>True when the input itself has problems and no mode could be checked.</summary>
    public bool ShowInputProblems => HasInput && !HasCheck && !IsChecking && Diagnostics.Count > 0;

    /// <summary>True once the build has written output.</summary>
    public bool HasLog => Log.Count > 0;

    /// <summary>Compile button text naming what will be built.</summary>
    public string CompileButtonText => SelectedMode is { IsBlocked: true }
        ? $"{SelectedMode.Title} can't build this"
        : _check?.Kind == PowerShellCompilationArtifactKind.BinaryModule ? "Compile module" : "Compile executable";

    /// <summary>The selected mode's verdict sentence.</summary>
    public string SelectedVerdict => SelectedMode?.Verdict ?? string.Empty;

    /// <summary>True when the output carries the script as text anyone can read back.</summary>
    public bool ResultContainsReadableSource => _lastResult is { Succeeded: true, ContainsReadableSource: true };

    /// <summary>Path, size and time of the last build.</summary>
    public string ResultDetail => _lastResult is { Succeeded: true } result
        ? $"{FormatSize(result.ArtifactSizeBytes)}, built in {result.Elapsed.TotalSeconds:0} s{(result.Signed ? ", signed" : string.Empty)}"
        : string.Empty;

    /// <summary>Starts a check for a dropped or chosen path.</summary>
    public Task SetInputAsync(string path)
    {
        if (path.EndsWith(".psproject.json", StringComparison.OrdinalIgnoreCase)) return OpenSettingsAsync(path);
        ForgetSettingsFor(path);
        return CheckAsync(path);
    }

    /// <summary>Opens the folder that holds the last artifact.</summary>
    public async Task OpenOutputAsync()
    {
        var folder = _lastResult?.ArtifactPath is { } path ? Path.GetDirectoryName(path) : null;
        if (folder is not null && OpenFolder is not null) await OpenFolder(folder);
    }

    // Returns true when this check's result was applied; false when it was cancelled or superseded.
    private async Task<bool> CheckAsync(string path)
    {
        Cancel();
        var generation = ++_checkGeneration;
        using var operation = BeginOperation();
        // A different input must never be compiled with the previous input's mode and target, even if its check is cancelled.
        if (!string.Equals(path, InputPath, StringComparison.Ordinal)) ClearCheck();
        InputPath = path;
        _lastResult = null;
        RaiseResultChanged();
        Log.Clear();
        IsChecking = true;
        StatusText = $"Checking {InputName} in all three modes…";
        try
        {
            var check = await _engine.CheckAsync(path, CurrentFileRules, operation.Token);
            // A check that started later owns the window now; a cancelled one may still finish normally.
            if (generation != _checkGeneration) return false;
            ApplyCheck(check);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (generation == _checkGeneration) StatusText = "Check cancelled.";
            return false;
        }
        finally
        {
            if (generation == _checkGeneration) IsChecking = false;
        }
    }

    private void ApplyCheck(CheckResult check)
    {
        _check = check;
        Modes.Clear();
        foreach (var mode in check.Modes)
            Modes.Add(new ModeCardViewModel(mode, isRecommended: mode.Mode == check.Recommended));
        OnPropertyChanged(nameof(HasCheck));
        OnPropertyChanged(nameof(InputSummary));

        var saved = TakePendingSavedTarget();
        var initial = (saved is null ? null : Modes.FirstOrDefault(card => card.Mode == saved.Mode))
                      ?? Modes.FirstOrDefault(static card => card.IsRecommended) ?? Modes.FirstOrDefault();
        _selectedMode = null;
        SelectedMode = initial;
        if (initial is null) RefreshSelectedMode();
        if (saved is not null) ApplySavedTarget(saved);

        StatusText = check.Modes.Count == 0
            ? "This input can't be checked. See the problems below."
            : check.Recommended is { } recommended
                ? $"{recommended} suits this input best. Pick a target and compile."
                : "No mode can build this input yet. Each mode lists what blocks it.";
    }

    private void RefreshSelectedMode()
    {
        Dependencies.Clear();
        Targets.Clear();
        if (_check is not null && SelectedMode is { } card)
        {
            foreach (var dependency in card.Check.Dependencies) Dependencies.Add(dependency);
            if (_check.Kind is { } kind)
                foreach (var target in _engine.GetTargets(card.Mode, kind)) Targets.Add(target);
        }

        SelectedTarget = Targets.FirstOrDefault();
        RefreshProblems();
        RaiseCommandStates();
    }

    // The problems list shows the check's findings for the selected mode; a failed build replaces them until the next build or check.
    private void RefreshProblems()
    {
        Diagnostics.Clear();
        if (_check is not null)
        {
            foreach (var diagnostic in _check.InputDiagnostics) Diagnostics.Add(new DiagnosticItemViewModel(diagnostic));
            if (SelectedMode is { } card)
                foreach (var diagnostic in card.Check.Diagnostics) Diagnostics.Add(new DiagnosticItemViewModel(diagnostic));
        }

        RaiseListsChanged();
    }

    private void ClearCheck()
    {
        _check = null;
        Modes.Clear();
        _selectedMode = null;
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(SelectedVerdict));
        OnPropertyChanged(nameof(CompileButtonText));
        OnPropertyChanged(nameof(HasCheck));
        OnPropertyChanged(nameof(InputSummary));
        RefreshSelectedMode();
    }

    private async Task CompileAsync()
    {
        if (SelectedMode is not { } card || SelectedTarget is not { } target) return;
        using var operation = BeginOperation();
        _lastResult = null;
        RaiseResultChanged();
        RefreshProblems();
        Log.Clear();
        IsCompiling = true;
        StatusText = $"Compiling {InputName} in {card.Title} for {target.DisplayName}. The first build for a target takes a minute or so.";
        var progress = new Progress<CompileProgress>(AppendLog);
        try
        {
            var request = new CompileRequest(
                InputPath,
                card.Mode,
                target,
                string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory.Trim(),
                SignOutput ? SelectedCertificate?.Thumbprint : null,
                CurrentFileRules);
            var result = await _engine.CompileAsync(request, progress, operation.Token);
            _lastResult = result;
            if (!result.Succeeded)
            {
                Diagnostics.Clear();
                foreach (var diagnostic in result.Diagnostics) Diagnostics.Add(new DiagnosticItemViewModel(diagnostic));
                RaiseListsChanged();
                StatusText = "The build failed. The problems list shows why.";
            }
            else
            {
                StatusText = $"Compiled {Path.GetFileName(result.ArtifactPath)}.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Build cancelled.";
        }
        finally
        {
            IsCompiling = false;
            RaiseResultChanged();
        }
    }

    private void LoadCertificates()
    {
        try
        {
            foreach (var certificate in _engine.GetSigningCertificates()) Certificates.Add(certificate);
            SelectedCertificate = Certificates.FirstOrDefault();
            SigningHint = Certificates.Count == 0
                ? OperatingSystem.IsWindows()
                    ? "No valid code-signing certificate with a private key was found in your certificate store."
                    : "Signing needs Windows for now."
                : "Signing may ask for your certificate's PIN or a touch on your security key.";
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
        {
            SigningHint = $"The certificate store couldn't be read: {ex.Message}";
        }
    }

    private void AppendLog(CompileProgress line)
    {
        Log.Add(new LogLineViewModel(line.Message, line.Severity));
        while (Log.Count > MaximumLogLines) Log.RemoveAt(0);
    }

    private void Cancel() => _operation?.Cancel();

    private OperationScope BeginOperation()
    {
        var source = new CancellationTokenSource();
        _operation = source;
        return new OperationScope(this, source);
    }

    private void RaiseCommandStates()
    {
        OnPropertyChanged(nameof(CanCompile));
        CompileCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        RecheckCommand.RaiseCanExecuteChanged();
    }

    private void RaiseListsChanged()
    {
        OnPropertyChanged(nameof(ProblemsHeader));
        OnPropertyChanged(nameof(DependenciesHeader));
        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(ShowInputProblems));
    }

    private void RaiseResultChanged()
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultDetail));
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(ResultPath));
        OnPropertyChanged(nameof(ResultContainsReadableSource));
    }

    internal static string DescribeOutcome(CompileOutcome outcome, int typedUnits, int runtimeUnits) => outcome switch
    {
        CompileOutcome.Native => "Everything was compiled to .NET. It runs without PowerShell.",
        CompileOutcome.Mixed => $"{typedUnits} of {typedUnits + runtimeUnits} parts were compiled to .NET; the rest runs on the PowerShell runtime inside it.",
        CompileOutcome.Packaged => "The script was packed with the PowerShell runtime. Nothing was compiled to .NET.",
        _ => "Not built."
    };

    private static string DescribeKind(PowerShellCompilationArtifactKind kind) => kind switch
    {
        PowerShellCompilationArtifactKind.Executable => "Executable",
        PowerShellCompilationArtifactKind.BinaryModule => "Binary module",
        _ => kind.ToString()
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} B"
    };

    private sealed class OperationScope : IDisposable
    {
        private readonly MainWindowViewModel _owner;
        private readonly CancellationTokenSource _source;

        public OperationScope(MainWindowViewModel owner, CancellationTokenSource source)
        {
            _owner = owner;
            _source = source;
        }

        public CancellationToken Token => _source.Token;

        public void Dispose()
        {
            if (ReferenceEquals(_owner._operation, _source)) _owner._operation = null;
            _source.Dispose();
        }
    }
}
