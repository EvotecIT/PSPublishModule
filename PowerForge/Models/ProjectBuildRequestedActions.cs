namespace PowerForge;

/// <summary>
/// Optional action overrides supplied by the caller for project builds.
/// </summary>
internal sealed class ProjectBuildRequestedActions
{
    public string? ReleaseVersionFloor { get; set; }
    public string? ReleaseVersionFloorProject { get; set; }
    public bool? PlanOnly { get; set; }
    public bool? UpdateVersions { get; set; }
    public bool? Build { get; set; }
    public bool? PublishNuget { get; set; }
    public bool? PublishGitHub { get; set; }
}
