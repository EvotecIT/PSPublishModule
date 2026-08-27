namespace PowerForge;

internal static class PowerShellLoweredCommandProviderCollector
{
    internal static PowerShellCompilationCommandProviderContract[] Collect(IEnumerable<PowerShellLoweredStatement> statements)
        => Enumerate(statements)
            .GroupBy(static provider => provider.ProviderId + "\0" + provider.ProviderVersion, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal)
            .ThenBy(static provider => provider.ProviderVersion, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<PowerShellCompilationCommandProviderContract> Enumerate(IEnumerable<PowerShellLoweredStatement> statements)
    {
        foreach (var statement in statements)
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
                case PowerShellLoweredIfStatement conditional:
                    foreach (var provider in conditional.Clauses.SelectMany(static clause => Enumerate(clause.Statements))) yield return provider;
                    if (conditional.ElseStatements is not null)
                        foreach (var provider in Enumerate(conditional.ElseStatements.Value)) yield return provider;
                    break;
                case PowerShellLoweredWhileStatement loop:
                    foreach (var provider in Enumerate(loop.Statements)) yield return provider;
                    break;
                case PowerShellLoweredForStatement loop:
                    foreach (var provider in Enumerate(loop.Statements)) yield return provider;
                    break;
                case PowerShellLoweredForEachStatement loop:
                    foreach (var provider in Enumerate(loop.Statements)) yield return provider;
                    break;
                case PowerShellLoweredSwitchStatement switchStatement:
                    foreach (var provider in switchStatement.Clauses.SelectMany(static clause => Enumerate(clause.Statements))) yield return provider;
                    if (switchStatement.DefaultStatements is not null)
                        foreach (var provider in Enumerate(switchStatement.DefaultStatements.Value)) yield return provider;
                    break;
                case PowerShellLoweredTryStatement tryStatement:
                    foreach (var provider in Enumerate(tryStatement.Statements)) yield return provider;
                    foreach (var provider in tryStatement.Catches.SelectMany(static clause => Enumerate(clause.Statements))) yield return provider;
                    if (tryStatement.FinallyStatements is not null)
                        foreach (var provider in Enumerate(tryStatement.FinallyStatements.Value)) yield return provider;
                    break;
            }
        }
    }
}
