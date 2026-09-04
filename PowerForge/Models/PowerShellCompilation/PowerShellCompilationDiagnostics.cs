namespace PowerForge;

/// <summary>Compiler stage that produced a mapped failure.</summary>
public enum PowerShellCompilationFailureStage
{
    /// <summary>Source or build input validation.</summary>
    Input,
    /// <summary>Dependency discovery, locking, or closure validation.</summary>
    Dependency,
    /// <summary>Parsing, binding, analysis, or lowering.</summary>
    Analysis,
    /// <summary>Generated project restore.</summary>
    Restore,
    /// <summary>Managed compilation.</summary>
    Build,
    /// <summary>Trimming or NativeAOT publication.</summary>
    Optimization,
    /// <summary>Public ABI construction or comparison.</summary>
    Abi,
    /// <summary>Artifact staging, signing, or publication.</summary>
    Publication,
    /// <summary>Execution of the produced artifact.</summary>
    Runtime,
    /// <summary>The failing stage could not be classified more narrowly.</summary>
    Unknown
}

/// <summary>One portable authored location associated with a compiler or runtime failure.</summary>
public sealed class PowerShellCompilationFailureLocation
{
    /// <summary>Relocation-safe source path.</summary>
    public string RelativePath { get; set; } = string.Empty;
    /// <summary>Stable authored-unit identity.</summary>
    public string UnitId { get; set; } = string.Empty;
    /// <summary>Authored function or synthetic script name.</summary>
    public string UnitName { get; set; } = string.Empty;
    /// <summary>One-based authored start line.</summary>
    public int Line { get; set; }
    /// <summary>One-based authored start column.</summary>
    public int Column { get; set; }
    /// <summary>Stable diagnostic or tool code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Redacted failure detail.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Boundary contract relevant to the failure, when one exists.</summary>
    public string BoundaryContract { get; set; } = string.Empty;
}

/// <summary>Redacted failure diagnosis returned by build and runtime mapping.</summary>
public sealed class PowerShellCompilationFailure
{
    /// <summary>Failure contract schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Compiler stage that failed.</summary>
    public PowerShellCompilationFailureStage Stage { get; set; }
    /// <summary>Stable failure reason.</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Redacted primary diagnosis.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>Tool exit code when a child process failed.</summary>
    public int? ExitCode { get; set; }
    /// <summary>Authored locations mapped from generated build or runtime evidence.</summary>
    public PowerShellCompilationFailureLocation[] Locations { get; set; } = Array.Empty<PowerShellCompilationFailureLocation>();
}

/// <summary>One statement-level mapping from generated code back to authored source.</summary>
public sealed class PowerShellCompilationFailureMapEntry
{
    /// <summary>Stable parser-independent source document identity.</summary>
    public string DocumentId { get; set; } = string.Empty;
    /// <summary>Portable authored source path.</summary>
    public string RelativePath { get; set; } = string.Empty;
    /// <summary>Stable authored-unit identity.</summary>
    public string UnitId { get; set; } = string.Empty;
    /// <summary>Authored function or synthetic script name.</summary>
    public string UnitName { get; set; } = string.Empty;
    /// <summary>Generated CLR member name.</summary>
    public string GeneratedMemberName { get; set; } = string.Empty;
    /// <summary>One-based authored statement start line.</summary>
    public int SourceStartLine { get; set; }
    /// <summary>One-based authored statement start column.</summary>
    public int SourceStartColumn { get; set; }
    /// <summary>One-based authored statement end line.</summary>
    public int SourceEndLine { get; set; }
    /// <summary>One-based authored statement end column.</summary>
    public int SourceEndColumn { get; set; }
    /// <summary>One-based generated statement start line relative to the generated method, or zero for hosted source.</summary>
    public int GeneratedStartLine { get; set; }
    /// <summary>One-based generated statement end line relative to the generated method, or zero for hosted source.</summary>
    public int GeneratedEndLine { get; set; }
    /// <summary>Typed/hosted boundary contract relevant to this unit.</summary>
    public string BoundaryContract { get; set; } = string.Empty;
}

/// <summary>Portable statement-level map used to diagnose generated build and runtime failures.</summary>
public sealed class PowerShellCompilationFailureMap
{
    /// <summary>Failure-map schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Deterministically ordered mapping entries.</summary>
    public PowerShellCompilationFailureMapEntry[] Entries { get; set; } = Array.Empty<PowerShellCompilationFailureMapEntry>();
    /// <summary>SHA-256 over the canonical map entries.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>One function-level bound or lowered IR description without source text or parser objects.</summary>
public sealed class PowerShellCompilationIrUnitSnapshot
{
    /// <summary>Stable semantic symbol identity.</summary>
    public string UnitId { get; set; } = string.Empty;
    /// <summary>Stable source document identity.</summary>
    public string DocumentId { get; set; } = string.Empty;
    /// <summary>Authored function or synthetic entrypoint name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Resolved CLR return type.</summary>
    public string ReturnType { get; set; } = string.Empty;
    /// <summary>Bound output cardinality.</summary>
    public string OutputCardinality { get; set; } = string.Empty;
    /// <summary>Bound value states reachable on the success path.</summary>
    public string[] ValueStates { get; set; } = Array.Empty<string>();
    /// <summary>Required compiler capabilities.</summary>
    public string[] Capabilities { get; set; } = Array.Empty<string>();
    /// <summary>Semantic effects.</summary>
    public string[] Effects { get; set; } = Array.Empty<string>();
    /// <summary>Execution disposition selected before backend rendering.</summary>
    public string Disposition { get; set; } = string.Empty;
    /// <summary>Top-level IR node kinds in authored order.</summary>
    public string[] Nodes { get; set; } = Array.Empty<string>();
}

/// <summary>Diffable bound and lowered IR snapshots for one compilation.</summary>
public sealed class PowerShellCompilationIrSnapshotBundle
{
    /// <summary>IR snapshot schema version.</summary>
    public int SchemaVersion { get; set; } = 3;
    /// <summary>Snapshot content excludes parser AST objects, authored source, and hosted source payload.</summary>
    public bool RedactedSemanticOnly { get; set; } = true;
    /// <summary>Deterministically ordered bound semantic units.</summary>
    public PowerShellCompilationIrUnitSnapshot[] Bound { get; set; } = Array.Empty<PowerShellCompilationIrUnitSnapshot>();
    /// <summary>Deterministically ordered lowered units.</summary>
    public PowerShellCompilationIrUnitSnapshot[] Lowered { get; set; } = Array.Empty<PowerShellCompilationIrUnitSnapshot>();
    /// <summary>SHA-256 over the canonical snapshot.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>Durable reference to an optional IR snapshot artifact.</summary>
public sealed class PowerShellCompilationIrSnapshotEvidence
{
    /// <summary>Whether the caller requested snapshot publication.</summary>
    public bool Emitted { get; set; }
    /// <summary>Portable path relative to the compilation manifest.</summary>
    public string RelativePath { get; set; } = string.Empty;
    /// <summary>SHA-256 of the canonical snapshot artifact.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>One deterministic compiler audit decision.</summary>
public sealed class PowerShellCompilationAuditEvent
{
    /// <summary>Stable event category.</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>Stable reason code.</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Stable outcome.</summary>
    public string Outcome { get; set; } = string.Empty;
    /// <summary>Optional portable subject identity.</summary>
    public string Subject { get; set; } = string.Empty;
}

/// <summary>Auditable cache, graph, ABI, boundary, and provider decisions.</summary>
public sealed class PowerShellCompilationAuditTrail
{
    /// <summary>Audit-trail schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Deterministically ordered events.</summary>
    public PowerShellCompilationAuditEvent[] Events { get; set; } = Array.Empty<PowerShellCompilationAuditEvent>();
    /// <summary>SHA-256 over the canonical event sequence.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>Retention and redaction policy for compiler diagnostics and optional evidence.</summary>
public sealed class PowerShellCompilationDiagnosticsPolicy
{
    /// <summary>Policy schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Diagnostics remain local unless a user explicitly exports them.</summary>
    public bool LocalOnly { get; set; } = true;
    /// <summary>PowerForge does not automatically upload compiler diagnostics or crash evidence.</summary>
    public bool AutomaticUpload { get; set; }
    /// <summary>Manifest, trace, audit, map, and optional IR evidence follow the artifact lifetime.</summary>
    public string ArtifactEvidenceRetention { get; set; } = "ArtifactLifetime";
    /// <summary>Failed-build and crash bundles are user-managed and should be removed after this many days when no longer needed.</summary>
    public int RecommendedFailureBundleRetentionDays { get; set; } = 7;
    /// <summary>Data categories excluded from portable diagnostic evidence.</summary>
    public string[] RedactedData { get; set; } = Array.Empty<string>();
}
