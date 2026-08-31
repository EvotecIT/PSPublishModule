using System;

namespace PowerForge;

/// <summary>Canonical capability sets for generated PowerShell compilation hosts.</summary>
public static class PowerShellCompilationCapabilities
{
    /// <summary>Target-backed runtime facts available without a PowerShell host.</summary>
    public const PowerShellCompilationCapability StaticRuntimeFacts =
        PowerShellCompilationCapability.PowerShellStreams |
        PowerShellCompilationCapability.RuntimeFreeProviderOperations |
        PowerShellCompilationCapability.RuntimeStateIntrinsics;

    /// <summary>Capabilities supplied by generated binary cmdlets.</summary>
    public const PowerShellCompilationCapability BinaryModule =
        PowerShellCompilationCapability.PowerShellStreams |
        PowerShellCompilationCapability.RuntimeFreeProviderOperations |
        PowerShellCompilationCapability.LocalFunctionCalls |
        PowerShellCompilationCapability.BoundParameters |
        PowerShellCompilationCapability.PowerShellObjects |
        PowerShellCompilationCapability.PipelineParameterBinding |
        PowerShellCompilationCapability.PowerShellHostTypes |
        PowerShellCompilationCapability.PowerShellLanguageConversions |
        PowerShellCompilationCapability.PowerShellLanguageOperators |
        PowerShellCompilationCapability.RuntimeStateIntrinsics;

    /// <summary>Capabilities supplied by a runtime-independent typed executable.</summary>
    public const PowerShellCompilationCapability TypedExecutable =
        PowerShellCompilationCapability.RuntimeFreeProviderOperations |
        PowerShellCompilationCapability.LocalFunctionCalls |
        PowerShellCompilationCapability.BoundParameters |
        PowerShellCompilationCapability.ExecutableParameterBinding |
        PowerShellCompilationCapability.RuntimeStateIntrinsics;
}

/// <summary>Surfaces on which a resolved parameter type can be represented without changing its meaning.</summary>
[Flags]
public enum PowerShellCompilationParameterTypeCapability
{
    /// <summary>The type is not supported by a typed parameter surface.</summary>
    None = 0,

    /// <summary>The type can appear in a generated CLR method signature.</summary>
    ClrMethod = 1,

    /// <summary>The type can be parsed by the runtime-independent executable argument binder.</summary>
    ProcessArgument = 2,

    /// <summary>The type requires a generated host that references System.Management.Automation.</summary>
    PowerShellHost = 4
}

/// <summary>One authored <c>Parameter</c> attribute binding for a parameter set.</summary>
public sealed class PowerShellCompilationParameterBinding
{
    /// <summary>Creates a parameter-set binding.</summary>
    public PowerShellCompilationParameterBinding(
        string? parameterSetName = null,
        bool mandatory = false,
        int? position = null,
        bool valueFromPipeline = false,
        bool valueFromPipelineByPropertyName = false,
        bool valueFromRemainingArguments = false,
        bool dontShow = false,
        string? helpMessage = null)
    {
        ParameterSetName = parameterSetName ?? string.Empty;
        Mandatory = mandatory;
        Position = position;
        ValueFromPipeline = valueFromPipeline;
        ValueFromPipelineByPropertyName = valueFromPipelineByPropertyName;
        ValueFromRemainingArguments = valueFromRemainingArguments;
        DontShow = dontShow;
        HelpMessage = helpMessage ?? string.Empty;
    }

    /// <summary>Authored parameter-set name, or an empty string for all parameter sets.</summary>
    public string ParameterSetName { get; }

    /// <summary>Whether the parameter is mandatory in this parameter set.</summary>
    public bool Mandatory { get; }

    /// <summary>Explicit zero-based position, or null when source leaves position implicit.</summary>
    public int? Position { get; }

    /// <summary>Whether values bind from pipeline objects in this parameter set.</summary>
    public bool ValueFromPipeline { get; }

    /// <summary>Whether values bind from pipeline-object properties in this parameter set.</summary>
    public bool ValueFromPipelineByPropertyName { get; }

    /// <summary>Whether remaining positional values bind to this parameter.</summary>
    public bool ValueFromRemainingArguments { get; }

    /// <summary>Whether discovery UIs should hide this parameter.</summary>
    public bool DontShow { get; }

    /// <summary>Literal help text preserved from source.</summary>
    public string HelpMessage { get; }
}

/// <summary>Binding behavior declared by an advanced function.</summary>
public sealed class PowerShellCompilationCommandBinding
{
    /// <summary>Creates an advanced-function binding description.</summary>
    public PowerShellCompilationCommandBinding(
        bool isAdvancedFunction = false,
        bool positionalBinding = true,
        string? defaultParameterSetName = null,
        bool supportsShouldProcess = false,
        string? confirmImpact = null)
    {
        IsAdvancedFunction = isAdvancedFunction;
        PositionalBinding = positionalBinding;
        DefaultParameterSetName = defaultParameterSetName ?? string.Empty;
        SupportsShouldProcess = supportsShouldProcess;
        ConfirmImpact = confirmImpact ?? string.Empty;
    }

    /// <summary>Whether source uses advanced-function binding.</summary>
    public bool IsAdvancedFunction { get; }

    /// <summary>Whether parameters without explicit positions receive source-order positions.</summary>
    public bool PositionalBinding { get; }

    /// <summary>Default parameter set, or an empty string when none is declared.</summary>
    public string DefaultParameterSetName { get; }

    /// <summary>Whether the generated command advertises ShouldProcess support.</summary>
    public bool SupportsShouldProcess { get; }

    /// <summary>Literal ConfirmImpact name, or an empty string when source uses the default.</summary>
    public string ConfirmImpact { get; }
}
