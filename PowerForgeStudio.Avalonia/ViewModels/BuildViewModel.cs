using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForge;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Avalonia.ViewModels;

/// <summary>Explicit planning for the selected working copy through the shared planner.</summary>
public sealed partial class BuildViewModel : ObservableObject, IDisposable
{
    private const string ScriptPlanningNotice = "Planning requests no build or publication. Script-backed contracts can still run trusted PowerShell with side effects; inspect their scripts first. Reviewed actions omit arguments, and output may contain secrets.";
    private const string JsonPlanningNotice = "JSON inspection generates a build-only plan without requesting project scripts or publication. Review the local actions below before building.";
    private readonly IRepositoryPlanPreviewService _planner;
    private CancellationTokenSource? _operation;
    private int _contextVersion;
    private bool _disposed;
    public BuildViewModel(IRepositoryPlanPreviewService? planner = null, IReleaseBuildExecutionService? builds = null,
        ProjectTaskService? tasks = null)
    {
        _planner = planner ?? new RepositoryPlanPreviewService();
        _builds = builds ?? new ReleaseBuildExecutionService();
        _taskService = tasks ?? new ProjectTaskService();
    }
    public ObservableCollection<RepositoryPlanResult> Results { get; } = [];
    [ObservableProperty] private string _workingCopyRoot = "";
    [ObservableProperty] private string _status = "Select a project to inspect its build contract.";
    [ObservableProperty] private bool _planningUsesScript = true;
    public string PlanningNotice => PlanningUsesScript ? ScriptPlanningNotice : JsonPlanningNotice;
    public string ContextSafetyNotice => PlanningUsesScript
        ? "Studio requests build-only execution. Project scripts can ignore switches or have external effects; inspect artifacts before a Studio release."
        : "JSON-only inspection does not run project scripts or publish. Review the plan, then inspect built artifacts before a Studio release.";
    partial void OnPlanningUsesScriptChanged(bool value)
    {
        OnPropertyChanged(nameof(PlanningNotice));
        OnPropertyChanged(nameof(ContextSafetyNotice));
    }
    [ObservableProperty] private string _contracts = "No contract inspected.";
    public string ContractDisplay => string.IsNullOrEmpty(WorkingCopyRoot) ? Contracts : string.Join("\n", Contracts.Split('\n')
        .Select(path => Path.IsPathFullyQualified(path) ? Path.GetRelativePath(WorkingCopyRoot, path) : path));
    partial void OnContractsChanged(string value) => OnPropertyChanged(nameof(ContractDisplay));
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasUnsavedChanges;
    partial void OnHasUnsavedChangesChanged(bool value)
    {
        if (value)
        {
            ++_contextVersion;
            _operation?.Cancel();
            Results.Clear();
            ClearTaskSelection();
            HasSuccessfulInspection = false;
            Status = "Configuration changed. Save or discard drafts, then inspect again.";
        }
        OnPropertyChanged(nameof(CanPlan)); OnPropertyChanged(nameof(EmphasizePlan)); OnPropertyChanged(nameof(CanBuild)); OnPropertyChanged(nameof(CanRunTask));
    }
    public bool CanPlan => !HasUnsavedChanges && !IsBusy && !IsBuilding && !IsTaskRunning && !_disposed && !string.IsNullOrEmpty(WorkingCopyRoot);
    public bool EmphasizePlan => CanPlan && !HasSuccessfulInspection;
    partial void OnWorkingCopyRootChanged(string value) { OnPropertyChanged(nameof(EmphasizePlan)); OnPropertyChanged(nameof(ContractDisplay)); }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanPlan)); OnPropertyChanged(nameof(EmphasizePlan)); OnPropertyChanged(nameof(CanBuild)); OnPropertyChanged(nameof(CanRunTask)); }

    public void SetWorkingCopy(string root)
    {
        if (string.Equals(root, WorkingCopyRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
        ++_contextVersion;
        _operation?.Cancel();
        WorkingCopyRoot = root;
        Results.Clear();
        ClearTaskSelection();
        HasSuccessfulInspection = false;
        HasDetectedBuildContract = false;
        PlanningUsesScript = true;
        Contracts = "No contract inspected.";
        Status = string.IsNullOrEmpty(root) ? "Select a project to inspect its build contract." : "Ready to inspect and plan this project.";
        OnPropertyChanged(nameof(CanPlan));
        OnPropertyChanged(nameof(EmphasizePlan));
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
        ClearTaskSelection();
        HasSuccessfulInspection = false;
        Status = "Inspecting build contracts…";
        try
        {
            var repository = await Task.Run(() => new RepositoryCatalogScanner().InspectRepository(root), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_disposed || version != _contextVersion) return;
            HasDetectedBuildContract = repository.IsReleaseManaged;
            PlanningUsesScript = !string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath) ||
                                 IsPowerShellScript(repository.ModuleBuildScriptPath) ||
                                 IsPowerShellScript(repository.ProjectBuildScriptPath);
            Contracts = string.Join("\n", new[] { repository.UnifiedReleaseConfigPath, repository.ModuleBuildScriptPath, repository.ProjectBuildScriptPath }
                .Where(x => !string.IsNullOrEmpty(x)).Distinct());
            await LoadTasksAsync(root, version, cancellation.Token);
            if (_disposed || version != _contextVersion) return;
            if (!repository.IsReleaseManaged)
            {
                Contracts = Tasks.Count > 0 ? "No package build contract found." : "No supported contract found.";
                Status = Tasks.Count > 0
                    ? "Project tasks are ready below. No package build contract was detected."
                    : "Add Build/project.build.json, a supported module/release JSON contract, or a PowerForge build script.";
                return;
            }
            Status = "Inspecting configuration and generating available plans…";
            var results = await Task.Run(() => _planner.PlanRepositoryAsync(repository, cancellation.Token), cancellation.Token);
            if (_disposed || version != _contextVersion) return;
            foreach (var result in results) Results.Add(result with
            {
                Summary = StudioOutputSanitizer.Sanitize(result.Summary),
                OutputTail = result.OutputTail is null ? null : StudioOutputSanitizer.Sanitize(result.OutputTail),
                ErrorTail = result.ErrorTail is null ? null : StudioOutputSanitizer.Sanitize(result.ErrorTail)
            });
            HasSuccessfulInspection = results.Count > 0 && results.All(x => x.Status == RepositoryPlanStatus.Succeeded);
            Status = results.Any(x => x.Status == RepositoryPlanStatus.Failed) ? "Inspection failed. Review the details below." : "Inspection complete. Each result states whether configuration was validated, exported or planned.";
        }
        catch (OperationCanceledException) { if (!_disposed && version == _contextVersion) Status = "Planning cancelled."; }
        catch (Exception ex) { if (!_disposed && version == _contextVersion) Status = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { if (ReferenceEquals(_operation, cancellation)) _operation = null; IsBusy = false; }
    }

    [RelayCommand]
    private void Cancel() { Status = "Cancellation requested; waiting for the planner to stop…"; _operation?.Cancel(); }

    private static bool IsPowerShellScript(string? path)
        => path?.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) == true;

    public void Dispose() { _disposed = true; ++_contextVersion; _operation?.Cancel(); _buildCancellation?.Cancel(); _taskCancellation?.Cancel(); }
}
