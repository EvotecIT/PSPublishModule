using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class ReleaseViewModel
{
    private const int MaximumVisibleProgressEvents = 100;
    public ObservableCollection<ReleaseArtifactProgress> ExecutionProgress { get; } = [];
    [ObservableProperty] private int _progressCompleted;
    [ObservableProperty] private int _progressTotal;
    [ObservableProperty] private string _progressStatus = "";
    public bool HasExecutionProgress => ExecutionProgress.Count > 0;
    public bool HasDeterminateProgress => ProgressTotal > 0;
    public bool ShowIndeterminateProgress => (IsSigning || IsPublishing || IsVerifying) && !HasDeterminateProgress;

    partial void OnProgressTotalChanged(int value)
    {
        OnPropertyChanged(nameof(HasDeterminateProgress));
        OnPropertyChanged(nameof(ShowIndeterminateProgress));
    }

    private void ResetExecutionProgress()
    {
        ExecutionProgress.Clear();
        ProgressCompleted = 0;
        ProgressTotal = 0;
        ProgressStatus = "";
        OnPropertyChanged(nameof(HasExecutionProgress));
    }

    private void BeginExecutionStage()
    {
        ProgressCompleted = 0;
        ProgressTotal = 0;
        ProgressStatus = "";
    }

    private void ApplyExecutionProgress(ReleaseArtifactProgress progress)
    {
        if (_disposed) return;
        ExecutionProgress.Add(progress);
        while (ExecutionProgress.Count > MaximumVisibleProgressEvents) ExecutionProgress.RemoveAt(0);
        ProgressCompleted = Math.Clamp(progress.CompletedItems, 0, Math.Max(0, progress.TotalItems));
        ProgressTotal = Math.Max(0, progress.TotalItems);
        ProgressStatus = progress.Display;
        OnPropertyChanged(nameof(HasExecutionProgress));
    }

    private sealed class LiveProgressSink(ReleaseViewModel owner) : IReleaseArtifactProgressSink
    {
        public async ValueTask ReportAsync(ReleaseArtifactProgress progress, CancellationToken cancellationToken = default)
        {
            await Dispatcher.UIThread.InvokeAsync(() => owner.ApplyExecutionProgress(progress), DispatcherPriority.Background, cancellationToken);
        }
    }
}
