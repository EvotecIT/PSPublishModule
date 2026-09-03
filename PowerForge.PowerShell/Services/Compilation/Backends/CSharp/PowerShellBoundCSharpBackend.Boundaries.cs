namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static bool ContainsDiscardValue(IEnumerable<PowerShellLoweredStatement> statements)
        => statements.Any(ContainsDiscardValue);

    private static bool ContainsDiscardValue(PowerShellLoweredStatement statement)
        => statement switch
        {
            PowerShellLoweredExpressionStatement { DiscardValue: true } => true,
            PowerShellLoweredIfStatement conditional =>
                conditional.Clauses.Any(static clause => ContainsDiscardValue(clause.Statements)) ||
                conditional.ElseStatements is not null && ContainsDiscardValue(conditional.ElseStatements.Value),
            PowerShellLoweredWhileStatement loop => ContainsDiscardValue(loop.Statements),
            PowerShellLoweredForStatement loop => ContainsDiscardValue(loop.Statements),
            PowerShellLoweredForEachStatement loop => ContainsDiscardValue(loop.Statements),
            PowerShellLoweredSwitchStatement selected =>
                selected.Clauses.Any(static clause => ContainsDiscardValue(clause.Statements)) ||
                selected.DefaultStatements is not null && ContainsDiscardValue(selected.DefaultStatements.Value),
            PowerShellLoweredTryStatement attempted =>
                ContainsDiscardValue(attempted.Statements) ||
                attempted.Catches.Any(static clause => ContainsDiscardValue(clause.Statements)) ||
                attempted.FinallyStatements is not null && ContainsDiscardValue(attempted.FinallyStatements.Value),
            _ => false
        };

    private static int CountHostedRegionSites(IEnumerable<PowerShellLoweredStatement> statements)
        => PowerShellLoweredTreeEnumerator.EnumerateStatements(statements).Count(static statement =>
               statement is PowerShellLoweredCommandRegionStatement or PowerShellLoweredCommandCaptureStatement) +
           PowerShellLoweredTreeEnumerator.EnumerateExpressions(statements).Count(static expression =>
               expression is PowerShellLoweredCommandAvailabilityExpression or PowerShellLoweredHostedBooleanCommandExpression ||
               expression is PowerShellLoweredInvocationExpression { RequiresPowerShellCommandRegions: true });

    private static bool ContainsNonQueryHostedBoundary(IEnumerable<PowerShellLoweredStatement> statements)
        => PowerShellLoweredTreeEnumerator.EnumerateStatements(statements).Any(static statement =>
               statement is PowerShellLoweredCommandRegionStatement or PowerShellLoweredCommandCaptureStatement) ||
           PowerShellLoweredTreeEnumerator.EnumerateExpressions(statements)
               .Any(static expression => expression is PowerShellLoweredInvocationExpression { RequiresPowerShellCommandRegions: true });
}
