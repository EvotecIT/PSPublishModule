namespace PSMaintenance;

/// <summary>
/// Specifies how to handle an existing destination folder during installation.
/// </summary>
public enum OnExistsOption
{
    /// <summary>
    /// Merge new files into the existing folder (default). Existing files are preserved unless <c>-Force</c> is used.
    /// </summary>
    Merge,
    /// <summary>
    /// Delete the destination folder and copy fresh files.
    /// </summary>
    Overwrite,
    /// <summary>
    /// Do nothing if the destination exists and return the path.
    /// </summary>
    Skip,
    /// <summary>
    /// Throw an error if destination exists.
    /// </summary>
    Stop
}
