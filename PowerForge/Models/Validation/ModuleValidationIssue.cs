namespace PowerForge;

/// <summary>
/// Structured issue produced by a module validation check.
/// </summary>
public sealed class ModuleValidationIssue
{
    /// <summary>Validation rule or issue code.</summary>
    public string Code { get; }

    /// <summary>Tool-specific severity, when available.</summary>
    public string Severity { get; }

    /// <summary>Human-readable issue message.</summary>
    public string Message { get; }

    /// <summary>Full file path associated with the issue.</summary>
    public string FilePath { get; }

    /// <summary>Path relative to the validated module root, when available.</summary>
    public string RelativePath { get; }

    /// <summary>One-based start line.</summary>
    public int Line { get; }

    /// <summary>One-based start column.</summary>
    public int Column { get; }

    /// <summary>One-based end line.</summary>
    public int EndLine { get; }

    /// <summary>One-based end column.</summary>
    public int EndColumn { get; }

    /// <summary>Recommended action for the module author.</summary>
    public string Recommendation { get; }

    /// <summary>Tool-supplied suggested correction, when available.</summary>
    public string SuggestedCorrection { get; }

    /// <summary>Tool or validation component that produced the issue.</summary>
    public string Source { get; }

    /// <summary>
    /// Creates a structured validation issue.
    /// </summary>
    public ModuleValidationIssue(
        string code,
        string severity,
        string message,
        string filePath = "",
        string relativePath = "",
        int line = 0,
        int column = 0,
        int endLine = 0,
        int endColumn = 0,
        string recommendation = "",
        string suggestedCorrection = "",
        string source = "")
    {
        Code = code ?? string.Empty;
        Severity = severity ?? string.Empty;
        Message = message ?? string.Empty;
        FilePath = filePath ?? string.Empty;
        RelativePath = relativePath ?? string.Empty;
        Line = line;
        Column = column;
        EndLine = endLine;
        EndColumn = endColumn;
        Recommendation = recommendation ?? string.Empty;
        SuggestedCorrection = suggestedCorrection ?? string.Empty;
        Source = source ?? string.Empty;
    }
}
