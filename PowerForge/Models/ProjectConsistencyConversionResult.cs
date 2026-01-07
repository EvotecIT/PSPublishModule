namespace PowerForge;

/// <summary>
/// Combined result from Convert-ProjectConsistency including the final report and conversion summaries.
/// </summary>
public sealed class ProjectConsistencyConversionResult
{
    /// <summary>Post-conversion consistency report.</summary>
    public ProjectConsistencyReport Report { get; }

    /// <summary>Encoding conversion summary, when encoding conversion ran.</summary>
    public ProjectConversionResult? Encoding { get; }

    /// <summary>Line ending conversion summary, when line ending conversion ran.</summary>
    public ProjectConversionResult? LineEndings { get; }

    /// <summary>
    /// Creates a new conversion result.
    /// </summary>
    public ProjectConsistencyConversionResult(ProjectConsistencyReport report, ProjectConversionResult? encoding, ProjectConversionResult? lineEndings)
    {
        Report = report;
        Encoding = encoding;
        LineEndings = lineEndings;
    }
}
