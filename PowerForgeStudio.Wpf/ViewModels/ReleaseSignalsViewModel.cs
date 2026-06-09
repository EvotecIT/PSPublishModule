using System.Collections.ObjectModel;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class ReleaseSignalsViewModel : ViewModelBase
{
    private string _releaseInboxHeadline = "Release inbox not assembled yet.";
    private string _releaseInboxDetails = "Refresh will rank actionable release work from queue state, GitHub attention, and release drift signals.";
    private string _gitHubInboxHeadline = "GitHub inbox not probed yet.";
    private string _gitHubInboxDetails = "Refresh will probe a limited set of repositories for open PRs, latest workflow status, and latest release tags.";
    private string _releaseDriftHeadline = "Release drift not assessed yet.";
    private string _releaseDriftDetails = "Refresh will compare local git state with lightweight GitHub release signals.";

    public ObservableCollection<RepositoryReleaseInboxItem> ReleaseInboxItems { get; } = [];

    public ObservableCollection<RepositoryPortfolioItem> GitHubInboxItems { get; } = [];

    public string ReleaseInboxHeadline
    {
        get => _releaseInboxHeadline;
        set => SetProperty(ref _releaseInboxHeadline, value);
    }

    public string ReleaseInboxDetails
    {
        get => _releaseInboxDetails;
        set => SetProperty(ref _releaseInboxDetails, value);
    }

    public string GitHubInboxHeadline
    {
        get => _gitHubInboxHeadline;
        set => SetProperty(ref _gitHubInboxHeadline, value);
    }

    public string GitHubInboxDetails
    {
        get => _gitHubInboxDetails;
        set => SetProperty(ref _gitHubInboxDetails, value);
    }

    public string ReleaseDriftHeadline
    {
        get => _releaseDriftHeadline;
        set => SetProperty(ref _releaseDriftHeadline, value);
    }

    public string ReleaseDriftDetails
    {
        get => _releaseDriftDetails;
        set => SetProperty(ref _releaseDriftDetails, value);
    }
}
