namespace PowerForge;

/// <summary>
/// Resolved project build preparation state used by the cmdlet workflow.
/// </summary>
internal sealed class ProjectBuildPreparedContext
{
    public bool PlanOnly { get; set; }
    public bool UpdateVersions { get; set; }
    public bool Build { get; set; }
    public bool PublishNuget { get; set; }
    public bool PublishGitHub { get; set; }
    public bool CreateReleaseZip { get; set; }
    public string RootPath { get; set; } = string.Empty;
    public string? StagingPath { get; set; }
    public string? OutputPath { get; set; }
    public string? ReleaseZipOutputPath { get; set; }
    public string? PlanOutputPath { get; set; }
    public string? PublishApiKey { get; set; }
    public string? GitHubToken { get; set; }
    public DotNetRepositoryReleaseSpec Spec { get; set; } = new();

    public bool HasWork => UpdateVersions || Build || PublishNuget || PublishGitHub;
}
