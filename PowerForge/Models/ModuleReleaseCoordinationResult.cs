namespace PowerForge;

/// <summary>
/// Result of coordinating module and package assets for a unified release.
/// </summary>
public sealed class ModuleReleaseCoordinationResult
{
    /// <summary>Configured or resolved release stage root. Empty when assets were not staged.</summary>
    public string StageRoot { get; set; } = string.Empty;

    /// <summary>Module artifact paths included in the unified release payload.</summary>
    public string[] ModuleAssetPaths { get; set; } = Array.Empty<string>();

    /// <summary>NuGet package paths included in the unified release payload.</summary>
    public string[] PackageAssetPaths { get; set; } = Array.Empty<string>();

    /// <summary>Final asset paths used for GitHub publishing or downstream upload.</summary>
    public string[] AssetPaths { get; set; } = Array.Empty<string>();

    /// <summary>GitHub release result when a unified GitHub publish was executed.</summary>
    public GitHubReleasePublishResult? GitHub { get; set; }
}
