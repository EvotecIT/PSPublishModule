namespace PowerForge;

/// <summary>
/// Specifies how to handle an existing destination folder during installation.
/// </summary>
public enum OnExistsOption
{
    /// <summary>
    /// Merge documentation into the existing folder. This is the default update mode: new files are added,
    /// unmentioned local files remain in place, and existing files are preserved unless <c>-Force</c> is used.
    /// </summary>
    Merge = 0,
    /// <summary>
    /// Refresh package-owned files without deleting local files that are not present in the package. Existing
    /// package files are overwritten, while unmentioned local files remain in place.
    /// </summary>
    Refresh = 4,
    /// <summary>
    /// Delete the destination folder before copying a fresh documentation set. Use <c>-Force</c> when read-only
    /// files may need to be cleared before the delete.
    /// </summary>
    Overwrite = 1,
    /// <summary>
    /// Do nothing when the destination exists and return the resolved destination path.
    /// </summary>
    Skip = 2,
    /// <summary>
    /// Throw an error when the destination exists.
    /// </summary>
    Stop = 3
}
