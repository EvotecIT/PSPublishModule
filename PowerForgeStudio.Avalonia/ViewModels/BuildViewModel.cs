using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Avalonia.ViewModels;

/// <summary>Explicit planning for the selected working copy through the shared planner.</summary>
public sealed partial class BuildViewModel : ObservableObject, IDisposable
{
    private readonly IRepositoryPlanPreviewService _planner;
    private CancellationTokenSource? _operation;
    private int _contextVersion;
    private bool _disposed;
    public BuildViewModel(IRepositoryPlanPreviewService? planner = null) => _planner = planner ?? new RepositoryPlanPreviewService();
    public ObservableCollection<RepositoryPlanResult> Results { get; } = [];
    [ObservableProperty] private string _workingCopyRoot = "";
    [ObservableProperty] private string _status = "Select a working copy to inspect its build contract.";
    [ObservableProperty] private string _contracts = "No contract inspected.";
    [ObservableProperty] private bool _isBusy;
    public bool CanPlan => !IsBusy && !string.IsNullOrEmpty(WorkingCopyRoot);
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanPlan));

    public void SetWorkingCopy(string root)
    {
        if (string.Equals(root, WorkingCopyRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
        ++_contextVersion;
        _operation?.Cancel();
        WorkingCopyRoot = root;
        Results.Clear();
        Contracts = "No contract inspected.";
        Status = string.IsNullOrEmpty(root) ? "Select a working copy to inspect its build contract." : "Ready to inspect and plan this working copy.";
        OnPropertyChanged(nameof(CanPlan));
    }

    [RelayCommand]
    public async Task PlanAsync()
    {
        if (!CanPlan || _disposed) return;
        var version = _contextVersion;
        var root = WorkingCopyRoot;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        IsBusy = true;
        Results.Clear();
        Status = "Inspecting build contracts…";
        try
        {
            var repository = await Task.Run(() => new RepositoryCatalogScanner().InspectRepository(root), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_disposed || version != _contextVersion) return;
            Contracts = string.Join("\n", new[] { repository.UnifiedReleaseConfigPath, repository.ModuleBuildScriptPath, repository.ProjectBuildScriptPath }
                .Where(x => !string.IsNullOrEmpty(x)).Distinct());
            if (!repository.IsReleaseManaged)
            {
                Contracts = "No supported contract found.";
                Status = "Add Build/project.build.json, a supported module/release JSON contract, or a PowerForge build script.";
                return;
            }
            Status = "Inspecting configuration and generating available plans…";
            var results = await Task.Run(() => _planner.PlanRepositoryAsync(repository, cancellation.Token), cancellation.Token);
            if (_disposed || version != _contextVersion) return;
            foreach (var result in results) Results.Add(result);
            Status = results.Any(x => x.Status == RepositoryPlanStatus.Failed) ? "Inspection failed. Review the details below." : "Inspection complete. Each result states whether configuration was validated, exported or planned.";
        }
        catch (OperationCanceledException) { if (!_disposed && version == _contextVersion) Status = "Planning cancelled."; }
        catch (Exception ex) { if (!_disposed && version == _contextVersion) Status = ex.Message; }
        finally { if (ReferenceEquals(_operation, cancellation)) _operation = null; IsBusy = false; }
    }

    [RelayCommand]
    private void Cancel() { Status = "Cancellation requested; waiting for the planner to stop…"; _operation?.Cancel(); }

    public void Dispose() { _disposed = true; ++_contextVersion; _operation?.Cancel(); }
}
