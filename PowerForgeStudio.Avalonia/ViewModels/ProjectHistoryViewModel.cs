using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class ProjectHistoryViewModel : ObservableObject, IDisposable
{
    private const int HistoryLimit = 50;
    private readonly IProjectHistoryService _history;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _read;
    private CancellationTokenSource? _detail;
    private int _contextVersion;
    private int _detailVersion;
    private bool _disposed;

    public ProjectHistoryViewModel(IProjectHistoryService? history = null)
        => _history = history ?? new ProjectGitService();

    public ObservableCollection<GitLogEntry> Commits { get; } = [];
    public ObservableCollection<string> ChangedFiles { get; } = [];
    [ObservableProperty] private string _root = "";
    [ObservableProperty] private string _branch = "-";
    [ObservableProperty] private string _status = "Select a working copy.";
    [ObservableProperty] private string _detailStatus = "Select a commit to inspect it.";
    [ObservableProperty] private string _diff = "Select a commit to inspect its changed files and diff.";
    [ObservableProperty] private string _output = "Commit history has not been inspected.";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isDetailLoading;
    [ObservableProperty] private bool _filesTruncated;
    [ObservableProperty] private bool _diffTruncated;
    [ObservableProperty] private GitLogEntry? _selectedCommit;
    public bool HasWorkingCopy => !string.IsNullOrWhiteSpace(Root);

    partial void OnSelectedCommitChanged(GitLogEntry? value) => _ = LoadDetailAsync(value);

    public void SetWorkingCopy(string root)
    {
        root = string.IsNullOrWhiteSpace(root) ? "" : Path.GetFullPath(root);
        if (SamePathOrEmpty(root, Root)) return;
        ++_contextVersion;
        ++_detailVersion;
        _read?.Cancel();
        _detail?.Cancel();
        IsLoading = false;
        IsDetailLoading = false;
        Root = root;
        OnPropertyChanged(nameof(HasWorkingCopy));
        ClearHistory(root.Length == 0 ? "Select a working copy." : "Refresh to read commit history.");
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        var version = ++_contextVersion;
        _read?.Cancel();
        _detail?.Cancel();
        _read?.Dispose();
        _read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _read.Token;
        var root = Root;
        var selectedHash = SelectedCommit?.Hash;
        ClearHistory(root.Length == 0 ? "Select a working copy." : "Reading commit history…");
        if (root.Length == 0) { _read.Dispose(); _read = null; return; }

        IsLoading = true;
        try
        {
            var git = await _history.GetHistoryContextAsync(root, token);
            if (!IsCurrent(version, root)) return;
            Branch = git.BranchDisplay;
            if (!git.IsGitRepository)
            {
                Status = "This folder is not a Git working copy.";
                Output = "No Git history was read.";
                return;
            }

            var entries = await _history.GetHistoryLogAsync(root, HistoryLimit, token);
            if (!IsCurrent(version, root)) return;
            foreach (var entry in entries) Commits.Add(entry);
            Status = entries.Count == 0
                ? $"{Branch} · This Git working copy has no commits yet."
                : $"{Branch} · {entries.Count} most recent commit(s).";
            Output = entries.Count == 0
                ? "The working copy is available, but no commit history exists yet."
                : $"Observed {entries.Count} commit(s). Select one to inspect bounded changed-file and diff evidence.\nNo repository content was modified.";
            SelectedCommit = entries.FirstOrDefault(entry => entry.Hash == selectedHash) ?? entries.FirstOrDefault();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrent(version, root))
            {
                Status = "Commit history is unavailable.";
                Output = StudioOutputSanitizer.Sanitize(ex.Message);
            }
        }
        finally
        {
            if (version == _contextVersion)
            {
                IsLoading = false;
                _read?.Dispose();
                _read = null;
            }
        }
    }

    private async Task LoadDetailAsync(GitLogEntry? commit)
    {
        var version = ++_detailVersion;
        _detail?.Cancel();
        _detail?.Dispose();
        _detail = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _detail.Token;
        ChangedFiles.Clear();
        FilesTruncated = false;
        DiffTruncated = false;
        if (commit is null || string.IsNullOrWhiteSpace(Root))
        {
            IsDetailLoading = false;
            DetailStatus = "Select a commit to inspect it.";
            Diff = "Select a commit to inspect its changed files and diff.";
            _detail.Dispose();
            _detail = null;
            return;
        }

        var root = Root;
        IsDetailLoading = true;
        DetailStatus = $"Reading {commit.ShortHash}…";
        Diff = "Loading commit diff…";
        try
        {
            var detail = await _history.GetCommitDetailAsync(root, commit.Hash, token);
            if (!IsCurrentDetail(version, root, commit.Hash)) return;
            foreach (var path in detail.ChangedFiles) ChangedFiles.Add(path);
            FilesTruncated = detail.ChangedFilesTruncated;
            DiffTruncated = detail.DiffTruncated;
            Diff = detail.Diff;
            DetailStatus = detail.ChangedFiles.Count == 0
                ? $"{commit.ShortHash} has no changed paths to display."
                : $"{commit.ShortHash} · {detail.ChangedFiles.Count} changed path(s){(detail.ChangedFilesTruncated ? " shown (list truncated)" : "")}.";
            Output = $"Selected {commit.ShortHash} by {commit.AuthorName}.\nChanged files and diff are read-only and bounded; no checkout or repository mutation occurred.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrentDetail(version, root, commit.Hash))
            {
                DetailStatus = "Commit detail is unavailable.";
                Diff = StudioOutputSanitizer.Sanitize(ex.Message);
            }
        }
        finally
        {
            if (version == _detailVersion)
            {
                IsDetailLoading = false;
                _detail?.Dispose();
                _detail = null;
            }
        }
    }

    private void ClearHistory(string status)
    {
        SelectedCommit = null;
        Commits.Clear();
        ChangedFiles.Clear();
        Branch = "-";
        Status = status;
        DetailStatus = "Select a commit to inspect it.";
        Diff = "Select a commit to inspect its changed files and diff.";
        Output = Root.Length == 0 ? "Commit history has not been inspected." : "No repository content was modified.";
        FilesTruncated = false;
        DiffTruncated = false;
        IsDetailLoading = false;
    }

    private bool IsCurrent(int version, string root)
        => !_disposed && version == _contextVersion && SamePathOrEmpty(root, Root);

    private bool IsCurrentDetail(int version, string root, string hash)
        => !_disposed && version == _detailVersion && SamePathOrEmpty(root, Root) && SelectedCommit?.Hash == hash;

    private static bool SamePathOrEmpty(string left, string right)
        => left.Length == 0 || right.Length == 0
            ? left.Length == right.Length
            : string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_contextVersion;
        ++_detailVersion;
        _lifetime.Cancel();
        _read?.Cancel();
        _detail?.Cancel();
        _read?.Dispose();
        _detail?.Dispose();
        _lifetime.Dispose();
    }
}
