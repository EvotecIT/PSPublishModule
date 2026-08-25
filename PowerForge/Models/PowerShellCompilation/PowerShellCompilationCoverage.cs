using System;

namespace PowerForge;

/// <summary>
/// Separates analyzer eligibility from functions that survive post-emission artifact shaping.
/// </summary>
public sealed class PowerShellCompilationCoverageBreakdown
{
    /// <summary>Creates a coverage breakdown.</summary>
    public PowerShellCompilationCoverageBreakdown(
        bool postEmissionEvaluated = false,
        int totalFunctions = 0,
        int analyzerEligibleFunctions = 0,
        int emittedFunctions = 0,
        int droppedEligibleFunctions = 0,
        int fallbackFunctions = 0,
        int totalScriptUnits = 0,
        int structurallyEligibleScriptUnits = 0,
        int fallbackScriptUnits = 0,
        int runtimeOnlyFunctions = 0,
        int runtimeOnlyScriptUnits = 0)
    {
        PostEmissionEvaluated = postEmissionEvaluated;
        TotalFunctions = totalFunctions;
        AnalyzerEligibleFunctions = analyzerEligibleFunctions;
        EmittedFunctions = emittedFunctions;
        DroppedEligibleFunctions = droppedEligibleFunctions;
        FallbackFunctions = fallbackFunctions;
        TotalScriptUnits = totalScriptUnits;
        StructurallyEligibleScriptUnits = structurallyEligibleScriptUnits;
        FallbackScriptUnits = fallbackScriptUnits;
        RuntimeOnlyFunctions = runtimeOnlyFunctions;
        RuntimeOnlyScriptUnits = runtimeOnlyScriptUnits;
    }

    /// <summary>Whether artifact-surface emission and shaping were evaluated.</summary>
    public bool PostEmissionEvaluated { get; }

    /// <summary>Total authored function units discovered.</summary>
    public int TotalFunctions { get; }

    /// <summary>Functions accepted by the analyzer before graph and artifact shaping.</summary>
    public int AnalyzerEligibleFunctions { get; }

    /// <summary>Functions that survived graph validation and artifact shaping as CLR methods.</summary>
    public int EmittedFunctions { get; }

    /// <summary>Analyzer-eligible functions rejected during graph or artifact shaping.</summary>
    public int DroppedEligibleFunctions { get; }

    /// <summary>Functions that remain on the PowerShell runtime path.</summary>
    public int FallbackFunctions { get; }

    /// <summary>Top-level script or module-initialization units discovered.</summary>
    public int TotalScriptUnits { get; }

    /// <summary>Script units structurally accepted by analysis; these are not counted as emitted methods.</summary>
    public int StructurallyEligibleScriptUnits { get; }

    /// <summary>Script units requiring PowerShell runtime behavior.</summary>
    public int FallbackScriptUnits { get; }

    /// <summary>Function units loaded through runtime-only manifest hooks.</summary>
    public int RuntimeOnlyFunctions { get; }

    /// <summary>Script units loaded through runtime-only manifest hooks.</summary>
    public int RuntimeOnlyScriptUnits { get; }

    /// <summary>Percentage of authored functions emitted as typed CLR methods.</summary>
    public double EmittedFunctionCoveragePercentage => TotalFunctions == 0 ? 0 : EmittedFunctions * 100d / TotalFunctions;
}

/// <summary>Reports that a baseline corpus input changed while retaining its portable identity.</summary>
public sealed class PowerShellCompilationCensusSourceDrift
{
    /// <summary>Creates a source-drift result.</summary>
    public PowerShellCompilationCensusSourceDrift(string product, string baselineFingerprint, string currentFingerprint)
    {
        Product = product ?? string.Empty;
        BaselineFingerprint = baselineFingerprint ?? string.Empty;
        CurrentFingerprint = currentFingerprint ?? string.Empty;
    }

    /// <summary>Portable census product name.</summary>
    public string Product { get; }

    /// <summary>Content fingerprint recorded by the baseline.</summary>
    public string BaselineFingerprint { get; }

    /// <summary>Content fingerprint observed in the current run.</summary>
    public string CurrentFingerprint { get; }
}
