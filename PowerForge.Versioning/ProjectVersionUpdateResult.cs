namespace PowerForge;

/// <summary>
/// Represents the result of updating a single project file version.
/// </summary>
public sealed class ProjectVersionUpdateResult
{
    /// <summary>
    /// Gets the file path that was considered.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the kind of source file that was updated.
    /// </summary>
    public ProjectVersionSourceKind Kind { get; }

    /// <summary>
    /// Gets the detected version before the update when known.
    /// </summary>
    public string? OldVersion { get; }

    /// <summary>
    /// Gets the requested target version.
    /// </summary>
    public string NewVersion { get; }

    /// <summary>
    /// Gets the update status.
    /// </summary>
    public ProjectVersionUpdateStatus Status { get; }

    /// <summary>
    /// Gets the error message when the update failed.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Creates a new per-file project version update result.
    /// </summary>
    public ProjectVersionUpdateResult(string source, ProjectVersionSourceKind kind, string? oldVersion, string newVersion, ProjectVersionUpdateStatus status, string? error)
    {
        Source = source;
        Kind = kind;
        OldVersion = oldVersion;
        NewVersion = newVersion;
        Status = status;
        Error = error;
    }
}
