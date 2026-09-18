namespace PowerForge;

/// <summary>
/// Represents the output of missing-function analysis, including the discovered referenced commands and any
/// inlineable helper definitions.
/// </summary>
public sealed class MissingFunctionsReport
{
    /// <summary>Resolved command references (may include nested helper dependencies).</summary>
    public MissingFunctionCommand[] Summary { get; }

    /// <summary>Filtered resolved command references (kept for legacy parity).</summary>
    public MissingFunctionCommand[] SummaryFiltered { get; }

    /// <summary>
    /// Inlineable helper function definitions (top-level plus nested when recursive mode is enabled).
    /// </summary>
    public string[] Functions { get; }

    /// <summary>Inlineable helper function definitions for the top-level analysis only.</summary>
    public string[] FunctionsTopLevelOnly { get; }

    /// <summary>Whether every command invocation was statically identifiable for closure analysis.</summary>
    public bool AnalysisComplete { get; }

    /// <summary>
    /// Approved modules that remain runtime dependencies because the analyzed source contains a
    /// module-qualified command, using-module declaration, or owned type reference.
    /// </summary>
    public string[] NonInlineableApprovedModules { get; }

    /// <summary>
    /// Creates a new <see cref="MissingFunctionsReport"/> instance.
    /// </summary>
    public MissingFunctionsReport(
        MissingFunctionCommand[] summary,
        MissingFunctionCommand[] summaryFiltered,
        string[] functions,
        string[] functionsTopLevelOnly,
        bool analysisComplete = true,
        string[]? nonInlineableApprovedModules = null)
    {
        Summary = summary ?? System.Array.Empty<MissingFunctionCommand>();
        SummaryFiltered = summaryFiltered ?? System.Array.Empty<MissingFunctionCommand>();
        Functions = functions ?? System.Array.Empty<string>();
        FunctionsTopLevelOnly = functionsTopLevelOnly ?? System.Array.Empty<string>();
        AnalysisComplete = analysisComplete;
        NonInlineableApprovedModules = nonInlineableApprovedModules ?? System.Array.Empty<string>();
    }
}
