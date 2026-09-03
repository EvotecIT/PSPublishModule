namespace PowerForge;

internal static class PowerShellLoweredModuleStateCollector
{
    internal static string[] Collect(IEnumerable<PowerShellLoweredStatement> statements)
        => GetReads(statements)
            .Select(static expression => expression.Arguments.FirstOrDefault())
            .OfType<PowerShellLoweredLiteralExpression>()
            .Select(static expression => expression.Value as string)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    internal static int CountReadSites(IEnumerable<PowerShellLoweredStatement> statements)
        => GetReads(statements).Count();

    private static IEnumerable<PowerShellLoweredRuntimeStateExpression> GetReads(
        IEnumerable<PowerShellLoweredStatement> statements)
        => PowerShellLoweredTreeEnumerator.EnumerateExpressions(statements)
            .OfType<PowerShellLoweredRuntimeStateExpression>()
            .Where(static expression => expression.Kind == PowerShellRuntimeStateIntrinsicKind.ModuleVariable);
}
