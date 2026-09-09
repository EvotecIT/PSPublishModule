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

internal enum PowerShellBoundLoopKind
{
    While,
    DoWhile,
    DoUntil
}

internal sealed class PowerShellBoundWhileStatement : PowerShellBoundStatement
{
    internal PowerShellBoundWhileStatement(SourceSpan span, PowerShellBoundLoopKind kind, PowerShellBoundExpression condition, PowerShellBoundBlock body,
        bool checkHostInterrupts = false)
        : base(span, condition.Effects | body.Effects | PowerShellLoopInterruptContract.Effects(checkHostInterrupts),
            condition.Capabilities | body.Capabilities | PowerShellLoopInterruptContract.Capabilities(checkHostInterrupts))
    {
        Kind = kind;
        Condition = condition;
        Body = body;
        CheckHostInterrupts = checkHostInterrupts;
    }

    internal PowerShellBoundLoopKind Kind { get; }
    internal PowerShellBoundExpression Condition { get; }
    internal PowerShellBoundBlock Body { get; }
    internal bool CheckHostInterrupts { get; }
}

internal sealed class PowerShellBoundForStatement : PowerShellBoundStatement
{
    internal PowerShellBoundForStatement(
        SourceSpan span,
        PowerShellBoundMutationExpression? initializer,
        PowerShellBoundExpression? condition,
        PowerShellBoundMutationExpression? iterator,
        PowerShellBoundBlock body,
        bool checkHostInterrupts = false)
        : base(
            span,
            PowerShellSemanticEffect.Mutation |
            (initializer?.Effects ?? PowerShellSemanticEffect.None) |
            (condition?.Effects ?? PowerShellSemanticEffect.None) |
            (iterator?.Effects ?? PowerShellSemanticEffect.None) |
            body.Effects | PowerShellLoopInterruptContract.Effects(checkHostInterrupts),
            (initializer?.Capabilities ?? PowerShellRequiredCapability.None) |
            (condition?.Capabilities ?? PowerShellRequiredCapability.None) |
            (iterator?.Capabilities ?? PowerShellRequiredCapability.None) |
            body.Capabilities | PowerShellLoopInterruptContract.Capabilities(checkHostInterrupts))
    {
        Initializer = initializer;
        Condition = condition;
        Iterator = iterator;
        Body = body;
        CheckHostInterrupts = checkHostInterrupts;
    }

    internal PowerShellBoundMutationExpression? Initializer { get; }
    internal PowerShellBoundExpression? Condition { get; }
    internal PowerShellBoundMutationExpression? Iterator { get; }
    internal PowerShellBoundBlock Body { get; }
    internal bool CheckHostInterrupts { get; }
}

internal sealed class PowerShellBoundForEachStatement : PowerShellBoundStatement
{
    internal PowerShellBoundForEachStatement(
        SourceSpan span,
        PowerShellSymbolId variable,
        Type elementType,
        PowerShellBoundExpression collection,
        PowerShellForEachEnumerationKind enumerationKind,
        PowerShellBoundBlock body,
        bool declareVariable = false,
        PowerShellBoundExpression? nullCollectionElement = null,
        bool checkHostInterrupts = false,
        PowerShellNativeForEachBinding? nativeBinding = null)
        : base(
            span,
            PowerShellSemanticEffect.Mutation | collection.Effects | body.Effects | (nullCollectionElement?.Effects ?? PowerShellSemanticEffect.None) |
                PowerShellLoopInterruptContract.Effects(checkHostInterrupts || enumerationKind is PowerShellForEachEnumerationKind.PowerShellEnumerable or PowerShellForEachEnumerationKind.NativeInvocation) |
                (enumerationKind == PowerShellForEachEnumerationKind.PowerShellEnumerable ? PowerShellSemanticEffect.NonSuccessStream : PowerShellSemanticEffect.None) |
                (nativeBinding is not null ? PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError : PowerShellSemanticEffect.None),
            collection.Capabilities | body.Capabilities | (nullCollectionElement?.Capabilities ?? PowerShellRequiredCapability.None) |
                PowerShellLoopInterruptContract.Capabilities(checkHostInterrupts || enumerationKind is PowerShellForEachEnumerationKind.PowerShellEnumerable or PowerShellForEachEnumerationKind.NativeInvocation) |
                (enumerationKind == PowerShellForEachEnumerationKind.PowerShellEnumerable ? PowerShellRequiredCapability.PowerShellStatementErrors : PowerShellRequiredCapability.None) |
                (nativeBinding is not null ? PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
                    PowerShellRequiredCapability.PowerShellStatementErrors : PowerShellRequiredCapability.None))
    {
        if ((nativeBinding is not null) != (enumerationKind == PowerShellForEachEnumerationKind.NativeInvocation))
            throw new ArgumentException("Native foreach enumeration requires its authored variable binding.");
        Variable = variable;
        ElementType = elementType;
        Collection = collection;
        EnumerationKind = enumerationKind;
        Body = body;
        DeclareVariable = declareVariable;
        NullCollectionElement = nullCollectionElement;
        CheckHostInterrupts = checkHostInterrupts || enumerationKind is PowerShellForEachEnumerationKind.PowerShellEnumerable or PowerShellForEachEnumerationKind.NativeInvocation;
        NativeBinding = nativeBinding;
    }

    internal PowerShellSymbolId Variable { get; }
    internal Type ElementType { get; }
    internal PowerShellBoundExpression Collection { get; }
    internal PowerShellForEachEnumerationKind EnumerationKind { get; }
    internal PowerShellBoundBlock Body { get; }
    internal bool DeclareVariable { get; }
    internal PowerShellBoundExpression? NullCollectionElement { get; }
    internal bool CheckHostInterrupts { get; }
    internal PowerShellNativeForEachBinding? NativeBinding { get; }
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

internal enum PowerShellBoundSwitchMatchMode
{
    Exact,
    Regex
}

internal sealed class PowerShellBoundSwitchStatement : PowerShellBoundStatement
{
    internal PowerShellBoundSwitchStatement(
        SourceSpan span,
        PowerShellBoundExpression value,
        PowerShellBoundSwitchClause[] clauses,
        PowerShellBoundBlock? defaultBlock,
        PowerShellBoundSwitchMatchMode matchMode,
        bool caseSensitive)
        : base(
            span,
            clauses.Aggregate(value.Effects | (defaultBlock?.Effects ?? PowerShellSemanticEffect.None), static (effects, clause) => effects | clause.Value.Effects | clause.Body.Effects),
            clauses.Aggregate(value.Capabilities | (defaultBlock?.Capabilities ?? PowerShellRequiredCapability.None), static (capabilities, clause) => capabilities | clause.Value.Capabilities | clause.Body.Capabilities))
    {
        Value = value;
        Clauses = clauses;
        DefaultBlock = defaultBlock;
        MatchMode = matchMode;
        CaseSensitive = caseSensitive;
    }

    internal PowerShellBoundExpression Value { get; }
    internal PowerShellImmutableArray<PowerShellBoundSwitchClause> Clauses { get; }
    internal PowerShellBoundBlock? DefaultBlock { get; }
    internal PowerShellBoundSwitchMatchMode MatchMode { get; }
    internal bool CaseSensitive { get; }
}

internal sealed class PowerShellBoundThrowStatement : PowerShellBoundStatement
{
    internal PowerShellBoundThrowStatement(SourceSpan span, PowerShellBoundExpression? expression,
        bool preserveStatementErrors = false, string sourcePath = "", string sourceText = "")
        : base(span, PowerShellSemanticEffect.TerminatingError | (expression?.Effects ?? PowerShellSemanticEffect.None),
            (expression?.Capabilities ?? PowerShellRequiredCapability.None) |
            (preserveStatementErrors ? PowerShellRequiredCapability.PowerShellStatementErrors | PowerShellRequiredCapability.PowerShellHostTypes : PowerShellRequiredCapability.None))
    {
        Expression = expression;
        PreserveStatementErrors = preserveStatementErrors;
        SourcePath = sourcePath;
        SourceText = sourceText;
    }

    internal PowerShellBoundExpression? Expression { get; }
    internal bool IsRethrow => Expression is null;
    internal bool PreserveStatementErrors { get; }
    internal string SourcePath { get; }
    internal string SourceText { get; }
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
        PowerShellBoundBlock? finallyBlock,
        bool suspendHostStopping = false)
        : base(
            span,
            catches.Aggregate(body.Effects | (finallyBlock?.Effects ?? PowerShellSemanticEffect.None), static (effects, clause) => effects | clause.Body.Effects),
            catches.Aggregate(body.Capabilities | (finallyBlock?.Capabilities ?? PowerShellRequiredCapability.None) |
                PowerShellLoopInterruptContract.Capabilities(suspendHostStopping), static (capabilities, clause) => capabilities | clause.Body.Capabilities) |
                (suspendHostStopping || PowerShellLoopInterruptContract.RequiresContext(body.Capabilities) ||
                 catches.Any(static clause => PowerShellLoopInterruptContract.RequiresContext(clause.Body.Capabilities)) ||
                 finallyBlock is not null && PowerShellLoopInterruptContract.RequiresContext(finallyBlock.Capabilities)
                    ? PowerShellRequiredCapability.PowerShellStatementErrors | PowerShellRequiredCapability.PowerShellHostTypes
                    : PowerShellRequiredCapability.None))
    {
        Body = body;
        Catches = catches;
        FinallyBlock = finallyBlock;
        SuspendHostStopping = suspendHostStopping;
    }

    internal PowerShellBoundBlock Body { get; }
    internal PowerShellImmutableArray<PowerShellBoundCatchClause> Catches { get; }
    internal PowerShellBoundBlock? FinallyBlock { get; }
    internal bool SuspendHostStopping { get; }
}

internal sealed class PowerShellBoundBreakStatement : PowerShellBoundStatement
{
    internal PowerShellBoundBreakStatement(SourceSpan span) : base(span, PowerShellSemanticEffect.None) { }
}

internal sealed class PowerShellBoundContinueStatement : PowerShellBoundStatement
{
    internal PowerShellBoundContinueStatement(SourceSpan span) : base(span, PowerShellSemanticEffect.None) { }
}
