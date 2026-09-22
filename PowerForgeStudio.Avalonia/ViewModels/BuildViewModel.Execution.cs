using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class BuildViewModel
{
    private readonly IReleaseBuildExecutionService _builds;
    private CancellationTokenSource? _buildCancellation;
    private int _buildVersion;
    [ObservableProperty] private bool _hasSuccessfulInspection;
    [ObservableProperty] private bool _isBuilding;
    [ObservableProperty] private bool _isReleaseRunning;
    partial void OnIsReleaseRunningChanged(bool value) => OnPropertyChanged(nameof(CanBuild));
    [ObservableProperty] private bool _wasBuildCancelled;
    [ObservableProperty] private string _buildRoot = "";
    [ObservableProperty] private string _buildStatus = "No build started.";
    [ObservableProperty] private string _buildOutput = "";
    [ObservableProperty] private ReleaseBuildExecutionResult? _buildResult;
    public bool HasBuild => !string.IsNullOrEmpty(BuildRoot);
    public bool CanBuild => HasSuccessfulInspection && !HasUnsavedChanges && !IsBusy && !IsBuilding && !IsReleaseRunning && !_disposed;
    partial void OnHasSuccessfulInspectionChanged(bool value) { OnPropertyChanged(nameof(CanBuild)); OnPropertyChanged(nameof(EmphasizePlan)); }
    partial void OnIsBuildingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(CanPlan));
        OnPropertyChanged(nameof(EmphasizePlan));
    }
    partial void OnBuildRootChanged(string value) => OnPropertyChanged(nameof(HasBuild));

    [RelayCommand]
    public async Task BuildAsync()
    {
        if (!CanBuild) return;
        var version = ++_buildVersion;
        var root = WorkingCopyRoot;
        using var cancellation = new CancellationTokenSource();
        _buildCancellation = cancellation;
        BuildRoot = root;
        WasBuildCancelled = false;
        BuildResult = null;
        BuildOutput = "";
        BuildStatus = "Building current configuration…";
        IsBuilding = true;
        var progress = new BufferedBuildProgress(text =>
        {
            if (_disposed || version != _buildVersion) return;
            var combined = BuildOutput + text;
            BuildOutput = combined.Length > 128 * 1024 ? combined[^(128 * 1024)..] : combined;
        });
        try
        {
            var result = await Task.Run(() => _builds.ExecuteAsync(root, cancellation.Token, progress));
            if (_disposed || version != _buildVersion) return;
            BuildResult = result with { AdapterResults = result.AdapterResults.Select(x => x with {
                Summary = StudioOutputSanitizer.Sanitize(x.Summary),
                OutputTail = StudioOutputSanitizer.Sanitize(x.OutputTail),
                ErrorTail = StudioOutputSanitizer.Sanitize(x.ErrorTail)
            }).ToArray() };
            BuildStatus = cancellation.IsCancellationRequested
                ? "Build stopped after cancellation was requested. Review any completed artifacts below."
                : StudioOutputSanitizer.Sanitize(result.Summary);
        }
        catch (OperationCanceledException) { if (!_disposed) BuildStatus = "Build cancelled. Partial output may remain in the build directories."; }
        catch (Exception ex) { if (!_disposed) BuildStatus = $"Build failed: {StudioOutputSanitizer.Sanitize(ex.Message)}"; }
        finally
        {
            WasBuildCancelled = cancellation.IsCancellationRequested;
            if (ReferenceEquals(_buildCancellation, cancellation)) _buildCancellation = null;
            IsBuilding = false;
        }
    }

    [RelayCommand]
    private void CancelBuild()
    {
        BuildStatus = "Cancellation requested; waiting for the build to stop…";
        _buildCancellation?.Cancel();
    }
}
