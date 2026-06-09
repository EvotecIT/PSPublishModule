namespace PowerForge;

internal sealed class ProjectBuildWorkflowResult
{
    public ProjectBuildResult Result { get; set; } = new();
    public ProjectBuildGitHubPublishSummary? GitHubPublishSummary { get; set; }
}
