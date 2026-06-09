namespace PowerForge;

/// <summary>
/// Describes the result of updating Xcode project version values.
/// </summary>
public sealed class XcodeProjectVersionUpdateResult
{
    /// <summary>
    /// Gets or sets the resolved <c>project.pbxproj</c> path.
    /// </summary>
    public string ProjectFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the project version values before the update.
    /// </summary>
    public XcodeProjectVersionInfo Before { get; set; } = new();

    /// <summary>
    /// Gets or sets the project version values after the update.
    /// </summary>
    public XcodeProjectVersionInfo After { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the file content changed.
    /// </summary>
    public bool Changed { get; set; }

    /// <summary>
    /// Gets or sets whether the update was planned without writing the file.
    /// </summary>
    public bool WhatIf { get; set; }
}
