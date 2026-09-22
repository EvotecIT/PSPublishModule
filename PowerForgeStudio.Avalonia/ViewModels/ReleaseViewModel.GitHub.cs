using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class ReleaseViewModel
{
    private readonly IGitHubReleaseCatalogService _releaseGitHubService = githubReleases ?? new GitHubProjectService();
    private CancellationTokenSource? _githubRefresh;
    private int _githubVersion;
    private DateTimeOffset? _githubObservedAtUtc;
    public ObservableCollection<GitHubReleaseMetric> GitHubReleases { get; } = [];
    [ObservableProperty] private GitHubReleaseMetric? _selectedGitHubRelease;
    [ObservableProperty] private bool _isLoadingGitHubReleases;
    [ObservableProperty] private string _githubReleaseStatus = "Choose a working copy to inspect its published GitHub releases.";
    public bool HasGitHubReleases => GitHubReleases.Count > 0;
    public bool HasSelectedGitHubRelease => SelectedGitHubRelease is not null;
    public bool CanRefreshGitHubReleases => !_disposed && !IsLoadingGitHubReleases && HistoryScopeRoot.Length > 0;
    public bool CanOpenGitHubRelease => SelectedGitHubRelease?.HtmlUrl is { Length: > 0 };

    partial void OnIsLoadingGitHubReleasesChanged(bool value) => OnPropertyChanged(nameof(CanRefreshGitHubReleases));
    partial void OnSelectedGitHubReleaseChanged(GitHubReleaseMetric? value)
    {
        OnPropertyChanged(nameof(HasSelectedGitHubRelease));
        OnPropertyChanged(nameof(CanOpenGitHubRelease));
        OpenGitHubReleaseCommand.NotifyCanExecuteChanged();
    }

    private void ResetGitHubReleaseCatalog(string root)
    {
        ++_githubVersion;
        _githubRefresh?.Cancel();
        _githubRefresh?.Dispose();
        _githubRefresh = null;
        GitHubReleases.Clear();
        SelectedGitHubRelease = null;
        _githubObservedAtUtc = null;
        IsLoadingGitHubReleases = false;
        GithubReleaseStatus = root.Length == 0
            ? "Choose a working copy to inspect its published GitHub releases."
            : "Refresh GitHub releases to read recent asset download counts for this project.";
        OnPropertyChanged(nameof(HasGitHubReleases));
        OnPropertyChanged(nameof(CanRefreshGitHubReleases));
    }

    [RelayCommand]
    public async Task RefreshGitHubReleasesAsync()
    {
        if (!CanRefreshGitHubReleases) return;
        var version = ++_githubVersion;
        var root = HistoryScopeRoot;
        _githubRefresh?.Dispose();
        using var read = new CancellationTokenSource();
        _githubRefresh = read;
        IsLoadingGitHubReleases = true;
        GithubReleaseStatus = "Reading recent GitHub releases and asset downloads…";
        try
        {
            var slug = await _releaseGitHubService.ResolveRepositoryAsync(root, read.Token);
            if (_disposed || version != _githubVersion) return;
            if (slug is null)
            {
                GithubReleaseStatus = "This working copy has no supported GitHub origin." + StaleCatalogSuffix();
                return;
            }
            var page = await _releaseGitHubService.FetchRecentReleasesAsync(slug, read.Token);
            if (_disposed || version != _githubVersion) return;
            _githubObservedAtUtc = DateTimeOffset.UtcNow;
            var selectedId = SelectedGitHubRelease?.Id;
            GitHubReleases.Clear();
            foreach (var release in page.Items) GitHubReleases.Add(release);
            SelectedGitHubRelease = GitHubReleases.FirstOrDefault(release => release.Id == selectedId)
                ?? GitHubReleases.FirstOrDefault();
            OnPropertyChanged(nameof(HasGitHubReleases));
            GithubReleaseStatus = page.Items.Count == 0
                ? $"No GitHub releases were returned for {slug}. Read {_githubObservedAtUtc:yyyy-MM-dd HH:mm} UTC."
                : $"Showing {page.Items.Count} recent release(s) for {slug}" +
                  (page.HasMore ? "; older releases are not included" : "") +
                  $". Read {_githubObservedAtUtc:yyyy-MM-dd HH:mm} UTC. Counts cover release assets only.";
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && version == _githubVersion)
                GithubReleaseStatus = "GitHub release read cancelled." + StaleCatalogSuffix();
        }
        catch (Exception ex)
        {
            if (!_disposed && version == _githubVersion)
                GithubReleaseStatus = "GitHub release data unavailable: " + StudioDisplayError.From(ex) + StaleCatalogSuffix();
        }
        finally
        {
            if (ReferenceEquals(_githubRefresh, read)) _githubRefresh = null;
            if (!_disposed && version == _githubVersion) IsLoadingGitHubReleases = false;
        }
    }

    private string StaleCatalogSuffix() => _githubObservedAtUtc is { } observed
        ? $" Counts from {observed:yyyy-MM-dd HH:mm} UTC remain visible and may be stale."
        : " No release snapshot is available.";

    [RelayCommand(CanExecute = nameof(CanOpenGitHubRelease))]
    private void OpenGitHubRelease()
    {
        var target = SelectedGitHubRelease?.HtmlUrl;
        if (target is null || !Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Contains("/releases/tag/", StringComparison.Ordinal)) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { GithubReleaseStatus = "Could not open the release: " + StudioDisplayError.From(ex); }
    }
}
