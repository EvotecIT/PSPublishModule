using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Explorer;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed record GitChangeRow(GitFileChange Change, bool Staged)
{
    public string Path => Change.Path;
    public string State => Staged ? "Staged" : Change.Kind == GitChangeKind.Untracked ? "Untracked" : "Working copy";
    public string Kind => Change.KindDisplay;
}

/// <summary>Shows the current index and working-copy changes; mutations capture their original root.</summary>
public sealed partial class GitChangesViewModel : ObservableObject, IDisposable
{
    private readonly ProjectGitService _git;
    public GitChangesViewModel(ProjectGitService? git = null) => _git = git ?? new ProjectGitService();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _read;
    private int _contextVersion, _diffVersion;
    private bool _disposed;
    private readonly Dictionary<string, string> _drafts = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public ObservableCollection<GitChangeRow> Files { get; } = [];
    [ObservableProperty] private string _root = "";
    [ObservableProperty] private string _status = "Select a working copy.";
    [ObservableProperty] private string _diff = "Select a change to review.";
    [ObservableProperty] private string _commitMessage = "";
    [ObservableProperty] private string _lastOperation = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isMutating;
    [ObservableProperty] private ProjectGitStatus? _snapshot;
    [ObservableProperty] private GitChangeRow? _selected;
    public bool CanAct => !IsLoading && !IsMutating && Snapshot?.IsGitRepository == true && !_disposed;
    public bool CanStage => CanAct && Selected is { Staged: false };
    public bool CanUnstage => CanAct && Selected is { Staged: true };
    public bool CanCommit => CanAct && Snapshot is { StagedCount: > 0, HasConflicts: false } && !string.IsNullOrWhiteSpace(CommitMessage);
    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanAct)); OnPropertyChanged(nameof(CanStage));
        OnPropertyChanged(nameof(CanUnstage)); OnPropertyChanged(nameof(CanCommit));
    }
    partial void OnIsLoadingChanged(bool value) => NotifyActions();
    partial void OnIsMutatingChanged(bool value) => NotifyActions();
    partial void OnSnapshotChanged(ProjectGitStatus? value) => NotifyActions();
    partial void OnCommitMessageChanged(string value) => NotifyActions();
    partial void OnSelectedChanged(GitChangeRow? value) { NotifyActions(); _ = LoadDiffAsync(value); }

    public void SetWorkingCopy(string root)
    {
        if (string.Equals(root, Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
        if (!string.IsNullOrEmpty(Root)) _drafts[Root] = CommitMessage;
        Root = root;
        CommitMessage = _drafts.GetValueOrDefault(root, "");
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        var version = ++_contextVersion;
        _read?.Cancel();
        using var read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _read = read;
        Snapshot = null; Selected = null; Files.Clear(); Diff = "Select a change to review.";
        if (string.IsNullOrEmpty(Root)) { IsLoading = false; Status = "Select a working copy."; _read = null; return; }
        var root = Root;
        IsLoading = true;
        Status = "Reading Git status…";
        try
        {
            var snapshot = await _git.GetStatusAsync(root, read.Token);
            if (_disposed || version != _contextVersion) return;
            Snapshot = snapshot;
            foreach (var file in snapshot.StagedChanges) Files.Add(new GitChangeRow(file, true));
            foreach (var file in snapshot.UnstagedChanges.Concat(snapshot.UntrackedFiles)) Files.Add(new GitChangeRow(file, false));
            Status = !snapshot.IsGitRepository ? "This folder is not a Git working copy."
                : snapshot.HasConflicts ? "Merge conflicts need resolution before committing."
                : $"{snapshot.BranchDisplay} · {snapshot.StatusSummary}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (version == _contextVersion) Status = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { if (version == _contextVersion) { IsLoading = false; _read = null; } }
    }

    private async Task LoadDiffAsync(GitChangeRow? row)
    {
        var version = ++_diffVersion;
        if (row is null) { Diff = "Select a change to review."; return; }
        var root = Root;
        Diff = "Loading change…";
        try
        {
            var text = row.Change.Kind == GitChangeKind.Untracked
                ? await new FileExplorerService().ReadTextPreviewAsync(Path.Combine(root, row.Path), _lifetime.Token)
                : await _git.GetDiffAsync(root, row.Path, row.Staged, _lifetime.Token, row.Change.OriginalPath);
            if (!_disposed && version == _diffVersion) Diff = string.IsNullOrEmpty(text) ? "No textual diff (the change may be metadata or a rename)." : text;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed && version == _diffVersion) Diff = StudioOutputSanitizer.Sanitize(ex.Message); }
    }

    [RelayCommand]
    private Task StageAsync()
    {
        var row = Selected;
        return CanStage && row is not null ? MutateAsync("Stage", (root, ct) => _git.StageFileAsync(root, row.Path, ct)) : Task.CompletedTask;
    }
    [RelayCommand]
    private Task UnstageAsync()
    {
        var row = Selected;
        return CanUnstage && row is not null ? MutateAsync("Unstage", (root, ct) => _git.UnstageFileAsync(root, row.Path, ct, row.Change.OriginalPath)) : Task.CompletedTask;
    }
    [RelayCommand]
    private Task CommitAsync()
    {
        var message = CommitMessage;
        return CanCommit ? MutateAsync("Commit staged changes", (root, ct) => _git.CommitAsync(root, message, ct), committedMessage: message) : Task.CompletedTask;
    }

    private async Task MutateAsync(string action, Func<string, CancellationToken, Task<bool>> execute, string? committedMessage = null)
    {
        var root = Root;
        IsMutating = true;
        try
        {
            await execute(root, _lifetime.Token);
            LastOperation = $"{action} completed in {root}.";
            if (committedMessage is not null)
            {
                if (Root == root && CommitMessage == committedMessage) CommitMessage = "";
                if (_drafts.GetValueOrDefault(root) == committedMessage) _drafts.Remove(root);
            }
        }
        catch (OperationCanceledException) { LastOperation = $"{action} was interrupted in {root}. Refresh its Git state before retrying."; }
        catch (Exception ex) { LastOperation = $"{action} failed in {root}: {StudioOutputSanitizer.Sanitize(ex.Message)}"; }
        finally { IsMutating = false; if (!_disposed) await RefreshAsync(); }
    }

    public void Dispose() { _disposed = true; _lifetime.Cancel(); _read?.Cancel(); _lifetime.Dispose(); }
}
