namespace PowerForge;

/// <summary>Stable module-pipeline summary for a generated PowerShell binary module.</summary>
public sealed class PowerShellModuleCompilationResult
{
    /// <summary>Compilation mode used for the generated module.</summary>
    public PowerShellCompilationMode Mode { get; set; }

    /// <summary>Target framework used for the generated assembly.</summary>
    public string TargetFramework { get; set; } = string.Empty;

    /// <summary>Number of authored executable units represented by the result.</summary>
    public int TotalUnits { get; set; }

    /// <summary>Number of units emitted as typed CLR methods.</summary>
    public int CompiledUnits { get; set; }

    /// <summary>Number of units retained on the PowerShell runtime fallback path.</summary>
    public int RuntimeFallbackUnits { get; set; }

    /// <summary>Typed compilation coverage among analyzed units.</summary>
    public double CoveragePercentage { get; set; }

    /// <summary>Whether any unit still executes through dynamic PowerShell semantics.</summary>
    public bool UsesPowerShellRuntimeFallback { get; set; }

    /// <summary>Generated assembly path inside the built module staging directory.</summary>
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>Generated module manifest path inside the built module staging directory.</summary>
    public string ModuleManifestPath { get; set; } = string.Empty;
}
