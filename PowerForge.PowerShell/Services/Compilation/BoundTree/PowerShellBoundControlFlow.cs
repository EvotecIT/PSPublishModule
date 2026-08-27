namespace PowerForge;

internal sealed class PowerShellBoundConditionalClause
{
    internal PowerShellBoundConditionalClause(PowerShellBoundExpression condition, PowerShellBoundBlock body)
    {
        Condition = condition;
        Body = body;
    }

    internal PowerShellBoundExpression Condition { get; }
    internal PowerShellBoundBlock Body { get; }
}

internal sealed class PowerShellBoundIfStatement : PowerShellBoundStatement
{
    internal PowerShellBoundIfStatement(SourceSpan span, PowerShellBoundConditionalClause[] clauses, PowerShellBoundBlock? elseBlock)
        : base(
            span,
            clauses.Aggregate(elseBlock?.Effects ?? PowerShellSemanticEffect.None, static (value, clause) => value | clause.Condition.Effects | clause.Body.Effects),
            clauses.Aggregate(elseBlock?.Capabilities ?? PowerShellRequiredCapability.None, static (value, clause) => value | clause.Condition.Capabilities | clause.Body.Capabilities))
    {
        Clauses = clauses;
        ElseBlock = elseBlock;
    }

    internal PowerShellImmutableArray<PowerShellBoundConditionalClause> Clauses { get; }
    internal PowerShellBoundBlock? ElseBlock { get; }
}

internal sealed class PowerShellBoundWhileStatement : PowerShellBoundStatement
{
    internal PowerShellBoundWhileStatement(SourceSpan span, PowerShellBoundExpression condition, PowerShellBoundBlock body)
        : base(span, condition.Effects | body.Effects, condition.Capabilities | body.Capabilities)
    {
        Condition = condition;
        Body = body;
    }

    internal PowerShellBoundExpression Condition { get; }
    internal PowerShellBoundBlock Body { get; }
}

internal sealed class PowerShellBoundForStatement : PowerShellBoundStatement
{
    internal PowerShellBoundForStatement(
        SourceSpan span,
        PowerShellBoundMutationExpression? initializer,
        PowerShellBoundExpression? condition,
        PowerShellBoundMutationExpression? iterator,
        PowerShellBoundBlock body)
        : base(
            span,
            PowerShellSemanticEffect.Mutation |
            (initializer?.Effects ?? PowerShellSemanticEffect.None) |
            (condition?.Effects ?? PowerShellSemanticEffect.None) |
            (iterator?.Effects ?? PowerShellSemanticEffect.None) |
            body.Effects,
            (initializer?.Capabilities ?? PowerShellRequiredCapability.None) |
            (condition?.Capabilities ?? PowerShellRequiredCapability.None) |
            (iterator?.Capabilities ?? PowerShellRequiredCapability.None) |
            body.Capabilities)
    {
        Initializer = initializer;
        Condition = condition;
        Iterator = iterator;
        Body = body;
    }

    internal PowerShellBoundMutationExpression? Initializer { get; }
    internal PowerShellBoundExpression? Condition { get; }
    internal PowerShellBoundMutationExpression? Iterator { get; }
    internal PowerShellBoundBlock Body { get; }
}

internal sealed class PowerShellBoundForEachStatement : PowerShellBoundStatement
{
    internal PowerShellBoundForEachStatement(
        SourceSpan span,
        PowerShellSymbolId variable,
        Type elementType,
        PowerShellBoundExpression collection,
        bool scalarString,
        PowerShellBoundBlock body)
        : base(span, PowerShellSemanticEffect.Mutation | collection.Effects | body.Effects, collection.Capabilities | body.Capabilities)
    {
        Variable = variable;
        ElementType = elementType;
        Collection = collection;
        ScalarString = scalarString;
        Body = body;
    }

    internal PowerShellSymbolId Variable { get; }
    internal Type ElementType { get; }
    internal PowerShellBoundExpression Collection { get; }
    internal bool ScalarString { get; }
    internal PowerShellBoundBlock Body { get; }
}

internal sealed class PowerShellBoundSwitchClause
{
    internal PowerShellBoundSwitchClause(PowerShellBoundExpression value, PowerShellBoundBlock body)
    {
        Value = value;
        Body = body;
    }

    internal PowerShellBoundExpression Value { get; }
    internal PowerShellBoundBlock Body { get; }
}

internal sealed class PowerShellBoundSwitchStatement : PowerShellBoundStatement
{
    internal PowerShellBoundSwitchStatement(
        SourceSpan span,
        PowerShellBoundExpression value,
        PowerShellBoundSwitchClause[] clauses,
        PowerShellBoundBlock? defaultBlock,
        bool caseSensitive)
        : base(
            span,
            clauses.Aggregate(value.Effects | (defaultBlock?.Effects ?? PowerShellSemanticEffect.None), static (effects, clause) => effects | clause.Value.Effects | clause.Body.Effects),
            clauses.Aggregate(value.Capabilities | (defaultBlock?.Capabilities ?? PowerShellRequiredCapability.None), static (capabilities, clause) => capabilities | clause.Value.Capabilities | clause.Body.Capabilities))
    {
        Value = value;
        Clauses = clauses;
        DefaultBlock = defaultBlock;
        CaseSensitive = caseSensitive;
    }

    internal PowerShellBoundExpression Value { get; }
    internal PowerShellImmutableArray<PowerShellBoundSwitchClause> Clauses { get; }
    internal PowerShellBoundBlock? DefaultBlock { get; }
    internal bool CaseSensitive { get; }
}

internal sealed class PowerShellBoundThrowStatement : PowerShellBoundStatement
{
    internal PowerShellBoundThrowStatement(SourceSpan span, PowerShellBoundExpression? expression)
        : base(span, PowerShellSemanticEffect.TerminatingError | (expression?.Effects ?? PowerShellSemanticEffect.None), expression?.Capabilities ?? PowerShellRequiredCapability.None)
    {
        Expression = expression;
    }

    internal PowerShellBoundExpression? Expression { get; }
    internal bool IsRethrow => Expression is null;
}

internal sealed class PowerShellBoundCatchClause
{
    internal PowerShellBoundCatchClause(Type[] exceptionTypes, PowerShellBoundBlock body)
    {
        ExceptionTypes = exceptionTypes;
        Body = body;
    }

    internal PowerShellImmutableArray<Type> ExceptionTypes { get; }
    internal PowerShellBoundBlock Body { get; }
}

internal sealed class PowerShellBoundTryStatement : PowerShellBoundStatement
{
    internal PowerShellBoundTryStatement(
        SourceSpan span,
        PowerShellBoundBlock body,
        PowerShellBoundCatchClause[] catches,
        PowerShellBoundBlock? finallyBlock)
        : base(
            span,
            catches.Aggregate(body.Effects | (finallyBlock?.Effects ?? PowerShellSemanticEffect.None), static (effects, clause) => effects | clause.Body.Effects),
            catches.Aggregate(body.Capabilities | (finallyBlock?.Capabilities ?? PowerShellRequiredCapability.None), static (capabilities, clause) => capabilities | clause.Body.Capabilities))
    {
        Body = body;
        Catches = catches;
        FinallyBlock = finallyBlock;
    }

    internal PowerShellBoundBlock Body { get; }
    internal PowerShellImmutableArray<PowerShellBoundCatchClause> Catches { get; }
    internal PowerShellBoundBlock? FinallyBlock { get; }
}

internal sealed class PowerShellBoundBreakStatement : PowerShellBoundStatement
{
    internal PowerShellBoundBreakStatement(SourceSpan span) : base(span, PowerShellSemanticEffect.None) { }
}

internal sealed class PowerShellBoundContinueStatement : PowerShellBoundStatement
{
    internal PowerShellBoundContinueStatement(SourceSpan span) : base(span, PowerShellSemanticEffect.None) { }
}
