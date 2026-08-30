namespace PowerForge;

/// <summary>Opt-in measured advice for one selected project target. It never mutates source or target mode.</summary>
public sealed class PowerShellCompilationProfileRecommendation
{
    /// <summary>Recommendation schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Exact target-contract identity considered.</summary>
    public string TargetContractSha256 { get; set; } = string.Empty;

    /// <summary>Selected target support level.</summary>
    public string SupportLevel { get; set; } = string.Empty;

    /// <summary>Analyzed source units.</summary>
    public int AnalyzedUnits { get; set; }

    /// <summary>Structurally and semantically eligible units before artifact shaping.</summary>
    public int EligibleUnits { get; set; }

    /// <summary>Eligible-unit ratio for this exact input; this is not PowerShell language coverage.</summary>
    public double EligibleUnitRatio { get; set; }

    /// <summary>Optional measured typed/hosted boundary profile.</summary>
    public PowerShellCompilationBoundaryRuntimeProfile? BoundaryProfile { get; set; }

    /// <summary>Deterministic suggested next evaluation.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Evidence-based reasons. The caller remains responsible for choosing a target.</summary>
    public string[] Reasons { get; set; } = Array.Empty<string>();
}
