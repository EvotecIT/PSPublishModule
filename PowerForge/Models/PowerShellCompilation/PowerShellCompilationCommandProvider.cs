using System;

namespace PowerForge;

/// <summary>Semantic family implemented by a compile-time PowerShell command provider.</summary>
public enum PowerShellCompilationCommandFamily
{
    /// <summary>Non-success stream output.</summary>
    Stream,
    /// <summary>Property or object projection.</summary>
    Projection,
    /// <summary>Pipeline item filtering.</summary>
    Filtering,
    /// <summary>Pipeline item mapping or enumeration.</summary>
    Mapping,
    /// <summary>Pipeline item ordering.</summary>
    Sorting,
    /// <summary>A bounded command region executed by the PowerShell host.</summary>
    HostedRegion
}

/// <summary>Success-output shape produced by a command provider.</summary>
public enum PowerShellCompilationCommandOutput
{
    /// <summary>No success output.</summary>
    None,
    /// <summary>Input items pass through unchanged.</summary>
    PassThrough,
    /// <summary>Projected output has a provider-defined shape.</summary>
    Projected,
    /// <summary>A subset of input items is emitted.</summary>
    Filtered,
    /// <summary>Zero or more results can be emitted for every input item.</summary>
    Enumerated,
    /// <summary>Input items are emitted in a provider-defined order.</summary>
    Sorted,
    /// <summary>The compile-time provider cannot statically narrow the output shape.</summary>
    Unknown
}

/// <summary>Cardinality promised by a command provider.</summary>
public enum PowerShellCompilationCommandCardinality
{
    /// <summary>No success output.</summary>
    None,
    /// <summary>Exactly one value.</summary>
    Scalar,
    /// <summary>Zero or more values.</summary>
    Collection,
    /// <summary>Cardinality depends on runtime input or host behavior.</summary>
    Unknown
}

/// <summary>Error behavior promised by a command provider.</summary>
public enum PowerShellCompilationCommandErrors
{
    /// <summary>The provider does not introduce an error channel.</summary>
    None,
    /// <summary>The provider can report nonterminating errors.</summary>
    NonTerminating,
    /// <summary>The provider can throw terminating errors.</summary>
    Terminating,
    /// <summary>The hosted command retains PowerShell error semantics.</summary>
    PowerShellHost
}

/// <summary>Runtime adapter required by a command provider.</summary>
public sealed class PowerShellCompilationCommandAdapterContract
{
    /// <summary>Versioned semantic-profile identity consumed by the adapter.</summary>
    public string SemanticProfile { get; set; } = string.Empty;

    /// <summary>Whether the adapter can execute without a PowerShell runtime.</summary>
    public bool RuntimeFree { get; set; }

    /// <summary>Whether the adapter is declared safe for NativeAOT analysis.</summary>
    public bool AotCompatible { get; set; }

    /// <summary>Exact runtime dependencies required by the adapter.</summary>
    public string[] Dependencies { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Versioned deterministic metadata for one compile-time-only command semantic provider.
/// Providers describe commands without importing or executing source modules.
/// </summary>
public sealed class PowerShellCompilationCommandProviderContract
{
    /// <summary>Provider contract schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Stable provider identity.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Provider implementation version.</summary>
    public string ProviderVersion { get; set; } = "1.0";

    /// <summary>Stable feature id used by diagnostics and census output.</summary>
    public string FeatureId { get; set; } = string.Empty;

    /// <summary>Semantic family.</summary>
    public PowerShellCompilationCommandFamily Family { get; set; }

    /// <summary>Canonical PowerShell command name.</summary>
    public string CommandName { get; set; } = string.Empty;

    /// <summary>Accepted module qualifiers.</summary>
    public string[] ModuleNames { get; set; } = Array.Empty<string>();

    /// <summary>Accepted aliases.</summary>
    public string[] Aliases { get; set; } = Array.Empty<string>();

    /// <summary>Success-output shape.</summary>
    public PowerShellCompilationCommandOutput Output { get; set; }

    /// <summary>Success-output cardinality.</summary>
    public PowerShellCompilationCommandCardinality Cardinality { get; set; }

    /// <summary>Named stream written by the provider, or Success/None.</summary>
    public string Stream { get; set; } = "Success";

    /// <summary>Error behavior.</summary>
    public PowerShellCompilationCommandErrors Errors { get; set; }

    /// <summary>Runtime adapter contract.</summary>
    public PowerShellCompilationCommandAdapterContract Adapter { get; set; } = new();

    /// <summary>Providers are always compile-time-only.</summary>
    public bool CompileTimeOnly { get; set; } = true;

    /// <summary>Providers are forbidden from importing source modules during analysis.</summary>
    public bool MayImportSourceModules { get; set; }

    /// <summary>Providers are forbidden from executing source during analysis.</summary>
    public bool MayExecuteSource { get; set; }
}
