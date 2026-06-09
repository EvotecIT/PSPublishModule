using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Request describing a GitHub release asset workflow.
/// </summary>
internal sealed class GitHubReleaseAssetWorkflowRequest
{
    public IReadOnlyList<string> ProjectPaths { get; set; } = System.Array.Empty<string>();
    public string Owner { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public bool IsPreRelease { get; set; }
    public bool GenerateReleaseNotes { get; set; }
    public string? Version { get; set; }
    public string? TagName { get; set; }
    public string? TagTemplate { get; set; }
    public string? ReleaseName { get; set; }
    public bool IncludeProjectNameInTag { get; set; }
    public string? ZipPath { get; set; }
}
