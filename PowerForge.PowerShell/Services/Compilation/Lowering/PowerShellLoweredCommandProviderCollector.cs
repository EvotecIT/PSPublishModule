namespace PowerForge;

internal static class PowerShellLoweredCommandProviderCollector
{
    internal static PowerShellCompilationCommandProviderContract[] Collect(IEnumerable<PowerShellLoweredStatement> statements)
        => Enumerate(statements)
            .GroupBy(static provider => provider.ProviderId + "\0" + provider.ProviderVersion + "\0" + provider.CommandName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal)
            .ThenBy(static provider => provider.ProviderVersion, StringComparer.Ordinal)
            .ThenBy(static provider => provider.CommandName, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<PowerShellCompilationCommandProviderContract> Enumerate(IEnumerable<PowerShellLoweredStatement> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case PowerShellLoweredAssignmentStatement assignment:
                    foreach (var provider in Enumerate(assignment.Value)) yield return provider;
                    break;
                case PowerShellLoweredIndexAssignmentStatement assignment:
                    foreach (var provider in Enumerate(assignment.Target)) yield return provider;
                    foreach (var provider in Enumerate(assignment.Index)) yield return provider;
                    foreach (var provider in Enumerate(assignment.Value)) yield return provider;
                    break;
                case PowerShellLoweredClrMemberAssignmentStatement assignment:
                    if (assignment.Receiver is not null)
                    foreach (var provider in Enumerate(assignment.Receiver)) yield return provider;
                    foreach (var provider in Enumerate(assignment.Value)) yield return provider;
                    break;
                case PowerShellLoweredReturnStatement returned when returned.Expression is not null:
                    foreach (var provider in Enumerate(returned.Expression)) yield return provider;
                    break;
                case PowerShellLoweredExpressionStatement expression:
                    foreach (var provider in Enumerate(expression.Expression)) yield return provider;
                    break;
                case PowerShellLoweredStreamWriteStatement stream:
                    yield return stream.Provider;
                    foreach (var provider in Enumerate(stream.Message)) yield return provider;
                    break;
                case PowerShellLoweredCommandRegionStatement region:
                    foreach (var stage in region.Stages) yield return stage.Provider;
                    break;
                case PowerShellLoweredCommandCaptureStatement capture:
                    foreach (var stage in capture.Stages) yield return stage.Provider;
                    break;
                case PowerShellLoweredIfStatement conditional:
                    foreach (var clause in conditional.Clauses)
                    {
                        foreach (var provider in Enumerate(clause.Condition)) yield return provider;
                        foreach (var provider in Enumerate(clause.Statements)) yield return provider;
                    }
                    if (conditional.ElseStatements is not null)
                        foreach (var provider in Enumerate(conditional.ElseStatements.Value)) yield return provider;
                    break;
                case PowerShellLoweredWhileStatement loop:
                    foreach (var provider in Enumerate(loop.Condition)) yield return provider;
                    foreach (var provider in Enumerate(loop.Statements)) yield return provider;
                    break;
                case PowerShellLoweredForStatement loop:
                    if (loop.Initializer is not null)
                        foreach (var provider in Enumerate(loop.Initializer)) yield return provider;
                    if (loop.Condition is not null)
                        foreach (var provider in Enumerate(loop.Condition)) yield return provider;
                    if (loop.Iterator is not null)
                        foreach (var provider in Enumerate(loop.Iterator)) yield return provider;
                    foreach (var provider in Enumerate(loop.Statements)) yield return provider;
                    break;
                case PowerShellLoweredForEachStatement loop:
                    foreach (var provider in Enumerate(loop.Collection)) yield return provider;
                    if (loop.NullCollectionElement is not null)
                        foreach (var provider in Enumerate(loop.NullCollectionElement)) yield return provider;
                    foreach (var provider in Enumerate(loop.Statements)) yield return provider;
                    break;
                case PowerShellLoweredSwitchStatement switchStatement:
                    foreach (var provider in Enumerate(switchStatement.Value)) yield return provider;
                    foreach (var clause in switchStatement.Clauses)
                    {
                        foreach (var provider in Enumerate(clause.Value)) yield return provider;
                        foreach (var provider in Enumerate(clause.Statements)) yield return provider;
                    }
                    if (switchStatement.DefaultStatements is not null)
                        foreach (var provider in Enumerate(switchStatement.DefaultStatements.Value)) yield return provider;
                    break;
                case PowerShellLoweredTryStatement tryStatement:
                    foreach (var provider in Enumerate(tryStatement.Statements)) yield return provider;
                    foreach (var provider in tryStatement.Catches.SelectMany(static clause => Enumerate(clause.Statements))) yield return provider;
                    if (tryStatement.FinallyStatements is not null)
                        foreach (var provider in Enumerate(tryStatement.FinallyStatements.Value)) yield return provider;
                    break;
                case PowerShellLoweredThrowStatement thrown when thrown.Expression is not null:
                    foreach (var provider in Enumerate(thrown.Expression)) yield return provider;
                    break;
            }
        }
    }

    private static IEnumerable<PowerShellCompilationCommandProviderContract> Enumerate(PowerShellLoweredExpression expression)
    {
        switch (expression)
        {
            case PowerShellLoweredRuntimeStateExpression runtime:
                if (runtime.Provider is not null) yield return runtime.Provider;
                foreach (var argument in runtime.Arguments)
                foreach (var provider in Enumerate(argument))
                    yield return provider;
                break;
            case PowerShellLoweredCommandAvailabilityExpression discovery:
                yield return discovery.Provider;
                foreach (var provider in Enumerate(discovery.Name)) yield return provider;
                break;
            case PowerShellLoweredHostedBooleanCommandExpression hostedBoolean:
                yield return hostedBoolean.Provider;
                foreach (var argument in hostedBoolean.Arguments.Where(static argument => argument.Value is not null))
                foreach (var provider in Enumerate(argument.Value!))
                    yield return provider;
                break;
            case PowerShellLoweredConversionExpression conversion:
                foreach (var provider in Enumerate(conversion.Operand)) yield return provider;
                break;
            case PowerShellLoweredBinaryExpression binary:
                foreach (var provider in Enumerate(binary.Left)) yield return provider;
                foreach (var provider in Enumerate(binary.Right)) yield return provider;
                break;
            case PowerShellLoweredUnaryExpression unary:
                foreach (var provider in Enumerate(unary.Operand)) yield return provider;
                break;
            case PowerShellLoweredTypeTestExpression typeTest:
                foreach (var provider in Enumerate(typeTest.Operand)) yield return provider;
                break;
            case PowerShellLoweredRegexExpression regex:
                foreach (var provider in Enumerate(regex.Input)) yield return provider;
                foreach (var provider in Enumerate(regex.Pattern)) yield return provider;
                if (regex.Replacement is not null)
                    foreach (var provider in Enumerate(regex.Replacement)) yield return provider;
                break;
            case PowerShellLoweredWildcardExpression wildcard:
                foreach (var provider in Enumerate(wildcard.Input)) yield return provider;
                foreach (var provider in Enumerate(wildcard.Pattern)) yield return provider;
                break;
            case PowerShellLoweredMembershipExpression membership:
                foreach (var provider in Enumerate(membership.Left)) yield return provider;
                foreach (var provider in Enumerate(membership.Right)) yield return provider;
                break;
            case PowerShellLoweredStringSplitExpression split:
                foreach (var provider in Enumerate(split.Input)) yield return provider;
                foreach (var provider in Enumerate(split.Pattern)) yield return provider;
                break;
            case PowerShellLoweredStringJoinExpression join:
                foreach (var provider in Enumerate(join.Values)) yield return provider;
                foreach (var provider in Enumerate(join.Separator)) yield return provider;
                break;
            case PowerShellLoweredInterpolatedStringExpression interpolated:
                foreach (var part in interpolated.Parts.Where(static part => part.Expression is not null))
                foreach (var provider in Enumerate(part.Expression!))
                    yield return provider;
                break;
            case PowerShellLoweredMutationExpression mutation when mutation.Value is not null:
                foreach (var provider in Enumerate(mutation.Value)) yield return provider;
                break;
            case PowerShellLoweredArrayExpression array:
                foreach (var element in array.Elements)
                foreach (var provider in Enumerate(element))
                    yield return provider;
                break;
            case PowerShellLoweredArrayConcatenationExpression concatenation:
                foreach (var provider in Enumerate(concatenation.Left)) yield return provider;
                foreach (var provider in Enumerate(concatenation.Right)) yield return provider;
                break;
            case PowerShellLoweredDictionaryExpression dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    foreach (var provider in Enumerate(entry.Key)) yield return provider;
                    foreach (var provider in Enumerate(entry.Value)) yield return provider;
                }
                break;
            case PowerShellLoweredPowerShellObjectExpression powerShellObject:
                foreach (var property in powerShellObject.Properties)
                foreach (var provider in Enumerate(property.Value))
                    yield return provider;
                break;
            case PowerShellLoweredIndexExpression index:
                foreach (var provider in Enumerate(index.Target)) yield return provider;
                foreach (var provider in Enumerate(index.Index)) yield return provider;
                break;
            case PowerShellLoweredClrMemberExpression { Receiver: not null } member:
                foreach (var provider in Enumerate(member.Receiver)) yield return provider;
                break;
            case PowerShellLoweredClrInvocationExpression invocation:
                if (invocation.Receiver is not null)
                    foreach (var provider in Enumerate(invocation.Receiver)) yield return provider;
                foreach (var argument in invocation.Arguments)
                foreach (var provider in Enumerate(argument))
                    yield return provider;
                break;
            case PowerShellLoweredInvocationExpression invocation:
                foreach (var argument in invocation.Arguments)
                foreach (var provider in Enumerate(argument))
                    yield return provider;
                break;
        }
    }
}
