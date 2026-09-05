using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Execution route selected for one coarse lowered region.</summary>
public enum PowerShellCompilationRegionExecution
{
    /// <summary>The complete region executes as generated CLR code.</summary>
    Typed,
    /// <summary>The region is one direct PowerShell-hosted command boundary.</summary>
    Hosted,
    /// <summary>Generated CLR control or value flow contains one or more hosted boundaries.</summary>
    Mixed
}

/// <summary>One deterministic coarse region derived from canonical lowered IR.</summary>
public sealed class PowerShellCompilationRegion
{
    /// <summary>Creates an immutable coarse-region record.</summary>
    [JsonConstructor]
    public PowerShellCompilationRegion(
        string regionId,
        int ordinal,
        PowerShellCompilationRegionExecution execution,
        int startOffset,
        int endOffset,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        IReadOnlyList<string>? inputs,
        IReadOnlyList<string>? outputs,
        IReadOnlyList<string>? mutations,
        IReadOnlyList<string>? streams,
        IReadOnlyList<string>? errors,
        string ordering,
        int hostedCommandBoundarySites,
        int moduleStateReadBoundarySites,
        int moduleStateWriteBoundarySites)
    {
        RegionId = regionId ?? string.Empty;
        Ordinal = Math.Max(0, ordinal);
        Execution = execution;
        StartOffset = Math.Max(0, startOffset);
        EndOffset = Math.Max(StartOffset, endOffset);
        StartLine = Math.Max(0, startLine);
        StartColumn = Math.Max(0, startColumn);
        EndLine = Math.Max(StartLine, endLine);
        EndColumn = Math.Max(0, endColumn);
        Inputs = Copy(inputs);
        Outputs = Copy(outputs);
        Mutations = Copy(mutations);
        Streams = Copy(streams);
        Errors = Copy(errors);
        Ordering = ordering ?? string.Empty;
        HostedCommandBoundarySites = Math.Max(0, hostedCommandBoundarySites);
        ModuleStateReadBoundarySites = Math.Max(0, moduleStateReadBoundarySites);
        ModuleStateWriteBoundarySites = Math.Max(0, moduleStateWriteBoundarySites);
    }

    /// <summary>Portable identity derived from source-document identity, span, and execution route.</summary>
    public string RegionId { get; }
    /// <summary>Zero-based region order within the authored function.</summary>
    public int Ordinal { get; }
    /// <summary>Typed, hosted, or mixed execution route.</summary>
    public PowerShellCompilationRegionExecution Execution { get; }
    /// <summary>Zero-based authored start offset.</summary>
    public int StartOffset { get; }
    /// <summary>Zero-based authored end offset.</summary>
    public int EndOffset { get; }
    /// <summary>One-based authored start line.</summary>
    public int StartLine { get; }
    /// <summary>One-based authored start column.</summary>
    public int StartColumn { get; }
    /// <summary>One-based authored end line.</summary>
    public int EndLine { get; }
    /// <summary>One-based authored end column.</summary>
    public int EndColumn { get; }
    /// <summary>Stable symbol identities or state values read by the region.</summary>
    public IReadOnlyList<string> Inputs { get; }
    /// <summary>Success output and mutated values observed by later regions.</summary>
    public IReadOnlyList<string> Outputs { get; }
    /// <summary>Stable local, parameter, object, or module-state mutation identities.</summary>
    public IReadOnlyList<string> Mutations { get; }
    /// <summary>PowerShell streams the region can emit, in canonical stream-name order.</summary>
    public IReadOnlyList<string> Streams { get; }
    /// <summary>Error routes that cross or terminate the region.</summary>
    public IReadOnlyList<string> Errors { get; }
    /// <summary>Ordering contract applied by lowering and emission.</summary>
    public string Ordering { get; }
    /// <summary>Static calls into hosted PowerShell command regions.</summary>
    public int HostedCommandBoundarySites { get; }
    /// <summary>Static reads from retained parent Hybrid module state, including propagated local calls.</summary>
    public int ModuleStateReadBoundarySites { get; }
    /// <summary>Static writes to retained parent Hybrid module state, including propagated local calls.</summary>
    public int ModuleStateWriteBoundarySites { get; }
    /// <summary>Total statically visible typed/hosted crossings within this region.</summary>
    public int StaticBoundaryCrossings =>
        HostedCommandBoundarySites + ModuleStateReadBoundarySites + ModuleStateWriteBoundarySites;
    /// <summary>Values transferred into or out of a hosted or mixed region.</summary>
    public int StaticBoundaryValueTransfers =>
        Execution == PowerShellCompilationRegionExecution.Typed
            ? 0
            : Inputs.Concat(Outputs).Concat(Mutations).Distinct(StringComparer.Ordinal).Count();
    /// <summary>
    /// Deterministic structural cost used to rank profiling candidates. This is not a time estimate;
    /// runtime promotion still requires a measured boundary profile.
    /// </summary>
    public int StaticBoundaryCostUnits => StaticBoundaryCrossings + StaticBoundaryValueTransfers;

    private static IReadOnlyList<string> Copy(IReadOnlyList<string>? values)
        => Array.AsReadOnly((values ?? Array.Empty<string>()).ToArray());
}

/// <summary>Canonical coarse-region graph for one emitted CLR method.</summary>
public sealed class PowerShellCompilationRegionGraph
{
    /// <summary>Creates an immutable graph in authored region order.</summary>
    [JsonConstructor]
    public PowerShellCompilationRegionGraph(IReadOnlyList<PowerShellCompilationRegion>? regions)
        => Regions = Array.AsReadOnly((regions ?? Array.Empty<PowerShellCompilationRegion>())
            .OrderBy(static region => region.Ordinal)
            .ToArray());

    /// <summary>Region-graph schema version.</summary>
    public int SchemaVersion => 1;
    /// <summary>Coarse regions in authored execution order.</summary>
    public IReadOnlyList<PowerShellCompilationRegion> Regions { get; }
    /// <summary>Total hosted command boundaries represented by the graph.</summary>
    public int HostedCommandBoundarySites => Regions.Sum(static region => region.HostedCommandBoundarySites);
    /// <summary>Total parent Hybrid module-state reads represented by the graph.</summary>
    public int ModuleStateReadBoundarySites => Regions.Sum(static region => region.ModuleStateReadBoundarySites);
    /// <summary>Total parent Hybrid module-state writes represented by the graph.</summary>
    public int ModuleStateWriteBoundarySites => Regions.Sum(static region => region.ModuleStateWriteBoundarySites);
    /// <summary>Total statically visible typed/hosted crossings represented by the graph.</summary>
    public int StaticBoundaryCrossings => Regions.Sum(static region => region.StaticBoundaryCrossings);
    /// <summary>Total deterministic structural cost used to rank candidates for measured profiling.</summary>
    public int StaticBoundaryCostUnits => Regions.Sum(static region => region.StaticBoundaryCostUnits);
}
