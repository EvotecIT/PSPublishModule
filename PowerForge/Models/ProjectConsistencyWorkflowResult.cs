namespace PowerForge;

/// <summary>
/// Combined workflow result for project consistency analysis and optional conversion.
/// </summary>
public sealed class ProjectConsistencyWorkflowResult
{
    /// <summary>
    /// Creates a new workflow result.
    /// </summary>
    public ProjectConsistencyWorkflowResult(
        string rootPath,
        string[] patterns,
        ProjectKind kind,
        ProjectConsistencyReport report,
        ProjectConversionResult? encodingConversion,
        ProjectConversionResult? lineEndingConversion)
    {
        RootPath = rootPath;
        Patterns = patterns ?? System.Array.Empty<string>();
        Kind = kind;
        Report = report;
        EncodingConversion = encodingConversion;
        LineEndingConversion = lineEndingConversion;
    }

    /// <summary>Normalized project root path.</summary>
    public string RootPath { get; }

    /// <summary>Resolved include patterns used by the enumeration.</summary>
    public string[] Patterns { get; }

    /// <summary>Resolved project kind.</summary>
    public ProjectKind Kind { get; }

    /// <summary>Final consistency report.</summary>
    public ProjectConsistencyReport Report { get; }

    /// <summary>Encoding conversion summary when conversion mode ran.</summary>
    public ProjectConversionResult? EncodingConversion { get; }

    /// <summary>Line ending conversion summary when conversion mode ran.</summary>
    public ProjectConversionResult? LineEndingConversion { get; }
}
