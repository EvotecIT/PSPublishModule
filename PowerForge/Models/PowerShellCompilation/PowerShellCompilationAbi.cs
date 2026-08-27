using System;

namespace PowerForge;

/// <summary>
/// Versioned semantic contract for runtime-free Strict PowerShell artifacts.
/// </summary>
public sealed class PowerShellCompilationSemanticProfile
{
    /// <summary>Current runtime-free Strict profile name.</summary>
    public const string RuntimeFreeStrictName = "PowerForge.PowerShell.Strict.RuntimeFree";

    /// <summary>Current semantic profile version.</summary>
    public const string RuntimeFreeStrictVersion = "1.0";

    /// <summary>Current compiler/runtime ABI version.</summary>
    public const string RuntimeFreeAbiVersion = "1";

    /// <summary>Profile name.</summary>
    public string Name { get; set; } = RuntimeFreeStrictName;

    /// <summary>Profile version.</summary>
    public string Version { get; set; } = RuntimeFreeStrictVersion;

    /// <summary>Compiler/runtime ABI version.</summary>
    public string CompilerRuntimeAbiVersion { get; set; } = RuntimeFreeAbiVersion;

    /// <summary>Whether the profile excludes a PowerShell runtime and dynamic source evaluation.</summary>
    public bool RuntimeFree { get; set; } = true;
}

/// <summary>
/// Normalized public CLR surface emitted for a compiled PowerShell artifact.
/// </summary>
public sealed class PowerShellCompilationAbiManifest
{
    /// <summary>ABI schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Generated CLR namespace.</summary>
    public string NamespaceName { get; set; } = string.Empty;

    /// <summary>Generated CLR type name.</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Stable command-to-CLR mappings sorted by PowerShell command identity.</summary>
    public PowerShellCompilationAbiMethod[] Methods { get; set; } = Array.Empty<PowerShellCompilationAbiMethod>();

    /// <summary>SHA-256 of the canonical ABI representation.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>One PowerShell-command-to-CLR member mapping.</summary>
public sealed class PowerShellCompilationAbiMethod
{
    /// <summary>Authored PowerShell command name.</summary>
    public string PowerShellName { get; set; } = string.Empty;

    /// <summary>Generated CLR method name.</summary>
    public string ClrName { get; set; } = string.Empty;

    /// <summary>Generated CLR return type.</summary>
    public string ReturnType { get; set; } = string.Empty;

    /// <summary>Success-output cardinality contract: None, Scalar, or Collection.</summary>
    public string OutputCardinality { get; set; } = string.Empty;

    /// <summary>Whether the method can accept or return null under its authored contract.</summary>
    public bool Nullable { get; set; }

    /// <summary>Stream contract expected by the generated method.</summary>
    public string StreamContract { get; set; } = "SuccessOutputOnly";

    /// <summary>Exception surface exposed to direct CLR callers.</summary>
    public string ExceptionContract { get; set; } = "ClrDirect";

    /// <summary>Ordered CLR parameters.</summary>
    public PowerShellCompilationAbiParameter[] Parameters { get; set; } = Array.Empty<PowerShellCompilationAbiParameter>();
}

/// <summary>One ordered public CLR parameter contract.</summary>
public sealed class PowerShellCompilationAbiParameter
{
    /// <summary>PowerShell parameter name.</summary>
    public string PowerShellName { get; set; } = string.Empty;

    /// <summary>Generated CLR parameter name.</summary>
    public string ClrName { get; set; } = string.Empty;

    /// <summary>CLR parameter type.</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Whether callers may pass null.</summary>
    public bool Nullable { get; set; }

    /// <summary>Whether the PowerShell binding contract requires an explicitly supplied value.</summary>
    public bool Required { get; set; }

    /// <summary>Whether omitted and explicitly bound values are distinguished.</summary>
    public bool TracksBoundState { get; set; }
}
