namespace PowerForge;

/// <summary>
/// Describes version values discovered in an Xcode project file.
/// </summary>
public sealed class XcodeProjectVersionInfo
{
    /// <summary>
    /// Gets or sets the resolved <c>project.pbxproj</c> path.
    /// </summary>
    public string ProjectFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets distinct <c>MARKETING_VERSION</c> values discovered in the project.
    /// </summary>
    public string[] MarketingVersions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets distinct <c>CURRENT_PROJECT_VERSION</c> values discovered in the project.
    /// </summary>
    public string[] BuildNumbers { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets the single marketing version when all project entries agree.
    /// </summary>
    public string? MarketingVersion => MarketingVersions.Length == 1 ? MarketingVersions[0] : null;

    /// <summary>
    /// Gets the single build number when all project entries agree.
    /// </summary>
    public string? BuildNumber => BuildNumbers.Length == 1 ? BuildNumbers[0] : null;

    /// <summary>
    /// Gets whether both marketing and build versions are present and internally consistent.
    /// </summary>
    public bool IsConsistent => MarketingVersions.Length == 1 && BuildNumbers.Length == 1;
}
