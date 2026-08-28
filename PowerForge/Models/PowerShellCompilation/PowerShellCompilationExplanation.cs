namespace PowerForge;

/// <summary>Stable compiler decision assigned to one authored compilation unit.</summary>
public enum PowerShellCompilationDecisionKind
{
    /// <summary>The unit is eligible for typed CLR emission.</summary>
    Typed,
    /// <summary>The selected mode retains the unit on a PowerShell runtime path.</summary>
    RuntimeFallback,
    /// <summary>The selected mode rejects the unit.</summary>
    Rejected
}

/// <summary>Relocation-safe diagnostic in a compiler decision trace.</summary>
public sealed class PowerShellCompilationExplanationDiagnostic
{
    /// <summary>Stable diagnostic code.</summary>
    public PowerShellCompilationDiagnosticCode Code { get; set; }
    /// <summary>Stable feature identifier.</summary>
    public string FeatureId { get; set; } = string.Empty;
    /// <summary>Human-readable cause.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>One-based source line.</summary>
    public int Line { get; set; }
    /// <summary>One-based source column.</summary>
    public int Column { get; set; }
}

/// <summary>Deterministic decision trace for one script or function.</summary>
public sealed class PowerShellCompilationUnitExplanation
{
    /// <summary>Relocation-safe identity derived from relative path and authored declaration.</summary>
    public string UnitId { get; set; } = string.Empty;
    /// <summary>Function name or the synthetic script name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Authored unit kind.</summary>
    public PowerShellCompilationUnitKind Kind { get; set; }
    /// <summary>One-based declaration line.</summary>
    public int StartLine { get; set; }
    /// <summary>Final compiler decision for the selected mode.</summary>
    public PowerShellCompilationDecisionKind Decision { get; set; }
    /// <summary>Ordered causal diagnostics; empty for a typed unit.</summary>
    public PowerShellCompilationExplanationDiagnostic[] Causes { get; set; } = Array.Empty<PowerShellCompilationExplanationDiagnostic>();
}

/// <summary>Decision traces for one source file without machine-specific absolute paths.</summary>
public sealed class PowerShellCompilationFileExplanation
{
    /// <summary>Normalized path relative to the analyzed root.</summary>
    public string RelativePath { get; set; } = string.Empty;
    /// <summary>File-level parse, input, or packaging causes that apply outside a single unit.</summary>
    public PowerShellCompilationExplanationDiagnostic[] Causes { get; set; } = Array.Empty<PowerShellCompilationExplanationDiagnostic>();
    /// <summary>Deterministically ordered unit decisions.</summary>
    public PowerShellCompilationUnitExplanation[] Units { get; set; } = Array.Empty<PowerShellCompilationUnitExplanation>();
}

/// <summary>Missing dependency cause that prevents the selected plan from proceeding.</summary>
public sealed class PowerShellCompilationDependencyExplanation
{
    /// <summary>Dependency name without a machine-specific source path.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Normalized module-relative path or external identity.</summary>
    public string RelativePath { get; set; } = string.Empty;
    /// <summary>Dependency content kind.</summary>
    public PowerShellCompilationDependencyKind Kind { get; set; }
    /// <summary>How the dependency was discovered.</summary>
    public PowerShellCompilationDependencyDiscovery Discovery { get; set; }
    /// <summary>Redacted explanation of the missing requirement.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Human- and machine-readable explanation of a compilation plan.</summary>
public sealed class PowerShellCompilationExplanation
{
    /// <summary>Explanation schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Selected compilation mode.</summary>
    public PowerShellCompilationMode Mode { get; set; }
    /// <summary>Selected target framework, when supplied.</summary>
    public string TargetFramework { get; set; } = string.Empty;
    /// <summary>Whether the selected mode can proceed.</summary>
    public bool CanProceed { get; set; }
    /// <summary>Typed unit count.</summary>
    public int TypedUnits { get; set; }
    /// <summary>Runtime-fallback unit count.</summary>
    public int RuntimeFallbackUnits { get; set; }
    /// <summary>Rejected unit count.</summary>
    public int RejectedUnits { get; set; }
    /// <summary>Missing dependency causes that block the plan independently of unit eligibility.</summary>
    public PowerShellCompilationDependencyExplanation[] DependencyCauses { get; set; } = Array.Empty<PowerShellCompilationDependencyExplanation>();
    /// <summary>Relocation-safe per-file decision traces.</summary>
    public PowerShellCompilationFileExplanation[] Files { get; set; } = Array.Empty<PowerShellCompilationFileExplanation>();
}
