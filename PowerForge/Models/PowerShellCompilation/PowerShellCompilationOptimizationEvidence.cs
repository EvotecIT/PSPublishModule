namespace PowerForge;

/// <summary>Deterministic evidence emitted by bound-IR rewrites, backend selections, and source instrumentation.</summary>
public sealed class PowerShellCompilationOptimizationEvidence
{
    /// <summary>Evidence schema version.</summary>
    public int SchemaVersion { get; set; } = 3;

    /// <summary>Pure constant expressions replaced with equivalent literals.</summary>
    public int ConstantExpressionsFolded { get; set; }

    /// <summary>Statically unreachable conditional or loop branches removed.</summary>
    public int DeadBranchesRemoved { get; set; }

    /// <summary>Redundant CLR identity conversions removed before lowering.</summary>
    public int IdentityConversionsRemoved { get; set; }

    /// <summary>Pipeline stages retained in one hosted invocation rather than separate host crossings.</summary>
    public int PipelineStagesFused { get; set; }

    /// <summary>Adjacent command statements retained in one hosted command region.</summary>
    public int CommandRegionStatementsCoalesced { get; set; }

    /// <summary>Array foreach loops selected for indexed generated-code emission.</summary>
    public int SpecializedCollectionLoops { get; set; }

    /// <summary>PowerShell-language conversion sites emitted through one generic per-method conversion plan.</summary>
    public int RuntimeConversionSitesSpecialized { get; set; }

    /// <summary>Bound statements emitted with authored-source sequence mapping.</summary>
    public int SourceMappedStatements { get; set; }

    /// <summary>Ordered immutable bound-IR rewrite passes enabled for the build.</summary>
    public string[] Passes { get; set; } =
    {
        "constant-folding",
        "dead-branch-elimination",
        "identity-conversion-elimination"
    };

    /// <summary>Ordered backend lowering selections whose use is counted by this evidence.</summary>
    public string[] BackendOptimizations { get; set; } =
    {
        "allocation-reduction",
        "pipeline-stage-fusion",
        "command-region-coalescing",
        "specialized-collection-loops",
        "cached-conversion-plans"
    };

    /// <summary>Ordered generated-artifact instrumentation enabled for the build.</summary>
    public string[] Instrumentation { get; set; } =
    {
        "authored-source-sequence-mapping"
    };

    /// <summary>Whether at least one bound-IR node was rewritten.</summary>
    public bool Changed => ConstantExpressionsFolded > 0 || DeadBranchesRemoved > 0 || IdentityConversionsRemoved > 0;
}

/// <summary>Static typed/hosted boundary evidence for one artifact plan.</summary>
public sealed class PowerShellCompilationBoundaryEvidence
{
    /// <summary>Evidence schema version.</summary>
    public int SchemaVersion { get; set; } = 3;

    /// <summary>Generated typed callable entry points.</summary>
    public int TypedEntryPoints { get; set; }

    /// <summary>Statically emitted calls into hosted PowerShell command regions.</summary>
    public int HostedRegionSites { get; set; }

    /// <summary>Typed CLR regions invoked from retained PowerShell function surfaces.</summary>
    public int PromotedTypedRegions { get; set; }

    /// <summary>Compilation units retained on a PowerShell runtime path.</summary>
    public int RuntimeFallbackUnits { get; set; }

    /// <summary>Minimum statically visible typed/hosted transitions; runtime invocation counts require profiling.</summary>
    public int StaticBoundarySites => TypedEntryPoints + HostedRegionSites + PromotedTypedRegions;

    /// <summary>Deterministic build-time advisory when static fallback dominates the typed plan.</summary>
    public string Advisory { get; set; } = string.Empty;

    /// <summary>Optional measured equivalent-workload runtime profile supplied to the build.</summary>
    public PowerShellCompilationBoundaryRuntimeProfile? RuntimeProfile { get; set; }
}

/// <summary>Measured cost of equivalent coarse typed work and fine hosted-boundary work.</summary>
public sealed class PowerShellCompilationBoundaryRuntimeProfile
{
    /// <summary>Evidence schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Stable caller-selected workload identity.</summary>
    public string Workload { get; set; } = string.Empty;

    /// <summary>Runtime identifier of the host that executed the profile.</summary>
    public string RuntimeIdentifier { get; set; } = string.Empty;

    /// <summary>Equivalent coarse typed workload duration in nanoseconds.</summary>
    public long BaselineDurationNanoseconds { get; set; }

    /// <summary>Fine hosted-boundary workload duration in nanoseconds.</summary>
    public long BoundaryDurationNanoseconds { get; set; }

    /// <summary>Total measured typed/hosted boundary invocations.</summary>
    public long BoundaryInvocations { get; set; }

    /// <summary>Estimated extra nanoseconds attributable to each boundary invocation.</summary>
    public double EstimatedOverheadNanosecondsPerBoundary { get; set; }

    /// <summary>Share of fine-boundary runtime attributed to boundary overhead.</summary>
    public double EstimatedOverheadRatio { get; set; }

    /// <summary>Deterministic runtime-profile advisory.</summary>
    public string Advisory { get; set; } = string.Empty;
}
