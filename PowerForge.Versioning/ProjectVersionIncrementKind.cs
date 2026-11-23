namespace PowerForge;

/// <summary>
/// Version component to increment when computing a new project version.
/// </summary>
public enum ProjectVersionIncrementKind
{
    /// <summary>
    /// Increment major and reset minor/build/revision.
    /// </summary>
    Major,

    /// <summary>
    /// Increment minor and reset build/revision.
    /// </summary>
    Minor,

    /// <summary>
    /// Increment build and reset revision.
    /// </summary>
    Build,

    /// <summary>
    /// Increment revision, adding it when the current version does not include one.
    /// </summary>
    Revision,
}
