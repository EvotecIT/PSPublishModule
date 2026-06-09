using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class HubDashboardViewModel : ViewModelBase
{
    public HubDashboardViewModel(HubDashboardSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public HubDashboardSnapshot Snapshot { get; }

    public int TotalProjects => Snapshot.TotalProjects;
    public int ManagedProjects => Snapshot.ManagedProjects;
    public int DirtyRepos => Snapshot.DirtyRepos;
    public int TotalOpenPrs => Snapshot.TotalOpenPrs;
    public int TotalOpenIssues => Snapshot.TotalOpenIssues;
    public int FailingBuilds => Snapshot.FailingBuilds;

    public IReadOnlyList<ProjectEntry> RecentlyActive => Snapshot.RecentlyActive;
    public IReadOnlyList<ProjectEntry> NeedingAttention => Snapshot.NeedingAttention;
    public IReadOnlyList<ProjectEntry> StaleProjects => Snapshot.StaleProjects;
}
