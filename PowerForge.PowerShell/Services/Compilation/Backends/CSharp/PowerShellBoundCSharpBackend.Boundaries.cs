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
        => statements.Sum(CountHostedRegionSites);

    private static int CountHostedRegionSites(PowerShellLoweredStatement statement)
        => statement switch
        {
            PowerShellLoweredCommandRegionStatement => 1,
            PowerShellLoweredCommandCaptureStatement => 1,
            PowerShellLoweredAssignmentStatement assignment => CountHostedRegionSites(assignment.Value),
            PowerShellLoweredIndexAssignmentStatement assignment =>
                CountHostedRegionSites(assignment.Target) +
                CountHostedRegionSites(assignment.Index) +
                CountHostedRegionSites(assignment.Value),
            PowerShellLoweredClrMemberAssignmentStatement assignment =>
                (assignment.Receiver is null ? 0 : CountHostedRegionSites(assignment.Receiver)) + CountHostedRegionSites(assignment.Value),
            PowerShellLoweredReturnStatement { Expression: not null } returned => CountHostedRegionSites(returned.Expression),
            PowerShellLoweredExpressionStatement expression => CountHostedRegionSites(expression.Expression),
            PowerShellLoweredStreamWriteStatement stream => CountHostedRegionSites(stream.Message),
            PowerShellLoweredIfStatement conditional =>
                conditional.Clauses.Sum(static clause =>
                    CountHostedRegionSites(clause.Condition) + CountHostedRegionSites(clause.Statements)) +
                (conditional.ElseStatements is null ? 0 : CountHostedRegionSites(conditional.ElseStatements.Value)),
            PowerShellLoweredWhileStatement loop =>
                CountHostedRegionSites(loop.Condition) + CountHostedRegionSites(loop.Statements),
            PowerShellLoweredForStatement loop =>
                (loop.Initializer is null ? 0 : CountHostedRegionSites(loop.Initializer)) +
                (loop.Condition is null ? 0 : CountHostedRegionSites(loop.Condition)) +
                (loop.Iterator is null ? 0 : CountHostedRegionSites(loop.Iterator)) +
                CountHostedRegionSites(loop.Statements),
            PowerShellLoweredForEachStatement loop =>
                CountHostedRegionSites(loop.Collection) +
                (loop.NullCollectionElement is null ? 0 : CountHostedRegionSites(loop.NullCollectionElement)) +
                CountHostedRegionSites(loop.Statements),
            PowerShellLoweredSwitchStatement selected =>
                CountHostedRegionSites(selected.Value) +
                selected.Clauses.Sum(static clause =>
                    CountHostedRegionSites(clause.Value) + CountHostedRegionSites(clause.Statements)) +
                (selected.DefaultStatements is null ? 0 : CountHostedRegionSites(selected.DefaultStatements.Value)),
            PowerShellLoweredThrowStatement { Expression: not null } thrown => CountHostedRegionSites(thrown.Expression),
            PowerShellLoweredTryStatement attempted =>
                CountHostedRegionSites(attempted.Statements) +
                attempted.Catches.Sum(static clause => CountHostedRegionSites(clause.Statements)) +
                (attempted.FinallyStatements is null ? 0 : CountHostedRegionSites(attempted.FinallyStatements.Value)),
            _ => 0
        };

    private static int CountHostedRegionSites(PowerShellLoweredExpression expression)
        => expression switch
        {
            PowerShellLoweredCommandAvailabilityExpression discovery => 1 + CountHostedRegionSites(discovery.Name),
            PowerShellLoweredConversionExpression conversion => CountHostedRegionSites(conversion.Operand),
            PowerShellLoweredBinaryExpression binary => CountHostedRegionSites(binary.Left) + CountHostedRegionSites(binary.Right),
            PowerShellLoweredUnaryExpression unary => CountHostedRegionSites(unary.Operand),
            PowerShellLoweredTypeTestExpression typeTest => CountHostedRegionSites(typeTest.Operand),
            PowerShellLoweredRegexExpression regex =>
                CountHostedRegionSites(regex.Input) + CountHostedRegionSites(regex.Pattern) +
                (regex.Replacement is null ? 0 : CountHostedRegionSites(regex.Replacement)),
            PowerShellLoweredWildcardExpression wildcard =>
                CountHostedRegionSites(wildcard.Input) + CountHostedRegionSites(wildcard.Pattern),
            PowerShellLoweredMembershipExpression membership =>
                CountHostedRegionSites(membership.Left) + CountHostedRegionSites(membership.Right),
            PowerShellLoweredStringSplitExpression split =>
                CountHostedRegionSites(split.Input) + CountHostedRegionSites(split.Pattern),
            PowerShellLoweredStringJoinExpression join =>
                CountHostedRegionSites(join.Values) + CountHostedRegionSites(join.Separator),
            PowerShellLoweredInterpolatedStringExpression interpolated => interpolated.Parts.Sum(static part =>
                part.Expression is null ? 0 : CountHostedRegionSites(part.Expression)),
            PowerShellLoweredMutationExpression mutation =>
                mutation.Value is null ? 0 : CountHostedRegionSites(mutation.Value),
            PowerShellLoweredArrayExpression array => array.Elements.Sum(CountHostedRegionSites),
            PowerShellLoweredArrayConcatenationExpression concatenation =>
                CountHostedRegionSites(concatenation.Left) + CountHostedRegionSites(concatenation.Right),
            PowerShellLoweredDictionaryExpression dictionary => dictionary.Entries.Sum(static entry =>
                CountHostedRegionSites(entry.Key) + CountHostedRegionSites(entry.Value)),
            PowerShellLoweredPowerShellObjectExpression powerShellObject => powerShellObject.Properties.Sum(static property =>
                CountHostedRegionSites(property.Value)),
            PowerShellLoweredIndexExpression index =>
                CountHostedRegionSites(index.Target) + CountHostedRegionSites(index.Index),
            PowerShellLoweredClrMemberExpression { Receiver: not null } member => CountHostedRegionSites(member.Receiver),
            PowerShellLoweredClrInvocationExpression invocation =>
                (invocation.Receiver is null ? 0 : CountHostedRegionSites(invocation.Receiver)) +
                invocation.Arguments.Sum(CountHostedRegionSites),
            PowerShellLoweredInvocationExpression invocation =>
                (invocation.RequiresPowerShellCommandRegions ? 1 : 0) + invocation.Arguments.Sum(CountHostedRegionSites),
            _ => 0
        };

    private static bool ContainsNonDiscoveryHostedBoundary(IEnumerable<PowerShellLoweredStatement> statements)
        => statements.Any(ContainsNonDiscoveryHostedBoundary);

    private static bool ContainsNonDiscoveryHostedBoundary(PowerShellLoweredStatement statement)
        => statement switch
        {
            PowerShellLoweredCommandRegionStatement or PowerShellLoweredCommandCaptureStatement => true,
            PowerShellLoweredAssignmentStatement assignment => ContainsCommandRegionLocalInvocation(assignment.Value),
            PowerShellLoweredIndexAssignmentStatement assignment =>
                ContainsCommandRegionLocalInvocation(assignment.Target) ||
                ContainsCommandRegionLocalInvocation(assignment.Index) ||
                ContainsCommandRegionLocalInvocation(assignment.Value),
            PowerShellLoweredClrMemberAssignmentStatement assignment =>
                (assignment.Receiver is not null && ContainsCommandRegionLocalInvocation(assignment.Receiver)) ||
                ContainsCommandRegionLocalInvocation(assignment.Value),
            PowerShellLoweredReturnStatement { Expression: not null } returned => ContainsCommandRegionLocalInvocation(returned.Expression),
            PowerShellLoweredExpressionStatement expression => ContainsCommandRegionLocalInvocation(expression.Expression),
            PowerShellLoweredStreamWriteStatement stream => ContainsCommandRegionLocalInvocation(stream.Message),
            PowerShellLoweredIfStatement conditional =>
                conditional.Clauses.Any(static clause =>
                    ContainsCommandRegionLocalInvocation(clause.Condition) || ContainsNonDiscoveryHostedBoundary(clause.Statements)) ||
                conditional.ElseStatements is not null && ContainsNonDiscoveryHostedBoundary(conditional.ElseStatements.Value),
            PowerShellLoweredWhileStatement loop =>
                ContainsCommandRegionLocalInvocation(loop.Condition) || ContainsNonDiscoveryHostedBoundary(loop.Statements),
            PowerShellLoweredForStatement loop =>
                loop.Initializer is not null && ContainsCommandRegionLocalInvocation(loop.Initializer) ||
                loop.Condition is not null && ContainsCommandRegionLocalInvocation(loop.Condition) ||
                loop.Iterator is not null && ContainsCommandRegionLocalInvocation(loop.Iterator) ||
                ContainsNonDiscoveryHostedBoundary(loop.Statements),
            PowerShellLoweredForEachStatement loop =>
                ContainsCommandRegionLocalInvocation(loop.Collection) ||
                loop.NullCollectionElement is not null && ContainsCommandRegionLocalInvocation(loop.NullCollectionElement) ||
                ContainsNonDiscoveryHostedBoundary(loop.Statements),
            PowerShellLoweredSwitchStatement selected =>
                ContainsCommandRegionLocalInvocation(selected.Value) ||
                selected.Clauses.Any(static clause =>
                    ContainsCommandRegionLocalInvocation(clause.Value) || ContainsNonDiscoveryHostedBoundary(clause.Statements)) ||
                selected.DefaultStatements is not null && ContainsNonDiscoveryHostedBoundary(selected.DefaultStatements.Value),
            PowerShellLoweredThrowStatement { Expression: not null } thrown => ContainsCommandRegionLocalInvocation(thrown.Expression),
            PowerShellLoweredTryStatement attempted =>
                ContainsNonDiscoveryHostedBoundary(attempted.Statements) ||
                attempted.Catches.Any(static clause => ContainsNonDiscoveryHostedBoundary(clause.Statements)) ||
                attempted.FinallyStatements is not null && ContainsNonDiscoveryHostedBoundary(attempted.FinallyStatements.Value),
            _ => false
        };

    private static bool ContainsCommandRegionLocalInvocation(PowerShellLoweredExpression expression)
        => expression switch
        {
            PowerShellLoweredCommandAvailabilityExpression discovery => ContainsCommandRegionLocalInvocation(discovery.Name),
            PowerShellLoweredConversionExpression conversion => ContainsCommandRegionLocalInvocation(conversion.Operand),
            PowerShellLoweredBinaryExpression binary =>
                ContainsCommandRegionLocalInvocation(binary.Left) || ContainsCommandRegionLocalInvocation(binary.Right),
            PowerShellLoweredUnaryExpression unary => ContainsCommandRegionLocalInvocation(unary.Operand),
            PowerShellLoweredTypeTestExpression typeTest => ContainsCommandRegionLocalInvocation(typeTest.Operand),
            PowerShellLoweredRegexExpression regex =>
                ContainsCommandRegionLocalInvocation(regex.Input) ||
                ContainsCommandRegionLocalInvocation(regex.Pattern) ||
                regex.Replacement is not null && ContainsCommandRegionLocalInvocation(regex.Replacement),
            PowerShellLoweredWildcardExpression wildcard =>
                ContainsCommandRegionLocalInvocation(wildcard.Input) || ContainsCommandRegionLocalInvocation(wildcard.Pattern),
            PowerShellLoweredMembershipExpression membership =>
                ContainsCommandRegionLocalInvocation(membership.Left) || ContainsCommandRegionLocalInvocation(membership.Right),
            PowerShellLoweredStringSplitExpression split =>
                ContainsCommandRegionLocalInvocation(split.Input) || ContainsCommandRegionLocalInvocation(split.Pattern),
            PowerShellLoweredStringJoinExpression join =>
                ContainsCommandRegionLocalInvocation(join.Values) || ContainsCommandRegionLocalInvocation(join.Separator),
            PowerShellLoweredInterpolatedStringExpression interpolated => interpolated.Parts.Any(static part =>
                part.Expression is not null && ContainsCommandRegionLocalInvocation(part.Expression)),
            PowerShellLoweredMutationExpression mutation =>
                mutation.Value is not null && ContainsCommandRegionLocalInvocation(mutation.Value),
            PowerShellLoweredArrayExpression array => array.Elements.Any(ContainsCommandRegionLocalInvocation),
            PowerShellLoweredArrayConcatenationExpression concatenation =>
                ContainsCommandRegionLocalInvocation(concatenation.Left) || ContainsCommandRegionLocalInvocation(concatenation.Right),
            PowerShellLoweredDictionaryExpression dictionary => dictionary.Entries.Any(static entry =>
                ContainsCommandRegionLocalInvocation(entry.Key) || ContainsCommandRegionLocalInvocation(entry.Value)),
            PowerShellLoweredPowerShellObjectExpression powerShellObject => powerShellObject.Properties.Any(static property =>
                ContainsCommandRegionLocalInvocation(property.Value)),
            PowerShellLoweredIndexExpression index =>
                ContainsCommandRegionLocalInvocation(index.Target) || ContainsCommandRegionLocalInvocation(index.Index),
            PowerShellLoweredClrMemberExpression { Receiver: not null } member => ContainsCommandRegionLocalInvocation(member.Receiver),
            PowerShellLoweredClrInvocationExpression invocation =>
                invocation.Receiver is not null && ContainsCommandRegionLocalInvocation(invocation.Receiver) ||
                invocation.Arguments.Any(ContainsCommandRegionLocalInvocation),
            PowerShellLoweredInvocationExpression invocation =>
                invocation.RequiresPowerShellCommandRegions || invocation.Arguments.Any(ContainsCommandRegionLocalInvocation),
            _ => false
        };
}
