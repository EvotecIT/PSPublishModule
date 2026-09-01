namespace PowerForge;

internal sealed partial class PowerShellTypedLowerer
{
    private static readonly string[] StreamHostParameterNames =
    {
        "__writeOutput", "__writeVerbose", "__writeDebug", "__writeWarning",
        "__writeInformation", "__writeHost", "__writeError"
    };

    private static readonly string[] CommandRegionHostParameterNames =
    {
        "__invokePowerShellRegion", "__invokePowerShellCapture"
    };

    private static readonly string[] RuntimeStateHostParameterNames =
    {
        "__shouldProcessTarget", "__shouldProcessAction", "__psVersion",
        "__whatIfPreference", "__runtimeState"
    };

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

    private static string? FindGeneratedHostParameterCollision(
        PowerShellBoundFunction function,
        bool requiresBoundParameters,
        bool requiresPowerShellStreams,
        bool requiresProviderCancellation,
        bool requiresPowerShellCommandRegions,
        bool requiresPowerShellRuntimeState)
    {
        var generated = new HashSet<string>(StringComparer.Ordinal)
        {
            requiresProviderCancellation ? "__providerCancellationToken" : string.Empty,
            requiresBoundParameters ? "__boundParameters" : string.Empty
        };
        if (requiresPowerShellStreams) generated.UnionWith(StreamHostParameterNames);
        if (requiresPowerShellCommandRegions) generated.UnionWith(CommandRegionHostParameterNames);
        if (requiresPowerShellRuntimeState) generated.UnionWith(RuntimeStateHostParameterNames);
        generated.Remove(string.Empty);
        return function.Parameters
            .Select(static parameter => PowerShellCSharpSymbolRenderer.Identifier(parameter.Symbol.Name))
            .FirstOrDefault(generated.Contains);
    }

    private static bool ContainsPowerShellStreamWrite(PowerShellBoundBlock block)
        => block.Statements.Any(StatementContainsPowerShellStreamWrite);

    private static bool ContainsCooperativeProvider(PowerShellBoundBlock block)
        => block.Statements.Any(StatementContainsCooperativeProvider);

    private static bool StatementContainsCooperativeProvider(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundStreamWriteStatement stream =>
                stream.Provider.Adapter.Cancellation is
                    PowerShellCompilationProviderCancellation.Cooperative or
                    PowerShellCompilationProviderCancellation.PostInitializationCooperative or
                    PowerShellCompilationProviderCancellation.ProcessIsolated,
            PowerShellBoundIfStatement conditional => conditional.Clauses.Any(clause => ContainsCooperativeProvider(clause.Body)) ||
                conditional.ElseBlock is not null && ContainsCooperativeProvider(conditional.ElseBlock),
            PowerShellBoundWhileStatement loop => ContainsCooperativeProvider(loop.Body),
            PowerShellBoundForStatement loop => ContainsCooperativeProvider(loop.Body),
            PowerShellBoundForEachStatement loop => ContainsCooperativeProvider(loop.Body),
            PowerShellBoundSwitchStatement switchStatement => switchStatement.Clauses.Any(clause => ContainsCooperativeProvider(clause.Body)) ||
                switchStatement.DefaultBlock is not null && ContainsCooperativeProvider(switchStatement.DefaultBlock),
            PowerShellBoundTryStatement tryStatement => ContainsCooperativeProvider(tryStatement.Body) ||
                tryStatement.Catches.Any(clause => ContainsCooperativeProvider(clause.Body)) ||
                tryStatement.FinallyBlock is not null && ContainsCooperativeProvider(tryStatement.FinallyBlock),
            _ => false
        };

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
        => PowerShellSemanticAnalyzer.EnumerateStatements(block).Any(static statement =>
               statement is PowerShellBoundCommandRegionStatement or PowerShellBoundCommandCaptureStatement) ||
           PowerShellSemanticAnalyzer.EnumerateStatements(block)
               .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
               .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
               .Any(static expression => expression is PowerShellBoundCommandAvailabilityExpression);
}
