namespace PowerForge;

/// <summary>
/// Carries a project-build version track into repository package-version alignment so
/// discovered package identifiers can be compared using the track's feed settings.
/// </summary>
internal sealed class ProjectBuildVersionAlignmentGroup
{
    /// <summary>Unique version-track name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>X-pattern shared by the projects in this track.</summary>
    public string ExpectedVersion { get; set; } = string.Empty;

    /// <summary>Project names participating in the track.</summary>
    public string[] Projects { get; set; } = System.Array.Empty<string>();

    /// <summary>Project whose package identity anchors the track.</summary>
    public string? AnchorProject { get; set; }

    /// <summary>Optional feed package identifier used for the anchor project.</summary>
    public string? AnchorPackageId { get; set; }

    /// <summary>NuGet sources used to resolve current versions for this track.</summary>
    public string[]? VersionSources { get; set; }

    /// <summary>Whether prerelease versions participate in current-version resolution.</summary>
    public bool IncludePrerelease { get; set; }
}
