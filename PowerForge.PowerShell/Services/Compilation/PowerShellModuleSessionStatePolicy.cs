namespace PowerForge;

/// <summary>Identifies generated command hosts whose behavior depends on native module state.</summary>
internal static class PowerShellModuleSessionStatePolicy
{
    internal static bool RequiresState(PowerShellCompiledMethod method)
        => method.RequiresPowerShellRuntimeState || method.RequiresPowerShellStreams ||
           method.RequiresPowerShellStatementErrors || method.RequiresPowerShellCommandRegions ||
           method.RequiresPowerShellModuleState;
}
