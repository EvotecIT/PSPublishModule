namespace PowerForge;

/// <summary>Owns the host dependency and stopping effect carried by compiled loops.</summary>
internal static class PowerShellLoopInterruptContract
{
    internal static bool IsAvailable(PowerShellCompilationCapability capabilities)
        => capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors);

    internal static PowerShellSemanticEffect Effects(bool enabled)
        => enabled ? PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError : PowerShellSemanticEffect.None;

    internal static PowerShellRequiredCapability Capabilities(bool enabled)
        => enabled ? PowerShellRequiredCapability.PowerShellStopping | PowerShellRequiredCapability.PowerShellHostTypes
            : PowerShellRequiredCapability.None;

    // Native exception handling requires the full context for either service;
    // a loop without an enclosing handler needs only its stopping callback.
    internal static bool RequiresContext(PowerShellRequiredCapability capabilities)
        => (capabilities & (PowerShellRequiredCapability.PowerShellStatementErrors | PowerShellRequiredCapability.PowerShellStopping)) != 0;
}
