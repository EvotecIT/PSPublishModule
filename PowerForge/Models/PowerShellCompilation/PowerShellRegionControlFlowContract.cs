namespace PowerForge;

/// <summary>Continuation shape of a promoted retained-function region.</summary>
public enum PowerShellRegionControlFlowBehavior
{
    /// <summary>The helper either returns from the retained function or falls through to its next statement.</summary>
    ReturnOrFallThrough
}

/// <summary>
/// Versioned contract for a typed helper whose result controls retained PowerShell return versus
/// fallthrough. The retained engine remains the owner of success-output enumeration and errors.
/// </summary>
public sealed class PowerShellRegionControlFlowContract
{
    /// <summary>Creates an immutable return-or-fallthrough contract.</summary>
    public PowerShellRegionControlFlowContract(
        PowerShellRegionControlFlowBehavior behavior,
        PowerShellRegionTransferContract returnValue)
    {
        Behavior = behavior;
        ReturnValue = returnValue ?? throw new ArgumentNullException(nameof(returnValue));
    }

    /// <summary>Contract schema version.</summary>
    public int SchemaVersion => 1;
    /// <summary>Closed continuation behavior.</summary>
    public PowerShellRegionControlFlowBehavior Behavior { get; }
    /// <summary>Value and enumeration contract used only when the helper selects return.</summary>
    public PowerShellRegionTransferContract ReturnValue { get; }
}
