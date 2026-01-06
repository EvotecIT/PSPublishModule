namespace PowerForge;

/// <summary>
/// Summary of module validation checks.
/// </summary>
public sealed class ModuleValidationReport
{
    /// <summary>Overall validation status.</summary>
    public CheckStatus Status { get; }

    /// <summary>Validation check results.</summary>
    public ModuleValidationCheckResult[] Checks { get; }

    /// <summary>Total number of checks executed.</summary>
    public int TotalChecks { get; }

    /// <summary>Checks that produced warnings.</summary>
    public int WarningChecks { get; }

    /// <summary>Checks that produced failures.</summary>
    public int FailedChecks { get; }

    /// <summary>Short summary text.</summary>
    public string Summary { get; }

    /// <summary>Creates a report from the provided check results.</summary>
    /// <param name="checks">Check results to include in the report.</param>
    public ModuleValidationReport(ModuleValidationCheckResult[] checks)
    {
        Checks = checks ?? System.Array.Empty<ModuleValidationCheckResult>();
        TotalChecks = Checks.Length;
        WarningChecks = 0;
        FailedChecks = 0;

        foreach (var c in Checks)
        {
            if (c is null) continue;
            if (c.Status == CheckStatus.Fail) FailedChecks++;
            else if (c.Status == CheckStatus.Warning) WarningChecks++;
        }

        Status =
            FailedChecks > 0 ? CheckStatus.Fail :
            WarningChecks > 0 ? CheckStatus.Warning :
            CheckStatus.Pass;

        Summary = BuildSummary(TotalChecks, WarningChecks, FailedChecks);
    }

    private static string BuildSummary(int total, int warn, int fail)
    {
        if (total <= 0) return "no checks";
        if (fail == 0 && warn == 0) return $"{total} checks passed";
        var ok = total - warn - fail;
        return $"{ok} passed, {warn} warnings, {fail} failed";
    }
}

/// <summary>
/// Result of a single validation check.
/// </summary>
public sealed class ModuleValidationCheckResult
{
    /// <summary>Check name.</summary>
    public string Name { get; }

    /// <summary>Configured severity.</summary>
    public ValidationSeverity Severity { get; }

    /// <summary>Computed status.</summary>
    public CheckStatus Status { get; }

    /// <summary>Short summary for the check.</summary>
    public string Summary { get; }

    /// <summary>Issues found by the check.</summary>
    public string[] Issues { get; }

    /// <summary>Creates a single validation check result.</summary>
    /// <param name="name">Check name.</param>
    /// <param name="severity">Configured severity.</param>
    /// <param name="status">Computed status.</param>
    /// <param name="summary">Short summary.</param>
    /// <param name="issues">Issues discovered by the check.</param>
    public ModuleValidationCheckResult(
        string name,
        ValidationSeverity severity,
        CheckStatus status,
        string summary,
        string[] issues)
    {
        Name = name ?? string.Empty;
        Severity = severity;
        Status = status;
        Summary = summary ?? string.Empty;
        Issues = issues ?? System.Array.Empty<string>();
    }
}
