namespace PowerForge;

/// <summary>Shares entry invocation rendering between Package and Hybrid executables.</summary>
internal static class PowerShellPackagedEntryInvocationSource
{
    internal static string Render(bool nativeEntry)
        => nativeEntry ? PowerShellHybridNativeEntryEmitter.LauncherInvocation() : """
                        powerShell.AddScript(source, useLocalScope: false);
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
