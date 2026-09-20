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
    [ObservableProperty] private string _buildRoot = "";
    [ObservableProperty] private string _buildStatus = "No build started.";
    [ObservableProperty] private string _buildOutput = "";
    [ObservableProperty] private ReleaseBuildExecutionResult? _buildResult;
    public bool HasBuild => !string.IsNullOrEmpty(BuildRoot);
    public bool CanBuild => HasSuccessfulInspection && !IsBusy && !IsBuilding && !_disposed;
    partial void OnHasSuccessfulInspectionChanged(bool value) => OnPropertyChanged(nameof(CanBuild));
    partial void OnIsBuildingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(CanPlan));
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
        BuildResult = null;
        BuildOutput = "";
        BuildStatus = "Building current configuration…";
        IsBuilding = true;
        var progress = new Progress<ReleaseBuildProgress>(update =>
        {
            if (_disposed || version != _buildVersion) return;
            var line = $"{update.Phase} · {update.State} · {update.Detail}";
            var text = BuildOutput + $"\n[{DateTime.Now:HH:mm:ss}] {line}";
            BuildOutput = text.Length > 128 * 1024 ? text[^(128 * 1024)..] : text;
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
