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

/// <summary>Relocation-safe parameter type information captured in a compiler decision trace.</summary>
public sealed class PowerShellCompilationExplanationParameter
{
    /// <summary>Authored parameter name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Resolved CLR type name.</summary>
    public string TypeName { get; set; } = string.Empty;
    /// <summary>Whether the authored contract permits null.</summary>
    public bool AllowNull { get; set; }
    /// <summary>Whether a default value is present.</summary>
    public bool HasDefaultValue { get; set; }
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
    /// <summary>Resolved CLR return type for the analyzed unit.</summary>
    public string ReturnType { get; set; } = string.Empty;
    /// <summary>Resolved parameter types and nullability in authored order.</summary>
    public PowerShellCompilationExplanationParameter[] Parameters { get; set; } = Array.Empty<PowerShellCompilationExplanationParameter>();
    /// <summary>Final compiler decision for the selected mode.</summary>
    public PowerShellCompilationDecisionKind Decision { get; set; }
    /// <summary>Stable lowering route selected for the final artifact.</summary>
    public string LoweringRoute { get; set; } = string.Empty;
    /// <summary>Final artifact disposition selected for the unit.</summary>
    public string ArtifactDisposition { get; set; } = string.Empty;
    /// <summary>Whether semantic analysis accepted the unit before artifact shaping.</summary>
    public bool SemanticEligible { get; set; }
    /// <summary>Whether a CLR implementation is present in the delivered artifact.</summary>
    public bool Emitted { get; set; }
    /// <summary>Whether authored source remains in the delivered hosted payload.</summary>
    public bool RetainedHostedSource { get; set; }
    /// <summary>Number of hosted command regions in the emitted implementation.</summary>
    public int RuntimeCommandRegions { get; set; }
    /// <summary>Number of reads from emitted CLR into retained parent Hybrid script-module state.</summary>
    public int ModuleStateReadBoundaryCrossings { get; set; }
    /// <summary>Number of writes from emitted CLR into retained parent Hybrid script-module state.</summary>
    public int ModuleStateWriteBoundaryCrossings { get; set; }
    /// <summary>Number of typed/hosted boundary crossings.</summary>
    public int BoundaryCrossings { get; set; }
    /// <summary>Canonical coarse lowered-region evidence for an emitted CLR method.</summary>
    public PowerShellCompilationRegionGraph? RegionGraph { get; set; }
    /// <summary>Whether artifact shaping retained a runtime path for an eligible unit.</summary>
    public bool ShapingFallback { get; set; }
    /// <summary>Whether the artifact intentionally omits this unit.</summary>
    public bool Omitted { get; set; }
    /// <summary>Whether the artifact rejects this unit.</summary>
    public bool Rejected { get; set; }
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

/// <summary>Relocation-safe dependency decision included in a compiler trace.</summary>
public sealed class PowerShellCompilationDependencyTrace
{
    /// <summary>Dependency identity without a machine-specific source path.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Normalized module-relative path or external identity.</summary>
    public string RelativePath { get; set; } = string.Empty;
    /// <summary>Dependency content kind.</summary>
    public PowerShellCompilationDependencyKind Kind { get; set; }
    /// <summary>How the dependency was discovered.</summary>
    public PowerShellCompilationDependencyDiscovery Discovery { get; set; }
    /// <summary>Final dependency disposition consumed by artifact planning.</summary>
    public PowerShellCompilationDependencyDisposition Disposition { get; set; }
}

/// <summary>One authored input identity retained without copying source content or absolute paths.</summary>
public sealed class PowerShellCompilationReproductionSource
{
    /// <summary>Relocation-safe source path.</summary>
    public string RelativePath { get; set; } = string.Empty;
    /// <summary>SHA-256 of the exact authored source bytes.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>Bounded, redacted evidence needed to reproduce a compiler decision.</summary>
public sealed class PowerShellCompilationReproductionEvidence
{
    /// <summary>Reproduction evidence schema version.</summary>
    public int SchemaVersion { get; set; } = 4;
    /// <summary>Selected compilation mode.</summary>
    public PowerShellCompilationMode Mode { get; set; }
    /// <summary>Selected artifact kind.</summary>
    public PowerShellCompilationArtifactKind Kind { get; set; }
    /// <summary>Exact authored inputs represented only by relative path and content hash.</summary>
    public PowerShellCompilationReproductionSource[] Sources { get; set; } = Array.Empty<PowerShellCompilationReproductionSource>();
    /// <summary>Compiler version that produced the decision.</summary>
    public string CompilerVersion { get; set; } = string.Empty;
    /// <summary>Exact compiler assembly identity.</summary>
    public string CompilerSha256 { get; set; } = string.Empty;
    /// <summary>Runtime-free semantic profile name, when applicable.</summary>
    public string SemanticProfileName { get; set; } = string.Empty;
    /// <summary>Runtime-free semantic profile version, when applicable.</summary>
    public string SemanticProfileVersion { get; set; } = string.Empty;
    /// <summary>Exact target contract identity.</summary>
    public string TargetContractSha256 { get; set; } = string.Empty;
    /// <summary>Exact provider-contract set identity.</summary>
    public string ProviderContractsSha256 { get; set; } = string.Empty;
    /// <summary>Exact external provider-package lock identity.</summary>
    public string ProviderLockSha256 { get; set; } = string.Empty;
    /// <summary>Exact reviewed dependency-lock identity.</summary>
    public string DependencyLockSha256 { get; set; } = string.Empty;
    /// <summary>Exact generated-source identity.</summary>
    public string GeneratedSourceSha256 { get; set; } = string.Empty;
    /// <summary>Exact public ABI identity.</summary>
    public string PublicAbiSha256 { get; set; } = string.Empty;
    /// <summary>Exact generated source-map identity.</summary>
    public string SourceMapSha256 { get; set; } = string.Empty;
    /// <summary>Exact deterministic decision-trace identity.</summary>
    public string DecisionTraceSha256 { get; set; } = string.Empty;
    /// <summary>Exact immutable final unit-disposition ledger identity.</summary>
    public string UnitDispositionLedgerSha256 { get; set; } = string.Empty;
    /// <summary>Exact ordered diagnostic identity.</summary>
    public string DiagnosticsSha256 { get; set; } = string.Empty;
    /// <summary>Exact optional bound/lowered IR snapshot identity.</summary>
    public string IrSnapshotsSha256 { get; set; } = string.Empty;
    /// <summary>Exact portable failure-map identity.</summary>
    public string FailureMapSha256 { get; set; } = string.Empty;
    /// <summary>Exact diagnostic audit-trail identity.</summary>
    public string DiagnosticAuditSha256 { get; set; } = string.Empty;
    /// <summary>Exact retention/redaction policy identity.</summary>
    public string DiagnosticsPolicySha256 { get; set; } = string.Empty;
    /// <summary>Selected SDK version.</summary>
    public string DotNetSdkVersion { get; set; } = string.Empty;
    /// <summary>Exact selected SDK identity.</summary>
    public string DotNetSdkSha256 { get; set; } = string.Empty;
    /// <summary>Canonical identity over every field in this evidence record except itself.</summary>
    public string EvidenceSha256 { get; set; } = string.Empty;
}

/// <summary>Human- and machine-readable explanation of a compilation plan.</summary>
public sealed class PowerShellCompilationExplanation
{
    /// <summary>Explanation schema version.</summary>
    public int SchemaVersion { get; set; } = 4;
    /// <summary>Compatibility contract used by semantic fingerprints across supported hosts.</summary>
    public int SemanticCompatibilityVersion { get; set; } = 3;
    /// <summary>SHA-256 over semantic decisions with authored coordinates and traversal order removed.</summary>
    public string SemanticFingerprintSha256 { get; set; } = string.Empty;
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
    /// <summary>All dependency decisions that shaped the selected artifact.</summary>
    public PowerShellCompilationDependencyTrace[] Dependencies { get; set; } = Array.Empty<PowerShellCompilationDependencyTrace>();
    /// <summary>Relocation-safe per-file decision traces.</summary>
    public PowerShellCompilationFileExplanation[] Files { get; set; } = Array.Empty<PowerShellCompilationFileExplanation>();
}
