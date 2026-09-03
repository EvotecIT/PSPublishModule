namespace PowerForge;

internal static class PowerShellLoweredCommandProviderCollector
{
    internal static PowerShellCompilationCommandProviderContract[] Collect(
        IEnumerable<PowerShellLoweredStatement> statements)
    {
        var roots = statements.ToArray();
        return PowerShellLoweredTreeEnumerator.EnumerateStatements(roots)
            .SelectMany(GetStatementProviders)
            .Concat(PowerShellLoweredTreeEnumerator.EnumerateExpressions(roots).SelectMany(GetExpressionProviders))
            .GroupBy(static provider => provider.ProviderId + "\0" + provider.ProviderVersion + "\0" + provider.CommandName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal)
            .ThenBy(static provider => provider.ProviderVersion, StringComparer.Ordinal)
            .ThenBy(static provider => provider.CommandName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<PowerShellCompilationCommandProviderContract> GetStatementProviders(
        PowerShellLoweredStatement statement)
    {
        switch (statement)
        {
            case PowerShellLoweredStreamWriteStatement stream:
                yield return stream.Provider;
                break;
            case PowerShellLoweredCommandRegionStatement region:
                foreach (var stage in region.Stages) yield return stage.Provider;
                break;
            case PowerShellLoweredCommandCaptureStatement capture:
                foreach (var stage in capture.Stages) yield return stage.Provider;
                break;
        }
    }

    private static IEnumerable<PowerShellCompilationCommandProviderContract> GetExpressionProviders(
        PowerShellLoweredExpression expression)
    {
        switch (expression)
        {
            case PowerShellLoweredRuntimeStateExpression { Provider: not null } runtime:
                yield return runtime.Provider;
                break;
            case PowerShellLoweredCommandAvailabilityExpression discovery:
                yield return discovery.Provider;
                break;
            case PowerShellLoweredHostedBooleanCommandExpression hostedBoolean:
                yield return hostedBoolean.Provider;
                break;
        }
    }
}
