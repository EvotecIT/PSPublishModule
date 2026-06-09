namespace PowerForge;

/// <summary>
/// Result returned for a single release-asset project entry.
/// </summary>
internal sealed class GitHubReleaseAssetWorkflowResult
{
    public bool Success { get; set; }
    public string? TagName { get; set; }
    public string? ReleaseName { get; set; }
    public string? ZipPath { get; set; }
    public string? ReleaseUrl { get; set; }
    public string? ErrorMessage { get; set; }
}
