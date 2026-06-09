namespace PowerForgeStudio.Domain.Hub;

public sealed record HubDashboardSnapshot(
    int TotalProjects,
    int ManagedProjects,
    int DirtyRepos,
    int TotalOpenPrs,
    int TotalOpenIssues,
    int FailingBuilds,
    int ProjectsAheadOfUpstream,
    IReadOnlyList<ProjectEntry> RecentlyActive,
    IReadOnlyList<ProjectEntry> NeedingAttention,
    IReadOnlyList<ProjectEntry> StaleProjects)
{
    public static HubDashboardSnapshot Empty { get; } = new(
        TotalProjects: 0,
        ManagedProjects: 0,
        DirtyRepos: 0,
        TotalOpenPrs: 0,
        TotalOpenIssues: 0,
        FailingBuilds: 0,
        ProjectsAheadOfUpstream: 0,
        RecentlyActive: [],
        NeedingAttention: [],
        StaleProjects: []);
}
