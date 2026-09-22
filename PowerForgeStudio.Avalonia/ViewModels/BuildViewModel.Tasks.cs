using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForge;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class BuildViewModel
{
    private readonly ProjectTaskService _taskService;
    private CancellationTokenSource? _taskCancellation;
    private int _taskVersion;

    public ObservableCollection<ProjectTaskPlan> Tasks { get; } = [];
    [ObservableProperty] private ProjectTaskPlan? _selectedTask;
    [ObservableProperty] private string _taskCatalogStatus = "Inspect this working copy to discover local tasks.";
    [ObservableProperty] private string _taskStatus = "No task started.";
    [ObservableProperty] private string _taskOutput = "";
    [ObservableProperty] private string _taskRoot = "";
    [ObservableProperty] private bool _isTaskRunning;
    [ObservableProperty] private bool _hasDetectedBuildContract;
    [ObservableProperty] private bool _hasTaskConfigurationError;

    public bool HasTasks => Tasks.Count > 0;
    public bool ShowTaskSection => HasTasks || HasTaskConfigurationError;
    public bool ShowBuildContractSection => !HasTasks || HasDetectedBuildContract;
    public bool HasSelectedTask => SelectedTask is not null;
    public bool HasSelectedTaskDescription => !string.IsNullOrWhiteSpace(SelectedTask?.Description);
    public bool HasTaskRun => !string.IsNullOrEmpty(TaskRoot);
    public bool CanRunTask => SelectedTask is not null && !HasUnsavedChanges && !IsBusy && !IsBuilding && !IsTaskRunning && !IsReleaseRunning && !_disposed;
    public string SelectedTaskExecutable => StudioOutputSanitizer.Sanitize(SelectedTask?.Executable);
    public string SelectedTaskDescription => StudioOutputSanitizer.Sanitize(SelectedTask?.Description);
    public string SelectedTaskArguments => SelectedTask is null || SelectedTask.Arguments.Count == 0
        ? "No arguments"
        : string.Join("\n", SelectedTask.Arguments.Select((value, index) => $"{index + 1}. {StudioOutputSanitizer.Sanitize(value)}"));
    public string SelectedTaskWorkingDirectory => StudioOutputSanitizer.Sanitize(SelectedTask?.WorkingDirectory);
    public string SelectedTaskTimeout => SelectedTask is null ? ""
        : SelectedTask.Timeout.TotalSeconds < 60
            ? $"{SelectedTask.Timeout.TotalSeconds:0} seconds"
            : $"{SelectedTask.Timeout.TotalMinutes:0.#} minutes";

    partial void OnSelectedTaskChanged(ProjectTaskPlan? value)
    {
        OnPropertyChanged(nameof(HasSelectedTask));
        OnPropertyChanged(nameof(HasSelectedTaskDescription));
        OnPropertyChanged(nameof(CanRunTask));
        OnPropertyChanged(nameof(SelectedTaskExecutable));
        OnPropertyChanged(nameof(SelectedTaskDescription));
        OnPropertyChanged(nameof(SelectedTaskArguments));
        OnPropertyChanged(nameof(SelectedTaskWorkingDirectory));
        OnPropertyChanged(nameof(SelectedTaskTimeout));
    }

    partial void OnTaskRootChanged(string value) => OnPropertyChanged(nameof(HasTaskRun));
    partial void OnHasDetectedBuildContractChanged(bool value) => OnPropertyChanged(nameof(ShowBuildContractSection));
    partial void OnHasTaskConfigurationErrorChanged(bool value) => OnPropertyChanged(nameof(ShowTaskSection));

    partial void OnIsTaskRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunTask));
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(CanPlan));
    }

    private void ClearTaskSelection()
    {
        Tasks.Clear();
        SelectedTask = null;
        HasTaskConfigurationError = false;
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(ShowTaskSection));
        OnPropertyChanged(nameof(ShowBuildContractSection));
        if (!IsTaskRunning) TaskCatalogStatus = "Inspect this working copy to discover local tasks.";
    }

    private async Task LoadTasksAsync(string root, int version, CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await Task.Run(() => _taskService.Load(root), cancellationToken);
            if (_disposed || version != _contextVersion) return;
            foreach (var task in catalog.Tasks) Tasks.Add(task);
            OnPropertyChanged(nameof(HasTasks));
            OnPropertyChanged(nameof(ShowTaskSection));
            OnPropertyChanged(nameof(ShowBuildContractSection));
            TaskCatalogStatus = catalog.Tasks.Count == 0
                ? "No local tasks declared. Add Build/powerforge.tasks.json to this working copy."
                : $"{catalog.Tasks.Count} local task(s) from Build/powerforge.tasks.json. Select one to review its command.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (!_disposed && version == _contextVersion)
            {
                HasTaskConfigurationError = true;
                TaskCatalogStatus = $"Project tasks could not be loaded: {StudioOutputSanitizer.Sanitize(ex.Message)}";
            }
        }
    }

    [RelayCommand]
    public async Task RunTaskAsync()
    {
        if (!CanRunTask || SelectedTask is not { } plan) return;
        using var cancellation = new CancellationTokenSource();
        _taskCancellation = cancellation;
        var version = ++_taskVersion;
        TaskRoot = plan.RepositoryRoot;
        TaskOutput = "";
        TaskStatus = $"Running {plan.Name}…";
        IsTaskRunning = true;
        var progress = new BufferedBuildProgress(text =>
        {
            if (_disposed || version != _taskVersion) return;
            var combined = TaskOutput + text;
            TaskOutput = combined.Length > 128 * 1024 ? combined[^(128 * 1024)..] : combined;
        });
        try
        {
            var result = await _taskService.RunAsync(plan,
                line => progress.Report(new ReleaseBuildProgress("Task", "Output", line)),
                line => progress.Report(new ReleaseBuildProgress("Task", "Error", line)), cancellation.Token);
            if (_disposed || version != _taskVersion) return;
            TaskStatus = cancellation.IsCancellationRequested
                ? "Task cancelled. Partial local changes may remain."
                : result.StartFailed
                    ? $"Task did not start: {StudioOutputSanitizer.Sanitize(result.StdErr)}"
                    : result.TimedOut
                        ? $"Task timed out after {plan.Timeout.TotalMinutes:0.#} minutes. Partial local changes may remain."
                        : result.StandardOutputLimitExceeded || result.StandardErrorLimitExceeded
                            ? "Task output exceeded the 128 KiB capture limit. Review the project artifacts and run it in a terminal for full output."
                        : result.Succeeded
                            ? $"{plan.Name} completed in {result.Duration.TotalSeconds:0.#} seconds."
                            : $"{plan.Name} failed with exit code {result.ExitCode}.";
        }
        catch (OperationCanceledException) { if (!_disposed) TaskStatus = "Task cancelled. Partial local changes may remain."; }
        catch (Exception ex) { if (!_disposed) TaskStatus = $"Task failed: {StudioOutputSanitizer.Sanitize(ex.Message)}"; }
        finally
        {
            progress.FlushNow();
            if (ReferenceEquals(_taskCancellation, cancellation)) _taskCancellation = null;
            IsTaskRunning = false;
        }
    }

    [RelayCommand]
    private void CancelTask()
    {
        if (!IsTaskRunning) return;
        TaskStatus = "Cancellation requested; waiting for the task process to stop…";
        _taskCancellation?.Cancel();
    }
}
