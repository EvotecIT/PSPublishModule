namespace PowerForge;

internal sealed class PowerShellLoweredConditionalClause
{
    internal PowerShellLoweredConditionalClause(PowerShellLoweredExpression condition, PowerShellLoweredStatement[] statements)
    {
        Condition = condition;
        Statements = statements;
    }

    internal PowerShellLoweredExpression Condition { get; }
    internal PowerShellLoweredStatement[] Statements { get; }
}

internal sealed class PowerShellLoweredIfStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredIfStatement(SourceSpan span, PowerShellLoweredConditionalClause[] clauses, PowerShellLoweredStatement[]? elseStatements)
        : base(span)
    {
        Clauses = clauses;
        ElseStatements = elseStatements;
    }

    internal PowerShellLoweredConditionalClause[] Clauses { get; }
    internal PowerShellLoweredStatement[]? ElseStatements { get; }
}

internal sealed class PowerShellLoweredWhileStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredWhileStatement(SourceSpan span, PowerShellLoweredExpression condition, PowerShellLoweredStatement[] statements)
        : base(span)
    {
        Condition = condition;
        Statements = statements;
    }

    internal PowerShellLoweredExpression Condition { get; }
    internal PowerShellLoweredStatement[] Statements { get; }
}

internal sealed class PowerShellLoweredForStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredForStatement(
        SourceSpan span,
        PowerShellLoweredMutationExpression? initializer,
        PowerShellLoweredExpression? condition,
        PowerShellLoweredMutationExpression? iterator,
        PowerShellLoweredStatement[] statements,
        bool declareInitializer)
        : base(span)
    {
        Initializer = initializer;
        Condition = condition;
        Iterator = iterator;
        Statements = statements;
        DeclareInitializer = declareInitializer;
    }

    internal PowerShellLoweredMutationExpression? Initializer { get; }
    internal PowerShellLoweredExpression? Condition { get; }
    internal PowerShellLoweredMutationExpression? Iterator { get; }
    internal PowerShellLoweredStatement[] Statements { get; }
    internal bool DeclareInitializer { get; }
}

internal sealed class PowerShellLoweredForEachStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredForEachStatement(
        SourceSpan span,
        PowerShellSymbolId variable,
        Type elementType,
        PowerShellLoweredExpression collection,
        bool scalarString,
        PowerShellLoweredStatement[] statements)
        : base(span)
    {
        Variable = variable;
        ElementType = elementType;
        Collection = collection;
        ScalarString = scalarString;
        Statements = statements;
    }

    internal PowerShellSymbolId Variable { get; }
    internal Type ElementType { get; }
    internal PowerShellLoweredExpression Collection { get; }
    internal bool ScalarString { get; }
    internal PowerShellLoweredStatement[] Statements { get; }
}

internal sealed class PowerShellLoweredBreakStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredBreakStatement(SourceSpan span) : base(span) { }
}

internal sealed class PowerShellLoweredContinueStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredContinueStatement(SourceSpan span) : base(span) { }
}
