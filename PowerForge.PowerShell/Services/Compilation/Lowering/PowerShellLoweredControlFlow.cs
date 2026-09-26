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
    internal PowerShellLoweredWhileStatement(SourceSpan span, PowerShellLoweredLoopKind kind, PowerShellLoweredExpression condition, PowerShellLoweredStatement[] statements,
        bool checkHostInterrupts)
        : base(span)
    {
        Kind = kind;
        Condition = condition;
        Statements = statements;
        CheckHostInterrupts = checkHostInterrupts;
    }

    internal PowerShellLoweredLoopKind Kind { get; }
    internal PowerShellLoweredExpression Condition { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal bool CheckHostInterrupts { get; }
}

internal sealed class PowerShellLoweredForStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredForStatement(
        SourceSpan span,
        PowerShellLoweredMutationExpression? initializer,
        PowerShellLoweredExpression? condition,
        PowerShellLoweredMutationExpression? iterator,
        PowerShellLoweredStatement[] statements,
        bool declareInitializer,
        bool checkHostInterrupts)
        : base(span)
    {
        Initializer = initializer;
        Condition = condition;
        Iterator = iterator;
        Statements = statements;
        DeclareInitializer = declareInitializer;
        CheckHostInterrupts = checkHostInterrupts;
    }

    internal PowerShellLoweredMutationExpression? Initializer { get; }
    internal PowerShellLoweredExpression? Condition { get; }
    internal PowerShellLoweredMutationExpression? Iterator { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal bool DeclareInitializer { get; }
    internal bool CheckHostInterrupts { get; }
}

internal sealed class PowerShellLoweredForEachStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredForEachStatement(
        SourceSpan span,
        PowerShellSymbolId variable,
        Type elementType,
        PowerShellLoweredExpression collection,
        PowerShellForEachEnumerationKind enumerationKind,
        PowerShellLoweredStatement[] statements,
        bool declareVariable,
        PowerShellLoweredExpression? nullCollectionElement,
        bool checkHostInterrupts,
        PowerShellNativeForEachBinding? nativeBinding = null)
        : base(span)
    {
        Variable = variable;
        ElementType = elementType;
        Collection = collection;
        EnumerationKind = enumerationKind;
        Statements = statements;
        DeclareVariable = declareVariable;
        NullCollectionElement = nullCollectionElement;
        CheckHostInterrupts = checkHostInterrupts;
        NativeBinding = nativeBinding;
    }

    internal PowerShellSymbolId Variable { get; }
    internal Type ElementType { get; }
    internal PowerShellLoweredExpression Collection { get; }
    internal PowerShellForEachEnumerationKind EnumerationKind { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal bool DeclareVariable { get; }
    internal PowerShellLoweredExpression? NullCollectionElement { get; }
    internal bool CheckHostInterrupts { get; }
    internal PowerShellNativeForEachBinding? NativeBinding { get; }
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
        bool caseSensitive,
        PowerShellBoundSwitchInputKind inputKind = PowerShellBoundSwitchInputKind.Scalar)
        : base(span)
    {
        Value = value;
        Clauses = clauses;
        DefaultStatements = defaultStatements is null ? null : new PowerShellImmutableArray<PowerShellLoweredStatement>(defaultStatements);
        MatchMode = matchMode;
        CaseSensitive = caseSensitive;
        InputKind = inputKind;
    }

    internal PowerShellLoweredExpression Value { get; }
    internal PowerShellImmutableArray<PowerShellLoweredSwitchClause> Clauses { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement>? DefaultStatements { get; }
    internal PowerShellBoundSwitchMatchMode MatchMode { get; }
    internal bool CaseSensitive { get; }
    internal PowerShellBoundSwitchInputKind InputKind { get; }
}

internal sealed class PowerShellLoweredThrowStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredThrowStatement(SourceSpan span, PowerShellLoweredExpression? expression,
        bool preserveStatementErrors = false, string sourcePath = "", string sourceText = "") : base(span)
    {
        Expression = expression;
        PreserveStatementErrors = preserveStatementErrors;
        SourcePath = sourcePath;
        SourceText = sourceText;
    }
    internal PowerShellLoweredExpression? Expression { get; }
    internal bool PreserveStatementErrors { get; }
    internal string SourcePath { get; }
    internal string SourceText { get; }
}

internal sealed class PowerShellLoweredCatchClause
{
    internal PowerShellLoweredCatchClause(
        Type[] exceptionTypes,
        PowerShellLoweredStatement[] statements,
        string exceptionTemporary,
        bool unwrapPowerShellRuntimeException,
        bool excludePowerShellControlFlow = false)
    {
        ExceptionTypes = exceptionTypes;
        Statements = statements;
        ExceptionTemporary = exceptionTemporary;
        UnwrapPowerShellRuntimeException = unwrapPowerShellRuntimeException;
        ExcludePowerShellControlFlow = excludePowerShellControlFlow;
    }

    internal PowerShellImmutableArray<Type> ExceptionTypes { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal string ExceptionTemporary { get; }
    internal bool UnwrapPowerShellRuntimeException { get; }
    /// <summary>Pipeline stop and PowerShell flow control unwind the command instead of entering an authored catch.</summary>
    internal bool ExcludePowerShellControlFlow { get; }
}

internal sealed class PowerShellLoweredTryStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredTryStatement(
        SourceSpan span,
        PowerShellLoweredStatement[] statements,
        PowerShellLoweredCatchClause[] catches,
        PowerShellLoweredStatement[]? finallyStatements,
        bool preserveStatementErrors = false,
        string exceptionTemporary = "",
        string clauseTemporary = "",
        string recordTemporary = "")
        : base(span)
    {
        Statements = statements;
        Catches = catches;
        FinallyStatements = finallyStatements is null ? null : new PowerShellImmutableArray<PowerShellLoweredStatement>(finallyStatements);
        PreserveStatementErrors = preserveStatementErrors;
        ExceptionTemporary = exceptionTemporary;
        ClauseTemporary = clauseTemporary;
        RecordTemporary = recordTemporary;
    }

    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCatchClause> Catches { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement>? FinallyStatements { get; }
    internal bool PreserveStatementErrors { get; }
    internal string ExceptionTemporary { get; }
    internal string ClauseTemporary { get; }
    internal string RecordTemporary { get; }
}

internal sealed class PowerShellLoweredBreakStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredBreakStatement(SourceSpan span) : base(span) { }
}

internal sealed class PowerShellLoweredContinueStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredContinueStatement(SourceSpan span) : base(span) { }
}
