namespace PowerForge;

/// <summary>
/// Represents a parsed summary of module test results (Pester object or NUnit XML),
/// focused on failures and basic totals.
/// </summary>
public sealed class ModuleTestFailureAnalysis
{
    /// <summary>Indicates the analysis input source (for example: <c>PesterResults</c> or a results file path).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Total number of discovered tests.</summary>
    public int TotalCount { get; set; }

    /// <summary>Number of passed tests.</summary>
    public int PassedCount { get; set; }

    /// <summary>Number of failed tests.</summary>
    public int FailedCount { get; set; }

    /// <summary>Number of skipped or inconclusive tests.</summary>
    public int SkippedCount { get; set; }

    /// <summary>List of failed tests.</summary>
    public ModuleTestFailureInfo[] FailedTests { get; set; } = System.Array.Empty<ModuleTestFailureInfo>();

    /// <summary>Timestamp when the analysis was produced.</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

