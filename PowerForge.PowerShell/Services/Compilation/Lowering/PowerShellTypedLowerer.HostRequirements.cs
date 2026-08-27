namespace PowerForge;

internal sealed partial class PowerShellTypedLowerer
{
    private static HashSet<string> PropagateHostRequirement(
        PowerShellBoundProgram program,
        Func<PowerShellBoundFunction, bool> hasDirectRequirement)
    {
        var required = program.Functions.Where(hasDirectRequirement)
            .Select(static function => function.Symbol.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var edge in program.CallGraph)
            {
                if (required.Contains(edge.Callee.StableKey) && required.Add(edge.Caller.StableKey)) changed = true;
            }
        } while (changed);
        return required;
    }

    private static bool ContainsPowerShellStreamWrite(PowerShellBoundBlock block)
        => block.Statements.Any(StatementContainsPowerShellStreamWrite);

    private static bool StatementContainsPowerShellStreamWrite(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundStreamWriteStatement => true,
            PowerShellBoundIfStatement conditional => conditional.Clauses.Any(clause => ContainsPowerShellStreamWrite(clause.Body)) ||
                (conditional.ElseBlock is not null && ContainsPowerShellStreamWrite(conditional.ElseBlock)),
            PowerShellBoundWhileStatement loop => ContainsPowerShellStreamWrite(loop.Body),
            PowerShellBoundForStatement loop => ContainsPowerShellStreamWrite(loop.Body),
            PowerShellBoundForEachStatement loop => ContainsPowerShellStreamWrite(loop.Body),
            PowerShellBoundSwitchStatement switchStatement => switchStatement.Clauses.Any(clause => ContainsPowerShellStreamWrite(clause.Body)) ||
                (switchStatement.DefaultBlock is not null && ContainsPowerShellStreamWrite(switchStatement.DefaultBlock)),
            PowerShellBoundTryStatement tryStatement => ContainsPowerShellStreamWrite(tryStatement.Body) ||
                tryStatement.Catches.Any(clause => ContainsPowerShellStreamWrite(clause.Body)) ||
                (tryStatement.FinallyBlock is not null && ContainsPowerShellStreamWrite(tryStatement.FinallyBlock)),
            _ => false
        };

    private static bool ContainsPowerShellCommandRegion(PowerShellBoundBlock block)
        => block.Statements.Any(StatementContainsPowerShellCommandRegion);

    private static bool StatementContainsPowerShellCommandRegion(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundCommandRegionStatement or PowerShellBoundCommandCaptureStatement => true,
            PowerShellBoundIfStatement conditional => conditional.Clauses.Any(clause => ContainsPowerShellCommandRegion(clause.Body)) ||
                (conditional.ElseBlock is not null && ContainsPowerShellCommandRegion(conditional.ElseBlock)),
            PowerShellBoundWhileStatement loop => ContainsPowerShellCommandRegion(loop.Body),
            PowerShellBoundForStatement loop => ContainsPowerShellCommandRegion(loop.Body),
            PowerShellBoundForEachStatement loop => ContainsPowerShellCommandRegion(loop.Body),
            PowerShellBoundSwitchStatement switchStatement => switchStatement.Clauses.Any(clause => ContainsPowerShellCommandRegion(clause.Body)) ||
                (switchStatement.DefaultBlock is not null && ContainsPowerShellCommandRegion(switchStatement.DefaultBlock)),
            PowerShellBoundTryStatement tryStatement => ContainsPowerShellCommandRegion(tryStatement.Body) ||
                tryStatement.Catches.Any(clause => ContainsPowerShellCommandRegion(clause.Body)) ||
                (tryStatement.FinallyBlock is not null && ContainsPowerShellCommandRegion(tryStatement.FinallyBlock)),
            _ => false
        };
}
