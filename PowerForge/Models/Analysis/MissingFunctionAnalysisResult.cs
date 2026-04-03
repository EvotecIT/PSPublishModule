namespace PowerForge;

/// <summary>
/// Represents the host-neutral output of missing-function analysis, including discovered referenced commands
/// and inlineable helper definitions.
/// </summary>
public sealed class MissingFunctionAnalysisResult
{
    /// <summary>Resolved command references (may include nested helper dependencies).</summary>
    public MissingCommandReference[] Summary { get; }

    /// <summary>Filtered resolved command references (kept for legacy parity).</summary>
    public MissingCommandReference[] SummaryFiltered { get; }

    /// <summary>
    /// Inlineable helper function definitions (top-level plus nested when recursive mode is enabled).
    /// </summary>
    public string[] Functions { get; }

    /// <summary>Inlineable helper function definitions for the top-level analysis only.</summary>
    public string[] FunctionsTopLevelOnly { get; }

    /// <summary>
    /// Creates a new <see cref="MissingFunctionAnalysisResult"/> instance.
    /// </summary>
    public MissingFunctionAnalysisResult(
        MissingCommandReference[] summary,
        MissingCommandReference[] summaryFiltered,
        string[] functions,
        string[] functionsTopLevelOnly)
    {
        Summary = summary ?? System.Array.Empty<MissingCommandReference>();
        SummaryFiltered = summaryFiltered ?? System.Array.Empty<MissingCommandReference>();
        Functions = functions ?? System.Array.Empty<string>();
        FunctionsTopLevelOnly = functionsTopLevelOnly ?? System.Array.Empty<string>();
    }
}
