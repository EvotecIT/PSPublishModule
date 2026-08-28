namespace PowerForge;

/// <summary>Deterministic evidence emitted by the bound-IR optimization pipeline.</summary>
public sealed class PowerShellCompilationOptimizationEvidence
{
    /// <summary>Evidence schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Pure constant expressions replaced with equivalent literals.</summary>
    public int ConstantExpressionsFolded { get; set; }

    /// <summary>Statically unreachable conditional or loop branches removed.</summary>
    public int DeadBranchesRemoved { get; set; }

    /// <summary>Ordered optimizer passes enabled for the build.</summary>
    public string[] Passes { get; set; } = { "constant-folding", "dead-branch-elimination" };

    /// <summary>Whether at least one bound-IR node was rewritten.</summary>
    public bool Changed => ConstantExpressionsFolded > 0 || DeadBranchesRemoved > 0;
}

/// <summary>Static typed/hosted boundary evidence for one artifact plan.</summary>
public sealed class PowerShellCompilationBoundaryEvidence
{
    /// <summary>Evidence schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Generated typed callable entry points.</summary>
    public int TypedEntryPoints { get; set; }

    /// <summary>Statically emitted calls into hosted PowerShell command regions.</summary>
    public int HostedRegionSites { get; set; }

    /// <summary>Compilation units retained on a PowerShell runtime path.</summary>
    public int RuntimeFallbackUnits { get; set; }

    /// <summary>Minimum statically visible typed/hosted transitions; runtime invocation counts require profiling.</summary>
    public int StaticBoundarySites => TypedEntryPoints + HostedRegionSites;

    /// <summary>Deterministic build-time advisory when static fallback dominates the typed plan.</summary>
    public string Advisory { get; set; } = string.Empty;
}
