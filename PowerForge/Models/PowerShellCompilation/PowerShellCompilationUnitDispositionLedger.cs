using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>One immutable causal record attached to a final unit disposition.</summary>
public sealed class PowerShellCompilationDispositionCause
{
    /// <summary>Creates a causal record.</summary>
    [JsonConstructor]
    public PowerShellCompilationDispositionCause(
        PowerShellCompilationDiagnosticCode code,
        string featureId,
        string message,
        int line,
        int column)
    {
        Code = code;
        FeatureId = featureId ?? string.Empty;
        Message = message ?? string.Empty;
        Line = line;
        Column = column;
    }

    /// <summary>Stable diagnostic category.</summary>
    public PowerShellCompilationDiagnosticCode Code { get; }
    /// <summary>Stable feature identifier.</summary>
    public string FeatureId { get; }
    /// <summary>Relocation-safe explanation.</summary>
    public string Message { get; }
    /// <summary>One-based source line.</summary>
    public int Line { get; }
    /// <summary>One-based source column.</summary>
    public int Column { get; }
}

/// <summary>Immutable final disposition of one authored compilation unit after artifact shaping.</summary>
public sealed class PowerShellCompilationUnitDisposition
{
    /// <summary>Creates one final unit disposition.</summary>
    public PowerShellCompilationUnitDisposition(
        string unitId,
        string relativePath,
        string name,
        PowerShellCompilationUnitKind kind,
        int startLine,
        bool semanticEligible,
        bool emittedClrMethod,
        bool emittedBinaryCmdlet,
        bool retainedHostedSource,
        int runtimeCommandRegions,
        int boundaryCrossings,
        bool shapingFallback,
        bool omitted,
        bool rejected,
        string generatedMemberName,
        IReadOnlyList<string>? dependencyCauses,
        IReadOnlyList<string>? boundaryCauses,
        IReadOnlyList<PowerShellCompilationDispositionCause>? diagnosticChain)
        : this(
            unitId,
            relativePath,
            name,
            kind,
            startLine,
            semanticEligible,
            emittedClrMethod,
            emittedBinaryCmdlet,
            retainedHostedSource,
            runtimeCommandRegions,
            boundaryCrossings,
            shapingFallback,
            omitted,
            rejected,
            generatedMemberName,
            dependencyCauses,
            boundaryCauses,
            diagnosticChain,
            moduleStateReadBoundaryCrossings: 0,
            moduleStateWriteBoundaryCrossings: 0)
    {
    }

    /// <summary>Creates one final unit disposition with directional parent-module state crossings.</summary>
    public PowerShellCompilationUnitDisposition(
        string unitId,
        string relativePath,
        string name,
        PowerShellCompilationUnitKind kind,
        int startLine,
        bool semanticEligible,
        bool emittedClrMethod,
        bool emittedBinaryCmdlet,
        bool retainedHostedSource,
        int runtimeCommandRegions,
        int boundaryCrossings,
        bool shapingFallback,
        bool omitted,
        bool rejected,
        string generatedMemberName,
        IReadOnlyList<string>? dependencyCauses,
        IReadOnlyList<string>? boundaryCauses,
        IReadOnlyList<PowerShellCompilationDispositionCause>? diagnosticChain,
        int moduleStateReadBoundaryCrossings,
        int moduleStateWriteBoundaryCrossings)
        : this(
            unitId,
            relativePath,
            name,
            kind,
            startLine,
            semanticEligible,
            emittedClrMethod,
            emittedBinaryCmdlet,
            retainedHostedSource,
            runtimeCommandRegions,
            boundaryCrossings,
            shapingFallback,
            omitted,
            rejected,
            generatedMemberName,
            dependencyCauses,
            boundaryCauses,
            diagnosticChain,
            moduleStateReadBoundaryCrossings,
            moduleStateWriteBoundaryCrossings,
            regionGraph: null)
    {
    }

    /// <summary>Creates one final unit disposition with directional state and canonical region evidence.</summary>
    [JsonConstructor]
    public PowerShellCompilationUnitDisposition(
        string unitId,
        string relativePath,
        string name,
        PowerShellCompilationUnitKind kind,
        int startLine,
        bool semanticEligible,
        bool emittedClrMethod,
        bool emittedBinaryCmdlet,
        bool retainedHostedSource,
        int runtimeCommandRegions,
        int boundaryCrossings,
        bool shapingFallback,
        bool omitted,
        bool rejected,
        string generatedMemberName,
        IReadOnlyList<string>? dependencyCauses,
        IReadOnlyList<string>? boundaryCauses,
        IReadOnlyList<PowerShellCompilationDispositionCause>? diagnosticChain,
        int moduleStateReadBoundaryCrossings,
        int moduleStateWriteBoundaryCrossings,
        PowerShellCompilationRegionGraph? regionGraph)
    {
        UnitId = unitId ?? string.Empty;
        RelativePath = relativePath ?? string.Empty;
        Name = name ?? string.Empty;
        Kind = kind;
        StartLine = startLine;
        SemanticEligible = semanticEligible;
        EmittedClrMethod = emittedClrMethod;
        EmittedBinaryCmdlet = emittedBinaryCmdlet;
        RetainedHostedSource = retainedHostedSource;
        RuntimeCommandRegions = Math.Max(0, runtimeCommandRegions);
        BoundaryCrossings = Math.Max(0, boundaryCrossings);
        ModuleStateReadBoundaryCrossings = Math.Max(0, moduleStateReadBoundaryCrossings);
        ModuleStateWriteBoundaryCrossings = Math.Max(0, moduleStateWriteBoundaryCrossings);
        if (ModuleStateReadBoundaryCrossings == 0 && ModuleStateWriteBoundaryCrossings == 0)
        {
            ModuleStateReadBoundaryCrossings = Math.Max(
                0,
                BoundaryCrossings - RuntimeCommandRegions -
                (RetainedHostedSource && (EmittedClrMethod || EmittedBinaryCmdlet) ? 1 : 0));
        }
        ShapingFallback = shapingFallback;
        Omitted = omitted;
        Rejected = rejected;
        GeneratedMemberName = generatedMemberName ?? string.Empty;
        DependencyCauses = Array.AsReadOnly((dependencyCauses ?? Array.Empty<string>()).ToArray());
        BoundaryCauses = Array.AsReadOnly((boundaryCauses ?? Array.Empty<string>()).ToArray());
        DiagnosticChain = Array.AsReadOnly((diagnosticChain ?? Array.Empty<PowerShellCompilationDispositionCause>()).ToArray());
        RegionGraph = regionGraph;
    }

    /// <summary>Stable relocation-safe authored-unit identity.</summary>
    public string UnitId { get; }
    /// <summary>Portable authored source path.</summary>
    public string RelativePath { get; }
    /// <summary>Authored function or synthetic script name.</summary>
    public string Name { get; }
    /// <summary>Authored unit kind.</summary>
    public PowerShellCompilationUnitKind Kind { get; }
    /// <summary>One-based authored start line.</summary>
    public int StartLine { get; }
    /// <summary>Whether canonical semantic analysis accepted the unit before shaping.</summary>
    public bool SemanticEligible { get; }
    /// <summary>Whether the delivered assembly contains a CLR implementation for the unit.</summary>
    public bool EmittedClrMethod { get; }
    /// <summary>Whether the delivered binary-module surface exposes the unit as a generated cmdlet.</summary>
    public bool EmittedBinaryCmdlet { get; }
    /// <summary>Whether authored source for the unit remains in the delivered hosted payload.</summary>
    public bool RetainedHostedSource { get; }
    /// <summary>Number of hosted PowerShell command regions inside the emitted implementation.</summary>
    public int RuntimeCommandRegions { get; }
    /// <summary>Number of statically identified typed/hosted boundary crossings.</summary>
    public int BoundaryCrossings { get; }
    /// <summary>Whether an analyzer-eligible unit still takes a runtime path after shaping.</summary>
    public bool ShapingFallback { get; }
    /// <summary>Whether the selected artifact intentionally omits the unit.</summary>
    public bool Omitted { get; }
    /// <summary>Whether the selected artifact rejects the unit.</summary>
    public bool Rejected { get; }
    /// <summary>Generated CLR member identity, when emitted.</summary>
    public string GeneratedMemberName { get; }
    /// <summary>Dependency causes known to affect this unit.</summary>
    public IReadOnlyList<string> DependencyCauses { get; }
    /// <summary>Boundary causes known to affect this unit.</summary>
    public IReadOnlyList<string> BoundaryCauses { get; }
    /// <summary>Ordered semantic and shaping diagnostic chain.</summary>
    public IReadOnlyList<PowerShellCompilationDispositionCause> DiagnosticChain { get; }

    /// <summary>Whether a typed CLR implementation is present for the authored unit.</summary>
    [JsonIgnore]
    public bool Emitted => EmittedClrMethod;

    /// <summary>Reads that cross from emitted CLR into the retained parent Hybrid script-module scope.</summary>
    public int ModuleStateReadBoundaryCrossings { get; }

    /// <summary>Writes that cross from emitted CLR into the retained parent Hybrid script-module scope.</summary>
    public int ModuleStateWriteBoundaryCrossings { get; }

    /// <summary>Canonical lowered region evidence when this unit has an emitted CLR method.</summary>
    public PowerShellCompilationRegionGraph? RegionGraph { get; }

    /// <summary>Total parent Hybrid script-module state crossings in either direction.</summary>
    [JsonIgnore]
    public int ModuleStateBoundaryCrossings => ModuleStateReadBoundaryCrossings + ModuleStateWriteBoundaryCrossings;

    /// <summary>Whether the delivered unit executes any PowerShell runtime semantics.</summary>
    [JsonIgnore]
    public bool RuntimeRouted => RetainedHostedSource || RuntimeCommandRegions > 0 || ModuleStateBoundaryCrossings > 0;

    /// <summary>Stable summary of all final artifact dispositions; dispositions may overlap.</summary>
    [JsonIgnore]
    public string ArtifactDisposition => string.Join("+", new[]
    {
        Emitted ? "TypedArtifact" : string.Empty,
        RetainedHostedSource ? "HostedSource" : string.Empty,
        RuntimeCommandRegions > 0 ? "HostedCommandRegions" : string.Empty,
        ModuleStateBoundaryCrossings > 0 ? "HostedModuleState" : string.Empty,
        Omitted ? "Omitted" : string.Empty,
        Rejected ? "Rejected" : string.Empty
    }.Where(static value => value.Length > 0));
}

/// <summary>
/// Immutable final authority for authored-unit disposition, metrics, explanation, census, and boundary evidence.
/// Counts intentionally allow an emitted unit also to retain hosted source or hosted command regions.
/// </summary>
public sealed class PowerShellCompilationUnitDispositionLedger
{
    /// <summary>Creates a final ledger.</summary>
    [JsonConstructor]
    public PowerShellCompilationUnitDispositionLedger(
        IReadOnlyList<PowerShellCompilationUnitDisposition>? entries,
        IReadOnlyList<string>? deliveryRuntimeCauses = null)
    {
        Entries = Array.AsReadOnly((entries ?? Array.Empty<PowerShellCompilationUnitDisposition>()).ToArray());
        DeliveryRuntimeCauses = Array.AsReadOnly((deliveryRuntimeCauses ?? Array.Empty<string>()).ToArray());
    }

    /// <summary>Ledger schema version.</summary>
    public int SchemaVersion => 3;
    /// <summary>Deterministically ordered authored-unit dispositions.</summary>
    public IReadOnlyList<PowerShellCompilationUnitDisposition> Entries { get; }
    /// <summary>Runtime delivery causes outside an authored compilation unit, such as manifest hooks.</summary>
    public IReadOnlyList<string> DeliveryRuntimeCauses { get; }
    /// <summary>Authored units represented by the ledger.</summary>
    public int AnalyzedUnits => Entries.Count;
    /// <summary>Units with a delivered CLR implementation.</summary>
    public int EmittedUnits => Entries.Count(static entry => entry.Emitted);
    /// <summary>Units that execute retained source or hosted command regions.</summary>
    public int RuntimeRoutedUnits => Entries.Count(static entry => entry.RuntimeRouted);
    /// <summary>Semantically ineligible units.</summary>
    public int FallbackUnits => Entries.Count(static entry => !entry.SemanticEligible);
    /// <summary>Semantically eligible units that still take a runtime route after shaping.</summary>
    public int ShapedFallbackUnits => Entries.Count(static entry => entry.ShapingFallback);
    /// <summary>Units intentionally omitted from the artifact.</summary>
    public int OmittedUnits => Entries.Count(static entry => entry.Omitted);
    /// <summary>Units rejected by the selected artifact contract.</summary>
    public int RejectedUnits => Entries.Count(static entry => entry.Rejected);
    /// <summary>Total statically identified hosted command regions.</summary>
    public int RuntimeCommandRegions => Entries.Sum(static entry => entry.RuntimeCommandRegions);
    /// <summary>Total statically identified parent Hybrid script-module state reads.</summary>
    public int ModuleStateReadBoundaryCrossings => Entries.Sum(static entry => entry.ModuleStateReadBoundaryCrossings);
    /// <summary>Total statically identified parent Hybrid script-module state writes.</summary>
    public int ModuleStateWriteBoundaryCrossings => Entries.Sum(static entry => entry.ModuleStateWriteBoundaryCrossings);
    /// <summary>Total statically identified parent Hybrid script-module state crossings.</summary>
    public int ModuleStateBoundaryCrossings => ModuleStateReadBoundaryCrossings + ModuleStateWriteBoundaryCrossings;
    /// <summary>Total statically identified typed/hosted crossings.</summary>
    public int BoundaryCrossings => Entries.Sum(static entry => entry.BoundaryCrossings);
    /// <summary>Whether the delivered artifact retains any PowerShell runtime execution path.</summary>
    public bool UsesPowerShellRuntimeFallback =>
        Entries.Any(static entry => entry.RuntimeRouted) || DeliveryRuntimeCauses.Count > 0;
    /// <summary>Typed emission coverage among authored units.</summary>
    public double CompilationCoveragePercentage => AnalyzedUnits == 0 ? 0 : EmittedUnits * 100d / AnalyzedUnits;
}
