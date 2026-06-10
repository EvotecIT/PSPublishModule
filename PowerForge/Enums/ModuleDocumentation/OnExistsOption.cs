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
    Merge,
    /// <summary>
    /// Delete the destination folder before copying a fresh documentation set. Use <c>-Force</c> when read-only
    /// files may need to be cleared before the delete.
    /// </summary>
    Overwrite,
    /// <summary>
    /// Do nothing when the destination exists and return the resolved destination path.
    /// </summary>
    Skip,
    /// <summary>
    /// Throw an error when the destination exists.
    /// </summary>
    Stop
}
