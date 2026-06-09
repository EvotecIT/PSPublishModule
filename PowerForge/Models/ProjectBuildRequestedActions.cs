namespace PowerForge;

/// <summary>
/// Optional action overrides supplied by the caller for project builds.
/// </summary>
internal sealed class ProjectBuildRequestedActions
{
    public bool? PlanOnly { get; set; }
    public bool? UpdateVersions { get; set; }
    public bool? Build { get; set; }
    public bool? PublishNuget { get; set; }
    public bool? PublishGitHub { get; set; }
}
