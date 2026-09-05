namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static void AddHostParameters(
        ICollection<string> parameters,
        PowerShellLoweredFunction function,
        bool requiresBoundParameters)
    {
        if (function.RequiresPowerShellStreams)
        {
            parameters.Add("global::System.Action<object?> __writeOutput");
            parameters.Add("global::System.Action<string> __writeVerbose");
            parameters.Add("global::System.Action<string> __writeDebug");
            parameters.Add("global::System.Action<string> __writeWarning");
            parameters.Add("global::System.Action<string> __writeInformation");
            parameters.Add("global::System.Action<string> __writeHost");
            parameters.Add("global::System.Action<string> __writeError");
        }
        if (function.RequiresProviderCancellation)
            parameters.Add("global::System.Threading.CancellationToken __providerCancellationToken");
        if (function.RequiresPowerShellCommandRegions)
        {
            parameters.Add("global::System.Action<string, object?[]> __invokePowerShellRegion");
            parameters.Add("global::System.Func<string, object?[], object?> __invokePowerShellCapture");
        }
        if (function.RequiresPowerShellRuntimeState)
        {
            parameters.Add("global::System.Func<string, bool> __shouldProcessTarget");
            parameters.Add("global::System.Func<string, string, bool> __shouldProcessAction");
            parameters.Add("object __psVersion");
            parameters.Add("bool __whatIfPreference");
            parameters.Add("global::System.Collections.Generic.IReadOnlyDictionary<string, object?> __runtimeState");
        }
        if (function.RequiresPowerShellModuleStateRead)
            parameters.Add("global::System.Func<string, object?> __readPowerShellModuleVariable");
        if (function.RequiresPowerShellModuleStateWrite)
            parameters.Add("global::System.Action<string, object?> __writePowerShellModuleVariable");
        if (requiresBoundParameters)
            parameters.Add("global::System.Collections.Generic.ISet<string> __boundParameters");
    }
}
