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
    public string ChangeType => Change.Kind.ToString();
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
    private DateTimeOffset? _observedAtUtc;
    private readonly Dictionary<string, string> _drafts = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Dictionary<string, string> _branchDrafts = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public ObservableCollection<GitChangeRow> Files { get; } = [];
    public ObservableCollection<string> Branches { get; } = [];
    [ObservableProperty] private string _root = "";
    [ObservableProperty] private string _status = "Select a working copy.";
    [ObservableProperty] private string _diff = "Select a change to review.";
    [ObservableProperty] private string _commitMessage = "";
    [ObservableProperty] private string _newBranchName = "";
    [ObservableProperty] private string? _selectedBranch;
    [ObservableProperty] private string _lastOperation = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isMutating;
    [ObservableProperty] private bool _isStale;
    [ObservableProperty] private ProjectGitStatus? _snapshot;
    [ObservableProperty] private GitChangeRow? _selected;
    public bool HasSelection => Selected is not null;
    public bool CanAct => !IsLoading && !IsMutating && !IsStale && Snapshot?.IsGitRepository == true && !_disposed;
    public bool CanStage => CanAct && Selected is { Staged: false };
    public bool CanUnstage => CanAct && Selected is { Staged: true };
    public bool CanCommit => CanAct && Snapshot is { StagedCount: > 0, HasConflicts: false } && !string.IsNullOrWhiteSpace(CommitMessage);
    public bool CanCreateBranch => CanAct && !string.IsNullOrWhiteSpace(NewBranchName);
    public bool CanSwitchBranch => CanAct && !string.IsNullOrWhiteSpace(SelectedBranch)
        && !string.Equals(Snapshot?.BranchName, SelectedBranch, StringComparison.Ordinal);
    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanAct)); OnPropertyChanged(nameof(CanStage));
        OnPropertyChanged(nameof(CanUnstage)); OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CanCreateBranch)); OnPropertyChanged(nameof(CanSwitchBranch));
    }
    partial void OnIsLoadingChanged(bool value) => NotifyActions();
    partial void OnIsMutatingChanged(bool value) => NotifyActions();
    partial void OnIsStaleChanged(bool value) => NotifyActions();
    partial void OnSnapshotChanged(ProjectGitStatus? value) => NotifyActions();
    partial void OnCommitMessageChanged(string value) => NotifyActions();
    partial void OnNewBranchNameChanged(string value) => NotifyActions();
    partial void OnSelectedBranchChanged(string? value) => NotifyActions();
    partial void OnSelectedChanged(GitChangeRow? value) { OnPropertyChanged(nameof(HasSelection)); NotifyActions(); _ = LoadDiffAsync(value); }

    public void SetWorkingCopy(string root)
    {
        if (string.Equals(root, Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
        if (!string.IsNullOrEmpty(Root))
        {
            _drafts[Root] = CommitMessage;
            _branchDrafts[Root] = NewBranchName;
        }
        ClearSnapshot();
        Root = root;
        CommitMessage = _drafts.GetValueOrDefault(root, "");
        NewBranchName = _branchDrafts.GetValueOrDefault(root, "");
        SelectedBranch = null;
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
        if (string.IsNullOrEmpty(Root)) { ClearSnapshot(); IsLoading = false; Status = "Select a working copy."; _read = null; return; }
        var root = Root;
        IsLoading = true;
        Status = Snapshot is null ? "Reading Git status…"
            : $"Refreshing Git state… showing the observation from {_observedAtUtc:yyyy-MM-dd HH:mm:ss} UTC.";
        try
        {
            var snapshot = await _git.GetStatusAsync(root, read.Token);
            if (_disposed || version != _contextVersion) return;
            var selectedPath = Selected?.Path;
            var selectedStaged = Selected?.Staged;
            Selected = null;
            Snapshot = snapshot;
            Files.Clear(); Branches.Clear(); Diff = "Select a change to review.";
            foreach (var branch in snapshot.Branches) Branches.Add(branch);
            SelectedBranch = snapshot.Branches.Contains(SelectedBranch, StringComparer.Ordinal)
                ? SelectedBranch
                : snapshot.Branches.Contains(snapshot.BranchName, StringComparer.Ordinal) ? snapshot.BranchName
                : snapshot.Branches.FirstOrDefault();
            foreach (var file in snapshot.StagedChanges) Files.Add(new GitChangeRow(file, true));
            foreach (var file in snapshot.UnstagedChanges.Concat(snapshot.UntrackedFiles)) Files.Add(new GitChangeRow(file, false));
            if (selectedPath is not null)
                Selected = Files.FirstOrDefault(row => row.Staged == selectedStaged &&
                    string.Equals(row.Path, selectedPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            _observedAtUtc = DateTimeOffset.UtcNow;
            IsStale = false;
            Status = !snapshot.IsGitRepository ? "This folder is not a Git working copy."
                : snapshot.HasConflicts ? "Merge conflicts need resolution before committing."
                : $"{snapshot.BranchDisplay} · {snapshot.StatusSummary} · observed {_observedAtUtc:yyyy-MM-dd HH:mm:ss} UTC";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version != _contextVersion) return;
            IsStale = true;
            var error = StudioOutputSanitizer.Sanitize(ex.Message);
            Status = Snapshot is null ? error
                : $"Git refresh failed; showing the observation from {_observedAtUtc:yyyy-MM-dd HH:mm:ss} UTC. {error}";
        }
        finally { if (version == _contextVersion) { IsLoading = false; _read = null; } }
    }

    private void ClearSnapshot()
    {
        Snapshot = null;
        Selected = null;
        Files.Clear(); Branches.Clear(); Diff = "Select a change to review.";
        _observedAtUtc = null;
        IsStale = false;
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

    [RelayCommand]
    private Task CreateBranchAsync()
    {
        var branchName = NewBranchName.Trim();
        return CanCreateBranch
            ? MutateAsync("Create and switch branch", (root, ct) => _git.CreateBranchAsync(root, branchName, ct), createdBranch: branchName)
            : Task.CompletedTask;
    }

    [RelayCommand]
    private Task SwitchBranchAsync()
    {
        var branchName = SelectedBranch;
        return CanSwitchBranch && branchName is not null
            ? MutateAsync($"Switch to {branchName}", (root, ct) => _git.SwitchBranchAsync(root, branchName, ct))
            : Task.CompletedTask;
    }

    private async Task MutateAsync(string action, Func<string, CancellationToken, Task<bool>> execute,
        string? committedMessage = null, string? createdBranch = null)
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
            if (createdBranch is not null)
            {
                if (string.Equals(Root, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    && string.Equals(NewBranchName.Trim(), createdBranch, StringComparison.Ordinal)) NewBranchName = "";
                if (string.Equals(_branchDrafts.GetValueOrDefault(root)?.Trim(), createdBranch, StringComparison.Ordinal)) _branchDrafts.Remove(root);
            }
        }
        catch (OperationCanceledException) { LastOperation = $"{action} was interrupted in {root}. Refresh its Git state before retrying."; }
        catch (Exception ex) { LastOperation = $"{action} failed in {root}: {StudioOutputSanitizer.Sanitize(ex.Message)}"; }
        finally { IsMutating = false; if (!_disposed) await RefreshAsync(); }
    }

    public void Dispose() { _disposed = true; _lifetime.Cancel(); _read?.Cancel(); _lifetime.Dispose(); }
}
