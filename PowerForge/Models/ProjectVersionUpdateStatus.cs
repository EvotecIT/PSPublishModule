namespace PowerForge;

/// <summary>
/// Describes the outcome of a per-file project version update attempt.
/// </summary>
public enum ProjectVersionUpdateStatus
{
    /// <summary>
    /// The file was updated on disk.
    /// </summary>
    Updated,

    /// <summary>
    /// No content change was required.
    /// </summary>
    NoChange,

    /// <summary>
    /// The update was skipped, for example due to <c>ShouldProcess</c>.
    /// </summary>
    Skipped,

    /// <summary>
    /// The update failed.
    /// </summary>
    Error,
}
