using System.Collections.ObjectModel;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class RepositoryFamilyViewModel : ViewModelBase
{
    private string _headline = "Repository families will appear after the first scan.";
    private string _details = "Family grouping will cluster primary repos with their worktrees and review clones so the portfolio can narrow to one operational unit.";
    private string _actionHeadline = "Select a repository family to unlock family-level queue actions.";
    private string _laneHeadline = "Select a family or repository to open the family lane board.";
    private string _laneDetails = "The family lane will show which members are ready, waiting on USB, publish-ready, verify-ready, failed, or completed.";

    public ObservableCollection<RepositoryWorkspaceFamilySnapshot> Families { get; } = [];

    public ObservableCollection<RepositoryWorkspaceFamilyLaneItem> LaneItems { get; } = [];

    public string Headline
    {
        get => _headline;
        set => SetProperty(ref _headline, value);
    }

    public string Details
    {
        get => _details;
        set => SetProperty(ref _details, value);
    }

    public string ActionHeadline
    {
        get => _actionHeadline;
        set => SetProperty(ref _actionHeadline, value);
    }

    public string LaneHeadline
    {
        get => _laneHeadline;
        set => SetProperty(ref _laneHeadline, value);
    }

    public string LaneDetails
    {
        get => _laneDetails;
        set => SetProperty(ref _laneDetails, value);
    }
}
