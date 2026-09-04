namespace PowerForge;

internal sealed class PowerShellLoweredConditionalClause
{
    internal PowerShellLoweredConditionalClause(PowerShellLoweredExpression condition, PowerShellLoweredStatement[] statements)
    {
        Condition = condition;
        Statements = statements;
    }

    internal PowerShellLoweredExpression Condition { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
}

internal sealed class PowerShellLoweredIfStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredIfStatement(SourceSpan span, PowerShellLoweredConditionalClause[] clauses, PowerShellLoweredStatement[]? elseStatements)
        : base(span)
    {
        Clauses = clauses;
        ElseStatements = elseStatements is null ? null : new PowerShellImmutableArray<PowerShellLoweredStatement>(elseStatements);
    }

    internal PowerShellImmutableArray<PowerShellLoweredConditionalClause> Clauses { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement>? ElseStatements { get; }
}

internal enum PowerShellLoweredLoopKind
{
    While,
    DoWhile,
    DoUntil
}

internal sealed class PowerShellLoweredWhileStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredWhileStatement(SourceSpan span, PowerShellLoweredLoopKind kind, PowerShellLoweredExpression condition, PowerShellLoweredStatement[] statements)
        : base(span)
    {
        Kind = kind;
        Condition = condition;
        Statements = statements;
    }

    internal PowerShellLoweredLoopKind Kind { get; }
    internal PowerShellLoweredExpression Condition { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
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
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
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
        PowerShellLoweredStatement[] statements,
        bool declareVariable,
        PowerShellLoweredExpression? nullCollectionElement,
        bool systemArray)
        : base(span)
    {
        Variable = variable;
        ElementType = elementType;
        Collection = collection;
        ScalarString = scalarString;
        Statements = statements;
        DeclareVariable = declareVariable;
        NullCollectionElement = nullCollectionElement;
        SystemArray = systemArray;
    }

    internal PowerShellSymbolId Variable { get; }
    internal Type ElementType { get; }
    internal PowerShellLoweredExpression Collection { get; }
    internal bool ScalarString { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal bool DeclareVariable { get; }
    internal PowerShellLoweredExpression? NullCollectionElement { get; }
    internal bool SystemArray { get; }
}

internal sealed class PowerShellLoweredSwitchClause
{
    internal PowerShellLoweredSwitchClause(PowerShellLoweredExpression value, PowerShellLoweredStatement[] statements)
    {
        Value = value;
        Statements = statements;
    }

    internal PowerShellLoweredExpression Value { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
}

internal sealed class PowerShellLoweredSwitchStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredSwitchStatement(
        SourceSpan span,
        PowerShellLoweredExpression value,
        PowerShellLoweredSwitchClause[] clauses,
        PowerShellLoweredStatement[]? defaultStatements,
        PowerShellBoundSwitchMatchMode matchMode,
        bool caseSensitive)
        : base(span)
    {
        Value = value;
        Clauses = clauses;
        DefaultStatements = defaultStatements is null ? null : new PowerShellImmutableArray<PowerShellLoweredStatement>(defaultStatements);
        MatchMode = matchMode;
        CaseSensitive = caseSensitive;
    }

    internal PowerShellLoweredExpression Value { get; }
    internal PowerShellImmutableArray<PowerShellLoweredSwitchClause> Clauses { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement>? DefaultStatements { get; }
    internal PowerShellBoundSwitchMatchMode MatchMode { get; }
    internal bool CaseSensitive { get; }
}

internal sealed class PowerShellLoweredThrowStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredThrowStatement(SourceSpan span, PowerShellLoweredExpression? expression) : base(span) => Expression = expression;
    internal PowerShellLoweredExpression? Expression { get; }
}

internal sealed class PowerShellLoweredCatchClause
{
    internal PowerShellLoweredCatchClause(
        Type[] exceptionTypes,
        PowerShellLoweredStatement[] statements,
        string exceptionTemporary,
        bool unwrapPowerShellRuntimeException)
    {
        ExceptionTypes = exceptionTypes;
        Statements = statements;
        ExceptionTemporary = exceptionTemporary;
        UnwrapPowerShellRuntimeException = unwrapPowerShellRuntimeException;
    }

    internal PowerShellImmutableArray<Type> ExceptionTypes { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal string ExceptionTemporary { get; }
    internal bool UnwrapPowerShellRuntimeException { get; }
}

internal sealed class PowerShellLoweredTryStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredTryStatement(
        SourceSpan span,
        PowerShellLoweredStatement[] statements,
        PowerShellLoweredCatchClause[] catches,
        PowerShellLoweredStatement[]? finallyStatements)
        : base(span)
    {
        Statements = statements;
        Catches = catches;
        FinallyStatements = finallyStatements is null ? null : new PowerShellImmutableArray<PowerShellLoweredStatement>(finallyStatements);
    }

    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCatchClause> Catches { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement>? FinallyStatements { get; }
}

internal sealed class PowerShellLoweredBreakStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredBreakStatement(SourceSpan span) : base(span) { }
}

internal sealed class PowerShellLoweredContinueStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredContinueStatement(SourceSpan span) : base(span) { }
}
