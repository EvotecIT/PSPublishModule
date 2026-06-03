namespace PowerForge;

/// <summary>
/// Describes one file path checked during isolated module profile validation.
/// </summary>
public sealed class IsolatedModuleProfileValidationPath
{
    /// <summary>Validation category for the checked path.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Optional profile-relative path, when the path came from profile metadata.</summary>
    public string? RelativePath { get; set; }

    /// <summary>Absolute path that was checked.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Whether the checked file exists.</summary>
    public bool Exists { get; set; }
}
