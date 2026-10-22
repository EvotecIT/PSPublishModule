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
    /// Approved modules whose requirements are satisfied by complete analysis of the input.
    /// Includes unused donors and helpers already defined locally, while excluding unresolved
    /// dynamic or module-owned runtime references. Retained manifest content is checked separately.
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
