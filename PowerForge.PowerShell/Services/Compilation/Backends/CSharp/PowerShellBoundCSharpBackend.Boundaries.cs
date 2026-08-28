namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static int CountHostedRegionSites(IEnumerable<PowerShellLoweredStatement> statements)
        => statements.Sum(CountHostedRegionSites);

    private static int CountHostedRegionSites(PowerShellLoweredStatement statement)
        => statement switch
        {
            PowerShellLoweredCommandRegionStatement => 1,
            PowerShellLoweredCommandCaptureStatement => 1,
            PowerShellLoweredIfStatement conditional =>
                conditional.Clauses.Sum(static clause => CountHostedRegionSites(clause.Statements)) +
                (conditional.ElseStatements is null ? 0 : CountHostedRegionSites(conditional.ElseStatements.Value)),
            PowerShellLoweredWhileStatement loop => CountHostedRegionSites(loop.Statements),
            PowerShellLoweredForStatement loop => CountHostedRegionSites(loop.Statements),
            PowerShellLoweredForEachStatement loop => CountHostedRegionSites(loop.Statements),
            PowerShellLoweredSwitchStatement selected =>
                selected.Clauses.Sum(static clause => CountHostedRegionSites(clause.Statements)) +
                (selected.DefaultStatements is null ? 0 : CountHostedRegionSites(selected.DefaultStatements.Value)),
            PowerShellLoweredTryStatement attempted =>
                CountHostedRegionSites(attempted.Statements) +
                attempted.Catches.Sum(static clause => CountHostedRegionSites(clause.Statements)) +
                (attempted.FinallyStatements is null ? 0 : CountHostedRegionSites(attempted.FinallyStatements.Value)),
            _ => 0
        };
}
