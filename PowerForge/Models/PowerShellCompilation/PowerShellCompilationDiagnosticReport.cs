namespace PowerForge;

/// <summary>The owner stage that produced a project diagnostic.</summary>
public enum PowerShellCompilationDiagnosticStage
{
    /// <summary>Project/source input validation or explanation evidence file access.</summary>
    Input,
    /// <summary>Selected artifact or semantic target validation.</summary>
    Target,
    /// <summary>Provider or dependency resolution.</summary>
    Dependency,
    /// <summary>Parsing, binding, semantic analysis, or lowering.</summary>
    Semantic,
    /// <summary>Final artifact shaping after semantic analysis.</summary>
    Shaping,
    /// <summary>Existing build receipt, lock, or artifact verification.</summary>
    Integrity
}

/// <summary>A portable local-call edge projected from the compiler's bound call graph.</summary>
public sealed class PowerShellCompilationLocalCall
{
    /// <summary>Stable identity of the called authored unit.</summary>
    public string CalleeUnitId { get; set; } = string.Empty;
    /// <summary>Authored callee name.</summary>
    public string CalleeName { get; set; } = string.Empty;
    /// <summary>Portable callee source path.</summary>
    public string CalleeRelativePath { get; set; } = string.Empty;
    /// <summary>Callee declaration line.</summary>
    public int CalleeStartLine { get; set; }
    /// <summary>Authored call line, or zero when synthetic analysis has no exact mapping.</summary>
    public int Line { get; set; }
    /// <summary>Authored call column, or zero when unavailable.</summary>
    public int Column { get; set; }
}

/// <summary>A stage-specific issue without a copy of authored source.</summary>
public sealed class PowerShellCompilationReportIssue
{
    /// <summary>Stage that supplied this issue, not inferred from its message.</summary>
    public PowerShellCompilationDiagnosticStage Stage { get; set; }
    /// <summary>Stable compiler feature or workflow failure code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Portable source path, or empty for a project-wide issue.</summary>
    public string RelativePath { get; set; } = string.Empty;
    /// <summary>One-based source line, or zero when unavailable.</summary>
    public int Line { get; set; }
    /// <summary>One-based source column, or zero when unavailable.</summary>
    public int Column { get; set; }
    /// <summary>Explanation supplied by the owning compiler or workflow stage.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Final decisions and local-call dependencies for one complete authored unit.</summary>
public sealed class PowerShellCompilationDiagnosticGroup
{
    /// <summary>Portable authored source path.</summary>
    public string RelativePath { get; set; } = string.Empty;
    /// <summary>Existing final decision evidence; this report does not decide eligibility.</summary>
    public PowerShellCompilationUnitExplanation Unit { get; set; } = new();
    /// <summary>Semantic and shaping causes attributed to their original stage.</summary>
    public PowerShellCompilationReportIssue[] Issues { get; set; } = Array.Empty<PowerShellCompilationReportIssue>();
    /// <summary>Direct bound local calls. Follow callee unit identities for transitive causes; cycles are retained.</summary>
    public PowerShellCompilationLocalCall[] LocalCalls { get; set; } = Array.Empty<PowerShellCompilationLocalCall>();
}

/// <summary>Grouped project diagnostics projected from canonical compiler decisions.</summary>
public sealed class PowerShellCompilationDiagnosticReport
{
    /// <summary>Grouped report schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Whether source analysis and final shaping permit the selected target; does not verify an existing artifact.</summary>
    public bool CanProceed { get; set; }
    /// <summary>Whether unit routes come from final shaping; false when only analysis evidence was available.</summary>
    public bool FinalShapeAvailable { get; set; }
    /// <summary>Project, file, dependency, or integrity issues outside individual units.</summary>
    public PowerShellCompilationReportIssue[] Issues { get; set; } = Array.Empty<PowerShellCompilationReportIssue>();
    /// <summary>Deterministically ordered complete-unit groups.</summary>
    public PowerShellCompilationDiagnosticGroup[] Units { get; set; } = Array.Empty<PowerShellCompilationDiagnosticGroup>();
}
