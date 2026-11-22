namespace PowerForge;

/// <summary>Identifies generated command hosts whose behavior depends on native module state.</summary>
internal static class PowerShellModuleSessionStatePolicy
{
    internal static bool RequiresState(PowerShellCompiledMethod method)
        => method.NativeFunctionBinding is null && (method.RequiresPowerShellRuntimeState || method.RequiresPowerShellStreams ||
           method.RequiresPowerShellStatementErrors || method.RequiresPowerShellStopping || method.RequiresPowerShellCommandRegions ||
           method.RequiresPowerShellModuleState ||
           method.Parameters.Any(static parameter => parameter.TypeName == typeof(string).FullName));
}
