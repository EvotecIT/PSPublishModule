namespace PowerForge;

/// <summary>
/// Enumerates canonical lowered statements and expressions so metadata collectors do not grow
/// independent, incomplete copies of the lowered tree shape.
/// </summary>
internal static class PowerShellLoweredTreeEnumerator
{
    internal static IEnumerable<PowerShellLoweredStatement> EnumerateStatements(
        IEnumerable<PowerShellLoweredStatement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;
            foreach (var nested in EnumerateNestedStatements(statement))
                yield return nested;
        }
    }

    internal static IEnumerable<PowerShellLoweredExpression> EnumerateExpressions(
        IEnumerable<PowerShellLoweredStatement> statements)
    {
        foreach (var statement in EnumerateStatements(statements))
        foreach (var expression in EnumerateDirectExpressions(statement))
        foreach (var descendant in EnumerateExpressionTree(expression))
            yield return descendant;
    }

    private static IEnumerable<PowerShellLoweredStatement> EnumerateNestedStatements(
        PowerShellLoweredStatement statement)
    {
        IEnumerable<PowerShellLoweredStatement> nested = statement switch
        {
            PowerShellLoweredIfStatement conditional => conditional.Clauses
                .SelectMany(static clause => clause.Statements)
                .Concat(conditional.ElseStatements is null
                    ? Array.Empty<PowerShellLoweredStatement>()
                    : conditional.ElseStatements.Value),
            PowerShellLoweredWhileStatement loop => loop.Statements,
            PowerShellLoweredForStatement loop => loop.Statements,
            PowerShellLoweredForEachStatement loop => loop.Statements,
            PowerShellLoweredSwitchStatement selected => selected.Clauses
                .SelectMany(static clause => clause.Statements)
                .Concat(selected.DefaultStatements is null
                    ? Array.Empty<PowerShellLoweredStatement>()
                    : selected.DefaultStatements.Value),
            PowerShellLoweredTryStatement attempted => attempted.Statements
                .Concat(attempted.Catches.SelectMany(static clause => clause.Statements))
                .Concat(attempted.FinallyStatements is null
                    ? Array.Empty<PowerShellLoweredStatement>()
                    : attempted.FinallyStatements.Value),
            _ => Array.Empty<PowerShellLoweredStatement>()
        };
        return EnumerateStatements(nested);
    }

    private static IEnumerable<PowerShellLoweredExpression> EnumerateDirectExpressions(
        PowerShellLoweredStatement statement)
    {
        switch (statement)
        {
            case PowerShellLoweredAssignmentStatement assignment:
                yield return assignment.Value;
                break;
            case PowerShellLoweredIndexAssignmentStatement assignment:
                yield return assignment.Target;
                yield return assignment.Index;
                yield return assignment.Value;
                break;
            case PowerShellLoweredClrMemberAssignmentStatement assignment:
                if (assignment.Receiver is not null) yield return assignment.Receiver;
                yield return assignment.Value;
                break;
            case PowerShellLoweredReturnStatement { Expression: not null } returned:
                yield return returned.Expression;
                break;
            case PowerShellLoweredExpressionStatement expression:
                yield return expression.Expression;
                break;
            case PowerShellLoweredStreamWriteStatement stream:
                yield return stream.Message;
                break;
            case PowerShellLoweredIfStatement conditional:
                foreach (var clause in conditional.Clauses) yield return clause.Condition;
                break;
            case PowerShellLoweredWhileStatement loop:
                yield return loop.Condition;
                break;
            case PowerShellLoweredForStatement loop:
                if (loop.Initializer is not null) yield return loop.Initializer;
                if (loop.Condition is not null) yield return loop.Condition;
                if (loop.Iterator is not null) yield return loop.Iterator;
                break;
            case PowerShellLoweredForEachStatement loop:
                yield return loop.Collection;
                if (loop.NullCollectionElement is not null) yield return loop.NullCollectionElement;
                break;
            case PowerShellLoweredSwitchStatement selected:
                yield return selected.Value;
                foreach (var clause in selected.Clauses) yield return clause.Value;
                break;
            case PowerShellLoweredThrowStatement { Expression: not null } thrown:
                yield return thrown.Expression;
                break;
        }
    }

    private static IEnumerable<PowerShellLoweredExpression> EnumerateExpressionTree(
        PowerShellLoweredExpression expression)
    {
        yield return expression;
        foreach (var child in EnumerateChildExpressions(expression))
        foreach (var descendant in EnumerateExpressionTree(child))
            yield return descendant;
    }

    private static IEnumerable<PowerShellLoweredExpression> EnumerateChildExpressions(
        PowerShellLoweredExpression expression)
    {
        switch (expression)
        {
            case PowerShellLoweredRuntimeStateExpression runtime:
                foreach (var argument in runtime.Arguments) yield return argument;
                break;
            case PowerShellLoweredCommandAvailabilityExpression discovery:
                yield return discovery.Name;
                break;
            case PowerShellLoweredHostedBooleanCommandExpression hostedBoolean:
                foreach (var argument in hostedBoolean.Arguments)
                    if (argument.Value is not null) yield return argument.Value;
                break;
            case PowerShellLoweredConversionExpression conversion:
                yield return conversion.Operand;
                break;
            case PowerShellLoweredBinaryExpression binary:
                yield return binary.Left;
                yield return binary.Right;
                break;
            case PowerShellLoweredUnaryExpression unary:
                yield return unary.Operand;
                break;
            case PowerShellLoweredTypeTestExpression typeTest:
                yield return typeTest.Operand;
                break;
            case PowerShellLoweredRegexExpression regex:
                yield return regex.Input;
                yield return regex.Pattern;
                if (regex.Replacement is not null) yield return regex.Replacement;
                break;
            case PowerShellLoweredWildcardExpression wildcard:
                yield return wildcard.Input;
                yield return wildcard.Pattern;
                break;
            case PowerShellLoweredMembershipExpression membership:
                yield return membership.Left;
                yield return membership.Right;
                break;
            case PowerShellLoweredStringSplitExpression split:
                yield return split.Input;
                yield return split.Pattern;
                break;
            case PowerShellLoweredStringJoinExpression join:
                yield return join.Values;
                yield return join.Separator;
                break;
            case PowerShellLoweredInterpolatedStringExpression interpolated:
                foreach (var part in interpolated.Parts)
                    if (part.Expression is not null) yield return part.Expression;
                break;
            case PowerShellLoweredMutationExpression { Value: not null } mutation:
                yield return mutation.Value;
                break;
            case PowerShellLoweredArrayExpression array:
                foreach (var element in array.Elements) yield return element;
                break;
            case PowerShellLoweredArrayConcatenationExpression concatenation:
                yield return concatenation.Left;
                yield return concatenation.Right;
                break;
            case PowerShellLoweredDictionaryExpression dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    yield return entry.Key;
                    yield return entry.Value;
                }
                break;
            case PowerShellLoweredPowerShellObjectExpression powerShellObject:
                foreach (var property in powerShellObject.Properties) yield return property.Value;
                break;
            case PowerShellLoweredIndexExpression index:
                yield return index.Target;
                yield return index.Index;
                break;
            case PowerShellLoweredClrMemberExpression { Receiver: not null } member:
                yield return member.Receiver;
                break;
            case PowerShellLoweredClrInvocationExpression invocation:
                if (invocation.Receiver is not null) yield return invocation.Receiver;
                foreach (var argument in invocation.Arguments) yield return argument;
                break;
            case PowerShellLoweredInvocationExpression invocation:
                foreach (var argument in invocation.Arguments) yield return argument;
                break;
        }
    }
}
