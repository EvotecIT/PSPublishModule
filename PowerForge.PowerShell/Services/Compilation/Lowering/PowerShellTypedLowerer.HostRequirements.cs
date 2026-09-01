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

    private static bool ContainsBoundParameterPresence(PowerShellBoundBlock block)
        => block.Statements.Any(StatementContainsBoundParameterPresence);

    private static bool StatementContainsBoundParameterPresence(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundAssignmentStatement assignment => ExpressionContainsBoundParameterPresence(assignment.Value),
            PowerShellBoundIndexAssignmentStatement assignment => ExpressionContainsBoundParameterPresence(assignment.Target) || ExpressionContainsBoundParameterPresence(assignment.Index) || ExpressionContainsBoundParameterPresence(assignment.Value),
            PowerShellBoundClrMemberAssignmentStatement assignment =>
                (assignment.Receiver is not null && ExpressionContainsBoundParameterPresence(assignment.Receiver)) ||
                ExpressionContainsBoundParameterPresence(assignment.Value),
            PowerShellBoundReturnStatement returned => returned.Expression is not null && ExpressionContainsBoundParameterPresence(returned.Expression),
            PowerShellBoundExpressionStatement expression => ExpressionContainsBoundParameterPresence(expression.Expression),
            PowerShellBoundIfStatement conditional => conditional.Clauses.Any(clause => ExpressionContainsBoundParameterPresence(clause.Condition) || ContainsBoundParameterPresence(clause.Body)) || conditional.ElseBlock is not null && ContainsBoundParameterPresence(conditional.ElseBlock),
            PowerShellBoundWhileStatement loop => ExpressionContainsBoundParameterPresence(loop.Condition) || ContainsBoundParameterPresence(loop.Body),
            PowerShellBoundForStatement loop => (loop.Initializer is not null && ExpressionContainsBoundParameterPresence(loop.Initializer)) || (loop.Condition is not null && ExpressionContainsBoundParameterPresence(loop.Condition)) || (loop.Iterator is not null && ExpressionContainsBoundParameterPresence(loop.Iterator)) || ContainsBoundParameterPresence(loop.Body),
            PowerShellBoundForEachStatement loop =>
                ExpressionContainsBoundParameterPresence(loop.Collection) ||
                (loop.NullCollectionElement is not null && ExpressionContainsBoundParameterPresence(loop.NullCollectionElement)) ||
                ContainsBoundParameterPresence(loop.Body),
            PowerShellBoundSwitchStatement switchStatement => ExpressionContainsBoundParameterPresence(switchStatement.Value) || switchStatement.Clauses.Any(clause => ExpressionContainsBoundParameterPresence(clause.Value) || ContainsBoundParameterPresence(clause.Body)) || switchStatement.DefaultBlock is not null && ContainsBoundParameterPresence(switchStatement.DefaultBlock),
            PowerShellBoundThrowStatement thrown => thrown.Expression is not null && ExpressionContainsBoundParameterPresence(thrown.Expression),
            PowerShellBoundTryStatement tryStatement => ContainsBoundParameterPresence(tryStatement.Body) || tryStatement.Catches.Any(clause => ContainsBoundParameterPresence(clause.Body)) || tryStatement.FinallyBlock is not null && ContainsBoundParameterPresence(tryStatement.FinallyBlock),
            _ => false
        };

    private static bool ExpressionContainsBoundParameterPresence(PowerShellBoundExpression expression)
        => expression switch
        {
            PowerShellBoundParameterPresenceExpression => true,
            PowerShellBoundConversionExpression conversion => ExpressionContainsBoundParameterPresence(conversion.Operand),
            PowerShellBoundBinaryExpression binary => ExpressionContainsBoundParameterPresence(binary.Left) || ExpressionContainsBoundParameterPresence(binary.Right),
            PowerShellBoundUnaryExpression unary => ExpressionContainsBoundParameterPresence(unary.Operand),
            PowerShellBoundTypeTestExpression typeTest => ExpressionContainsBoundParameterPresence(typeTest.Operand),
            PowerShellBoundRegexExpression regex => ExpressionContainsBoundParameterPresence(regex.Input) || ExpressionContainsBoundParameterPresence(regex.Pattern) || regex.Replacement is not null && ExpressionContainsBoundParameterPresence(regex.Replacement),
            PowerShellBoundWildcardExpression wildcard => ExpressionContainsBoundParameterPresence(wildcard.Input) || ExpressionContainsBoundParameterPresence(wildcard.Pattern),
            PowerShellBoundMembershipExpression membership => ExpressionContainsBoundParameterPresence(membership.Left) || ExpressionContainsBoundParameterPresence(membership.Right),
            PowerShellBoundStringSplitExpression split => ExpressionContainsBoundParameterPresence(split.Input) || ExpressionContainsBoundParameterPresence(split.Pattern),
            PowerShellBoundStringJoinExpression join => ExpressionContainsBoundParameterPresence(join.Values) || ExpressionContainsBoundParameterPresence(join.Separator),
            PowerShellBoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part => part.Expression is not null && ExpressionContainsBoundParameterPresence(part.Expression)),
            PowerShellBoundMutationExpression mutation => mutation.Value is not null && ExpressionContainsBoundParameterPresence(mutation.Value),
            PowerShellBoundArrayExpression array => array.Elements.Any(ExpressionContainsBoundParameterPresence),
            PowerShellBoundArrayConcatenationExpression concatenation => ExpressionContainsBoundParameterPresence(concatenation.Left) || ExpressionContainsBoundParameterPresence(concatenation.Right),
            PowerShellBoundDictionaryExpression dictionary => dictionary.Entries.Any(entry => ExpressionContainsBoundParameterPresence(entry.Key) || ExpressionContainsBoundParameterPresence(entry.Value)),
            PowerShellBoundPowerShellObjectExpression powerShellObject => powerShellObject.Properties.Any(property => ExpressionContainsBoundParameterPresence(property.Value)),
            PowerShellBoundIndexExpression index => ExpressionContainsBoundParameterPresence(index.Target) || ExpressionContainsBoundParameterPresence(index.Index),
            PowerShellBoundClrMemberExpression member => member.Receiver is not null && ExpressionContainsBoundParameterPresence(member.Receiver),
            PowerShellBoundClrInvocationExpression invocation => invocation.Receiver is not null && ExpressionContainsBoundParameterPresence(invocation.Receiver) || invocation.Arguments.Any(ExpressionContainsBoundParameterPresence),
            PowerShellBoundInvocationExpression invocation => invocation.Arguments.Any(ExpressionContainsBoundParameterPresence),
            _ => false
        };

    private static bool RequiresRuntimeStateHostBinding(PowerShellBoundBlock block)
        => block.Statements.Any(StatementRequiresRuntimeStateHostBinding);

    private static bool StatementRequiresRuntimeStateHostBinding(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundAssignmentStatement assignment => ExpressionRequiresRuntimeStateHostBinding(assignment.Value),
            PowerShellBoundIndexAssignmentStatement assignment =>
                ExpressionRequiresRuntimeStateHostBinding(assignment.Target) ||
                ExpressionRequiresRuntimeStateHostBinding(assignment.Index) ||
                ExpressionRequiresRuntimeStateHostBinding(assignment.Value),
            PowerShellBoundClrMemberAssignmentStatement assignment =>
                (assignment.Receiver is not null && ExpressionRequiresRuntimeStateHostBinding(assignment.Receiver)) ||
                ExpressionRequiresRuntimeStateHostBinding(assignment.Value),
            PowerShellBoundReturnStatement returned => returned.Expression is not null && ExpressionRequiresRuntimeStateHostBinding(returned.Expression),
            PowerShellBoundExpressionStatement expression => ExpressionRequiresRuntimeStateHostBinding(expression.Expression),
            PowerShellBoundIfStatement conditional => conditional.Clauses.Any(clause =>
                    ExpressionRequiresRuntimeStateHostBinding(clause.Condition) || RequiresRuntimeStateHostBinding(clause.Body)) ||
                (conditional.ElseBlock is not null && RequiresRuntimeStateHostBinding(conditional.ElseBlock)),
            PowerShellBoundWhileStatement loop => ExpressionRequiresRuntimeStateHostBinding(loop.Condition) || RequiresRuntimeStateHostBinding(loop.Body),
            PowerShellBoundForStatement loop =>
                (loop.Initializer is not null && ExpressionRequiresRuntimeStateHostBinding(loop.Initializer)) ||
                (loop.Condition is not null && ExpressionRequiresRuntimeStateHostBinding(loop.Condition)) ||
                (loop.Iterator is not null && ExpressionRequiresRuntimeStateHostBinding(loop.Iterator)) ||
                RequiresRuntimeStateHostBinding(loop.Body),
            PowerShellBoundForEachStatement loop =>
                ExpressionRequiresRuntimeStateHostBinding(loop.Collection) ||
                (loop.NullCollectionElement is not null && ExpressionRequiresRuntimeStateHostBinding(loop.NullCollectionElement)) ||
                RequiresRuntimeStateHostBinding(loop.Body),
            PowerShellBoundSwitchStatement switchStatement =>
                ExpressionRequiresRuntimeStateHostBinding(switchStatement.Value) ||
                switchStatement.Clauses.Any(clause => ExpressionRequiresRuntimeStateHostBinding(clause.Value) || RequiresRuntimeStateHostBinding(clause.Body)) ||
                (switchStatement.DefaultBlock is not null && RequiresRuntimeStateHostBinding(switchStatement.DefaultBlock)),
            PowerShellBoundThrowStatement thrown => thrown.Expression is not null && ExpressionRequiresRuntimeStateHostBinding(thrown.Expression),
            PowerShellBoundTryStatement tryStatement =>
                RequiresRuntimeStateHostBinding(tryStatement.Body) ||
                tryStatement.Catches.Any(clause => RequiresRuntimeStateHostBinding(clause.Body)) ||
                (tryStatement.FinallyBlock is not null && RequiresRuntimeStateHostBinding(tryStatement.FinallyBlock)),
            _ => false
        };

    private static bool ExpressionRequiresRuntimeStateHostBinding(PowerShellBoundExpression expression)
        => expression switch
        {
            PowerShellBoundRuntimeStateExpression runtime => runtime.RequiresHostBinding,
            PowerShellBoundCommandAvailabilityExpression discovery => ExpressionRequiresRuntimeStateHostBinding(discovery.Name),
            PowerShellBoundConversionExpression conversion => ExpressionRequiresRuntimeStateHostBinding(conversion.Operand),
            PowerShellBoundBinaryExpression binary => ExpressionRequiresRuntimeStateHostBinding(binary.Left) || ExpressionRequiresRuntimeStateHostBinding(binary.Right),
            PowerShellBoundUnaryExpression unary => ExpressionRequiresRuntimeStateHostBinding(unary.Operand),
            PowerShellBoundTypeTestExpression typeTest => ExpressionRequiresRuntimeStateHostBinding(typeTest.Operand),
            PowerShellBoundRegexExpression regex =>
                ExpressionRequiresRuntimeStateHostBinding(regex.Input) ||
                ExpressionRequiresRuntimeStateHostBinding(regex.Pattern) ||
                (regex.Replacement is not null && ExpressionRequiresRuntimeStateHostBinding(regex.Replacement)),
            PowerShellBoundWildcardExpression wildcard => ExpressionRequiresRuntimeStateHostBinding(wildcard.Input) || ExpressionRequiresRuntimeStateHostBinding(wildcard.Pattern),
            PowerShellBoundMembershipExpression membership => ExpressionRequiresRuntimeStateHostBinding(membership.Left) || ExpressionRequiresRuntimeStateHostBinding(membership.Right),
            PowerShellBoundStringSplitExpression split => ExpressionRequiresRuntimeStateHostBinding(split.Input) || ExpressionRequiresRuntimeStateHostBinding(split.Pattern),
            PowerShellBoundStringJoinExpression join => ExpressionRequiresRuntimeStateHostBinding(join.Values) || ExpressionRequiresRuntimeStateHostBinding(join.Separator),
            PowerShellBoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part => part.Expression is not null && ExpressionRequiresRuntimeStateHostBinding(part.Expression)),
            PowerShellBoundMutationExpression mutation => mutation.Value is not null && ExpressionRequiresRuntimeStateHostBinding(mutation.Value),
            PowerShellBoundArrayExpression array => array.Elements.Any(ExpressionRequiresRuntimeStateHostBinding),
            PowerShellBoundArrayConcatenationExpression concatenation => ExpressionRequiresRuntimeStateHostBinding(concatenation.Left) || ExpressionRequiresRuntimeStateHostBinding(concatenation.Right),
            PowerShellBoundDictionaryExpression dictionary => dictionary.Entries.Any(entry =>
                ExpressionRequiresRuntimeStateHostBinding(entry.Key) || ExpressionRequiresRuntimeStateHostBinding(entry.Value)),
            PowerShellBoundPowerShellObjectExpression powerShellObject => powerShellObject.Properties.Any(property => ExpressionRequiresRuntimeStateHostBinding(property.Value)),
            PowerShellBoundIndexExpression index => ExpressionRequiresRuntimeStateHostBinding(index.Target) || ExpressionRequiresRuntimeStateHostBinding(index.Index),
            PowerShellBoundClrMemberExpression member => member.Receiver is not null && ExpressionRequiresRuntimeStateHostBinding(member.Receiver),
            PowerShellBoundClrInvocationExpression invocation =>
                (invocation.Receiver is not null && ExpressionRequiresRuntimeStateHostBinding(invocation.Receiver)) ||
                invocation.Arguments.Any(ExpressionRequiresRuntimeStateHostBinding),
            PowerShellBoundInvocationExpression invocation => invocation.Arguments.Any(ExpressionRequiresRuntimeStateHostBinding),
            _ => false
        };

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
