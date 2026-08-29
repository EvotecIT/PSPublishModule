namespace PowerForge;

/// <summary>Stable module-pipeline summary for a generated PowerShell binary module.</summary>
public sealed class PowerShellModuleCompilationResult
{
    /// <summary>Compilation mode used for the generated module.</summary>
    public PowerShellCompilationMode Mode { get; set; }

    /// <summary>Target framework used for the generated assembly.</summary>
    public string TargetFramework { get; set; } = string.Empty;

    /// <summary>Number of authored executable units analyzed by the canonical compiler.</summary>
    public int AnalyzedUnits { get; set; }

    /// <summary>Number of analyzed units emitted into the final typed artifact surface.</summary>
    public int EmittedUnits { get; set; }

    /// <summary>Number of units routed through the PowerShell runtime in the delivered artifact.</summary>
    public int RuntimeRoutedUnits { get; set; }

    /// <summary>Number of analyzed units rejected by semantic compilation and routed to fallback.</summary>
    public int FallbackUnits { get; set; }

    /// <summary>Number of semantically eligible units routed to fallback during artifact shaping.</summary>
    public int ShapedFallbackUnits { get; set; }

    /// <summary>Typed compilation coverage among analyzed units.</summary>
    public double CoveragePercentage { get; set; }

    /// <summary>Whether any unit still executes through dynamic PowerShell semantics.</summary>
    public bool UsesPowerShellRuntimeFallback { get; set; }

    /// <summary>Generated assembly path inside the built module staging directory.</summary>
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>Generated module manifest path inside the built module staging directory.</summary>
    public string ModuleManifestPath { get; set; } = string.Empty;

    /// <summary>Durable canonical compiler diagnostics and provenance manifest in module staging.</summary>
    public string CompilationManifestPath { get; set; } = string.Empty;

    /// <summary>Exact finalized payload consumed by signing, packaging, publishing, and installation.</summary>
    public IReadOnlyList<string> FinalizedPayloadFiles { get; internal set; } = Array.Empty<string>();

    /// <summary>SHA-256 identity of the exact transformed staging input presented to the compiler.</summary>
    internal string StagingInputSha256 { get; set; } = string.Empty;

    /// <summary>Compatibility alias for <see cref="AnalyzedUnits"/>.</summary>
    public int TotalUnits => AnalyzedUnits;

    /// <summary>Compatibility alias for <see cref="EmittedUnits"/>.</summary>
    public int CompiledUnits => EmittedUnits;

    /// <summary>Compatibility alias for <see cref="RuntimeRoutedUnits"/>.</summary>
    public int RuntimeFallbackUnits => RuntimeRoutedUnits;
}
