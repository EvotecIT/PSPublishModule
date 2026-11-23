namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static bool ContainsDiscardValue(IEnumerable<PowerShellLoweredStatement> statements)
        => PowerShellLoweredTreeEnumerator.EnumerateStatements(statements)
            .Any(static statement => statement is PowerShellLoweredExpressionStatement { DiscardValue: true });

    private static bool ContainsNonQueryHostedBoundary(IEnumerable<PowerShellLoweredStatement> statements)
        => PowerShellLoweredTreeEnumerator.EnumerateStatements(statements).Any(static statement =>
               statement is PowerShellLoweredCommandRegionStatement or PowerShellLoweredCommandCaptureStatement) ||
           PowerShellLoweredTreeEnumerator.EnumerateExpressions(statements)
               .Any(static expression => expression is PowerShellLoweredInvocationExpression { RequiresPowerShellCommandRegions: true });
}
