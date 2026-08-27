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

    internal PowerShellBoundConditionalClause[] Clauses { get; }
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
            PowerShellSemanticEffect.Mutation | body.Effects,
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

internal sealed class PowerShellBoundBreakStatement : PowerShellBoundStatement
{
    internal PowerShellBoundBreakStatement(SourceSpan span) : base(span, PowerShellSemanticEffect.None) { }
}

internal sealed class PowerShellBoundContinueStatement : PowerShellBoundStatement
{
    internal PowerShellBoundContinueStatement(SourceSpan span) : base(span, PowerShellSemanticEffect.None) { }
}
