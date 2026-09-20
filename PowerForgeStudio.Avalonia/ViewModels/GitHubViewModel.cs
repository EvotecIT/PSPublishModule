using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed record GitHubItemRow(int Number, string Title, string State, string? Author, bool IsPullRequest)
{
    public string Caption => $"#{Number}  {Title}";
    public string Metadata => $"{State} · {Author ?? "Unknown author"}";
}

/// <summary>Project-scoped remote reads; generation checks reject results from prior selections.</summary>
public sealed partial class GitHubViewModel : ObservableObject, IDisposable
{
    private readonly IGitHubProjectService _service;
    private readonly bool _ownsService;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _listing, _detail;
    private int _contextVersion, _detailVersion;
    private bool _disposed;

    public GitHubViewModel(IGitHubProjectService? service = null)
    {
        _service = service ?? new GitHubProjectService();
        _ownsService = service is null;
    }

    public ObservableCollection<GitHubItemRow> Items { get; } = [];
    public ObservableCollection<GitHubThreadEntry> Discussion { get; } = [];
    public ObservableCollection<GitHubCheck> Checks { get; } = [];
    public IReadOnlyList<string> States { get; } = ["open", "closed", "all"];
    [ObservableProperty] private string _root = "";
    [ObservableProperty] private string _slug = "";
    [ObservableProperty] private string _stateFilter = "open";
    [ObservableProperty] private bool _showPullRequests = true;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isDetailLoading;
    [ObservableProperty] private string _status = "Select a project, then refresh GitHub.";
    [ObservableProperty] private string _detailStatus = "Select a pull request or issue.";
    [ObservableProperty] private string _checksStatus = "Checks have not been loaded.";
    [ObservableProperty] private string _detailTitle = "Discussion";
    [ObservableProperty] private string _headSha = "";
    [ObservableProperty] private GitHubItemRow? _selected;
    public bool ShowIssues => !ShowPullRequests;
    public bool CanRefresh => !_disposed && !IsLoading && Root.Length > 0;
    public bool HasSelection => Selected is not null && Slug.Length > 0;
    public bool HasChecks => Selected?.IsPullRequest == true;
    public string SelectedUrl => HasSelection ? $"https://github.com/{Slug}/{(Selected!.IsPullRequest ? "pull" : "issues")}/{Selected.Number}" : "";
    public Task SelectionLoad { get; private set; } = Task.CompletedTask;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnShowPullRequestsChanged(bool value) { OnPropertyChanged(nameof(ShowIssues)); Invalidate(); }
    partial void OnStateFilterChanged(string value) => Invalidate();
    partial void OnSelectedChanged(GitHubItemRow? value)
    {
        OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(HasChecks)); OnPropertyChanged(nameof(SelectedUrl));
        SelectionLoad = LoadSelectionAsync();
    }

    public void SetWorkingCopy(string root)
    {
        if (string.Equals(Root, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
        Root = root;
        Invalidate();
    }

    private void Invalidate()
    {
        ++_contextVersion;
        _listing?.Cancel(); _detail?.Cancel();
        Slug = ""; Selected = null; Items.Clear(); ClearDetail();
        IsLoading = false;
        Status = "Refresh to load GitHub for this working copy and filter.";
        OnPropertyChanged(nameof(CanRefresh)); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(SelectedUrl));
    }

    [RelayCommand] private void ShowPulls() => ShowPullRequests = true;
    [RelayCommand] private void ShowIssueList() => ShowPullRequests = false;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!CanRefresh) return;
        Invalidate();
        var version = _contextVersion;
        var root = Root; var state = StateFilter; var pulls = ShowPullRequests;
        using var read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _listing = read;
        IsLoading = true; Status = "Resolving GitHub origin and loading…";
        try
        {
            var slug = await _service.ResolveRepositoryAsync(root, read.Token);
            if (!Current(version)) return;
            if (slug is null) { Status = "No supported github.com origin found for this working copy."; return; }
            Slug = slug;
            GitHubItemRow[] rows;
            bool more;
            if (pulls)
            {
                var result = await _service.FetchPullRequestsAsync(slug, state, read.Token);
                more = result.HasMore;
                rows = result.Select(item => new GitHubItemRow(item.Number, item.Title, item.StateDisplay, item.AuthorLogin, true)).ToArray();
            }
            else
            {
                var result = await _service.FetchIssuesAsync(slug, state, read.Token);
                more = result.HasMore;
                rows = result.Select(item => new GitHubItemRow(item.Number, item.Title, item.StateDisplay, item.AuthorLogin, false)).ToArray();
            }
            if (!Current(version)) return;
            foreach (var row in rows) Items.Add(row);
            Status = $"{rows.Length} {(pulls ? "pull requests" : "issues")} loaded · {DateTime.Now:t}" +
                (more ? " · Partial listing: more items exist on GitHub." : "");
        }
        catch (OperationCanceledException) { if (Current(version)) Status = "GitHub request cancelled or timed out. Refresh to retry."; }
        catch (Exception ex) { if (Current(version)) Status = SafeError(ex); }
        finally { if (ReferenceEquals(_listing, read)) _listing = null; if (Current(version)) IsLoading = false; }
    }

    private void ClearDetail()
    {
        ClearFiles();
        ++_detailVersion;
        Discussion.Clear(); Checks.Clear(); HeadSha = "";
        DetailTitle = "Discussion"; DetailStatus = "Select a pull request or issue.";
        ChecksStatus = "Checks have not been loaded."; IsDetailLoading = false;
    }

    [RelayCommand]
    public async Task LoadSelectionAsync()
    {
        _detail?.Cancel(); ClearDetail();
        if (_disposed || Selected is not { } selected || Slug.Length == 0) return;
        var context = _contextVersion; var version = _detailVersion; var slug = Slug;
        using var read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _detail = read;
        bool CurrentDetail() => Current(context) && version == _detailVersion;
        IsDetailLoading = true; DetailTitle = selected.Caption; DetailStatus = "Loading discussion…";
        try
        {
            IReadOnlyList<GitHubThreadEntry> entries;
            bool more;
            if (selected.IsPullRequest)
            {
                var result = await _service.FetchPullRequestDetailAsync(slug, selected.Number, read.Token);
                if (!CurrentDetail()) return;
                if (result is null) { DetailStatus = "Pull request is unavailable."; return; }
                entries = GitHubThreadEntryBuilder.BuildPullRequestEntries(result.PullRequest, result);
                HeadSha = result.PullRequest.HeadSha ?? "";
                more = result.HasMoreDiscussion;
            }
            else
            {
                var result = await _service.FetchIssueDetailAsync(slug, selected.Number, read.Token);
                if (!CurrentDetail()) return;
                if (result is null) { DetailStatus = "Issue is unavailable."; return; }
                entries = GitHubThreadEntryBuilder.BuildIssueEntries(result.Issue, result);
                more = result.HasMoreDiscussion;
            }
            foreach (var entry in entries) Discussion.Add(entry);
            DetailStatus = more ? "Partial discussion: additional entries exist on GitHub." : "Discussion loaded. Markdown shown as text.";
            if (selected.IsPullRequest && HeadSha.Length > 0)
            {
                ChecksStatus = "Loading checks for the displayed PR head…";
                try
                {
                    var checks = await _service.FetchChecksAsync(slug, HeadSha, read.Token);
                    if (!CurrentDetail()) return;
                    foreach (var check in checks) Checks.Add(check);
                    ChecksStatus = $"{checks.Count} checks/statuses observed; {checks.Count(item => item.IsFailure)} need attention." +
                        (checks.HasMore ? " Partial listing." : "") + " Merge requirements have not been evaluated.";
                }
                catch (OperationCanceledException) { if (CurrentDetail()) ChecksStatus = "Checks request cancelled or timed out. Reload to retry."; }
                catch (Exception ex) { if (CurrentDetail()) ChecksStatus = SafeError(ex); }
            }
            else if (selected.IsPullRequest) ChecksStatus = "GitHub did not return a PR head SHA; checks unavailable.";
        }
        catch (OperationCanceledException) { if (CurrentDetail()) DetailStatus = "Discussion request cancelled or timed out. Reload to retry."; }
        catch (Exception ex) { if (CurrentDetail()) DetailStatus = SafeError(ex); }
        finally { if (ReferenceEquals(_detail, read)) _detail = null; if (CurrentDetail()) IsDetailLoading = false; }
    }

    private bool Current(int version) => !_disposed && version == _contextVersion;
    private static string SafeError(Exception ex) => ex is GitHubAccessException ? ex.Message :
        ex is InvalidDataException or System.Text.Json.JsonException ? "GitHub returned an invalid or oversized response. Reload to retry." :
        "Unable to load GitHub data. Check connectivity and authentication, then retry.";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_contextVersion; ++_detailVersion; _lifetime.Cancel();
        if (_ownsService && _service is IDisposable disposable) disposable.Dispose();
    }
}
