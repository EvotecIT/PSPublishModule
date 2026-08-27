namespace PowerForge;

/// <summary>
/// Selects how PowerForge handles PowerShell that cannot be translated to typed CLR code.
/// </summary>
public enum PowerShellCompilationMode
{
    /// <summary>Analyze eligibility without producing an artifact.</summary>
    Analyze,

    /// <summary>Package the original PowerShell runtime and make no compilation claim.</summary>
    Package,

    /// <summary>Compile eligible units and retain explicit PowerShell runtime fallbacks.</summary>
    Hybrid,

    /// <summary>Require every discovered executable unit to compile without fallback.</summary>
    Strict
}
