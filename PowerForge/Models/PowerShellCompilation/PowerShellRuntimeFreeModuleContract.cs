using System;

namespace PowerForge;

/// <summary>Defines the instance lifetime of a runtime-free compiled module.</summary>
public sealed class PowerShellRuntimeFreeModuleContract
{
    /// <summary>Authored initialization source identity.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Stable source document identity.</summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Private generated initializer method.</summary>
    public string InitializerName { get; set; } = string.Empty;

    /// <summary>Typed inputs supplied to construction and reset.</summary>
    public PowerShellCompilationParameter[] Parameters { get; set; } = Array.Empty<PowerShellCompilationParameter>();

    /// <summary>Supported constructor/reset arities; omitted trailing authored defaults are evaluated by the initializer.</summary>
    public int[] SupportedParameterCounts { get; set; } = Array.Empty<int>();

    /// <summary>Authored persistent scalar fields.</summary>
    public PowerShellRuntimeFreeModuleField[] Fields { get; set; } = Array.Empty<PowerShellRuntimeFreeModuleField>();

    /// <summary>Calls and lifetime transitions serialize on each instance.</summary>
    public string ConcurrencyPolicy => "SerializedPerInstance";

    /// <summary>Failed reset restores the previous instance state.</summary>
    public string ResetPolicy => "AtomicRollback";

    /// <summary>Disposal is idempotent, clears state, and rejects subsequent calls.</summary>
    public string DisposalPolicy => "TerminalClearState";

    /// <summary>Nested method calls are allowed; callbacks cannot reset or dispose an active instance.</summary>
    public string ReentrancyPolicy => "RejectLifetimeTransitionsDuringCalls";
}

/// <summary>One explicitly typed field in a compiled module instance.</summary>
public sealed class PowerShellRuntimeFreeModuleField
{
    /// <summary>Authored name without its script scope prefix.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>CLR storage type.</summary>
    public string TypeName { get; set; } = string.Empty;
}
