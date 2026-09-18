namespace PowerForge;

/// <summary>
/// Represents the output of missing-function analysis in a host-neutral form.
/// </summary>
public sealed class MissingFunctionAnalysisResult
{
    /// <summary>Resolved command references (may include nested helper dependencies).</summary>
    public MissingCommandReference[] Summary { get; }

    /// <summary>Filtered resolved command references (kept for parity with existing analyzer output).</summary>
    public MissingCommandReference[] SummaryFiltered { get; }

    /// <summary>Inlineable helper function definitions (top-level plus nested when recursive mode is enabled).</summary>
    public string[] Functions { get; }

    /// <summary>Inlineable helper function definitions for the top-level analysis only.</summary>
    public string[] FunctionsTopLevelOnly { get; }

    /// <summary>
    /// Approved modules for which every referenced command was resolved to an inlineable PowerShell function.
    /// A module is listed only when at least one of its functions was referenced.
    /// </summary>
    public string[] FullyInlinedApprovedModules { get; }

    /// <summary>
    /// Creates a new <see cref="MissingFunctionAnalysisResult"/> instance.
    /// </summary>
    public MissingFunctionAnalysisResult(
        MissingCommandReference[] summary,
        MissingCommandReference[] summaryFiltered,
        string[] functions,
        string[] functionsTopLevelOnly,
        string[]? fullyInlinedApprovedModules = null)
    {
        Summary = summary ?? System.Array.Empty<MissingCommandReference>();
        SummaryFiltered = summaryFiltered ?? System.Array.Empty<MissingCommandReference>();
        Functions = functions ?? System.Array.Empty<string>();
        FunctionsTopLevelOnly = functionsTopLevelOnly ?? System.Array.Empty<string>();
        FullyInlinedApprovedModules = fullyInlinedApprovedModules ?? System.Array.Empty<string>();
    }
}
