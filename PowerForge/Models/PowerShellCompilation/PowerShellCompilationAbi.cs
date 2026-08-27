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
    public const string RuntimeFreeStrictVersion = "1.1";

    /// <summary>Current compiler/runtime ABI version.</summary>
    public const string RuntimeFreeAbiVersion = "3";

    /// <summary>Profile name.</summary>
    public string Name { get; set; } = RuntimeFreeStrictName;

    /// <summary>Profile version.</summary>
    public string Version { get; set; } = RuntimeFreeStrictVersion;

    /// <summary>Compiler/runtime ABI version.</summary>
    public string CompilerRuntimeAbiVersion { get; set; } = RuntimeFreeAbiVersion;

    /// <summary>Whether the profile excludes a PowerShell runtime and dynamic source evaluation.</summary>
    public bool RuntimeFree { get; set; } = true;

    /// <summary>Whether emitted code depends on a separately versioned compiler runtime substrate.</summary>
    public bool HasRuntimeSubstrate { get; set; }

    /// <summary>Runtime substrate identity, or <c>None</c> when helpers are emitted into the artifact itself.</summary>
    public string RuntimeSubstrate { get; set; } = "None";
}

/// <summary>
/// Normalized public CLR surface emitted for a compiled PowerShell artifact.
/// </summary>
public sealed class PowerShellCompilationAbiManifest
{
    /// <summary>ABI schema version.</summary>
    public int SchemaVersion { get; set; } = 3;

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

    /// <summary>Command aliases participating in PowerShell binding.</summary>
    public string[] Aliases { get; set; } = Array.Empty<string>();

    /// <summary>Whether the source declares advanced-function binding.</summary>
    public bool IsAdvancedFunction { get; set; }

    /// <summary>Whether source-order positional binding is enabled.</summary>
    public bool PositionalBinding { get; set; } = true;

    /// <summary>Default parameter-set name, or empty when none is declared.</summary>
    public string DefaultParameterSetName { get; set; } = string.Empty;

    /// <summary>Whether the command advertises ShouldProcess support.</summary>
    public bool SupportsShouldProcess { get; set; }

    /// <summary>Declared ConfirmImpact value, or empty when the default applies.</summary>
    public string ConfirmImpact { get; set; } = string.Empty;

    /// <summary>Ordered CLR parameters.</summary>
    public PowerShellCompilationAbiParameter[] Parameters { get; set; } = Array.Empty<PowerShellCompilationAbiParameter>();

    /// <summary>Versioned command semantic providers whose adapters participate in method behavior.</summary>
    public PowerShellCompilationCommandProviderContract[] CommandProviders { get; set; } = Array.Empty<PowerShellCompilationCommandProviderContract>();
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

    /// <summary>Whether this CLR parameter was added by the compiler rather than authored in PowerShell.</summary>
    public bool CompilerAdded { get; set; }

    /// <summary>Stable purpose of a compiler-added parameter.</summary>
    public string CompilerPurpose { get; set; } = string.Empty;

    /// <summary>Whether the source parameter is a PowerShell switch.</summary>
    public bool IsSwitch { get; set; }

    /// <summary>PowerShell aliases accepted by the binder.</summary>
    public string[] Aliases { get; set; } = Array.Empty<string>();

    /// <summary>Whether source declares a default value.</summary>
    public bool HasDefaultValue { get; set; }

    /// <summary>Canonical supported default value, or null when no default is declared.</summary>
    public PowerShellCompilationLiteral? DefaultValue { get; set; }

    /// <summary>Authored parameter-set and pipeline binding contracts.</summary>
    public PowerShellCompilationParameterBinding[] Bindings { get; set; } = Array.Empty<PowerShellCompilationParameterBinding>();

    /// <summary>Authored validation contracts.</summary>
    public PowerShellCompilationValidation[] Validations { get; set; } = Array.Empty<PowerShellCompilationValidation>();

    /// <summary>Whether an empty string is allowed.</summary>
    public bool AllowEmptyString { get; set; }

    /// <summary>Whether an empty collection is allowed.</summary>
    public bool AllowEmptyCollection { get; set; }

    /// <summary>Whether wildcard syntax is part of the parameter contract.</summary>
    public bool SupportsWildcards { get; set; }
}
