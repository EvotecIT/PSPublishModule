namespace PowerForge;

/// <summary>Shares entry invocation rendering between Package and Hybrid executables.</summary>
internal static class PowerShellPackagedEntryInvocationSource
{
    internal static string RenderInvoke(bool nativeEntry)
        => "                entryLookup.Invoke(() => powerShell.Invoke<PSObject>(input: null, output));";

    internal static string Render(bool nativeEntry)
        => nativeEntry ? PowerShellHybridNativeEntryEmitter.LauncherInvocation() : """
                        using var entryLookup = PowerForgePackagedSource.AddCommand(powerShell, PowerForgePackagedEntryPoint.Path, source);
                        foreach (var argument in ParseArguments(args))
                        {
                            if (argument.Name is null)
                                powerShell.AddArgument(argument.Value);
                            else if (argument.Value is null)
                                powerShell.AddParameter(argument.Name);
                            else
                                powerShell.AddParameter(argument.Name, argument.Value);
                        }
            """;
}
