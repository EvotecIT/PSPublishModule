namespace PowerForgeStudio.Domain.Projects;

public sealed record ProjectOverviewSnapshot(
    DateTimeOffset InspectedAtUtc,
    string ProjectName,
    string ProjectKind,
    string WorkspaceKind,
    string ProjectRoot,
    string WorkingCopyRoot,
    string Purpose,
    string? ReadmePath,
    string Branch,
    string GitState,
    string AheadBehind,
    int WorkingCopyCount,
    IReadOnlyList<ProjectOverviewItem> Products,
    IReadOnlyList<ProjectOverviewItem> EntryPoints,
    IReadOnlyList<ProjectOverviewItem> Prerequisites,
    IReadOnlyList<string> Warnings);

public sealed record ProjectOverviewItem(
    string Name,
    string Detail,
    string? SourcePath = null,
    string? DisplayPath = null,
    bool IsAvailable = true)
{
    public string SourceDisplay => DisplayPath ?? SourcePath ?? "";
}
