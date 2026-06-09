namespace PowerForge;

/// <summary>
/// Describes one validation issue found while checking an isolated module profile.
/// </summary>
public sealed class IsolatedModuleProfileValidationIssue
{
    /// <summary>Issue severity. Current values are Error and Warning.</summary>
    public string Severity { get; set; } = "Error";

    /// <summary>Validation category that produced the issue.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Human-readable issue message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional absolute path related to the issue.</summary>
    public string? Path { get; set; }

    /// <summary>Optional profile-relative path related to the issue.</summary>
    public string? RelativePath { get; set; }
}
