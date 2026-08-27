namespace PowerForge;

internal abstract class PowerShellLoweredNode
{
    protected PowerShellLoweredNode(SourceSpan span) => Span = span;
    internal SourceSpan Span { get; }
}

internal abstract class PowerShellLoweredStatement : PowerShellLoweredNode
{
    protected PowerShellLoweredStatement(SourceSpan span) : base(span) { }
}

internal abstract class PowerShellLoweredExpression : PowerShellLoweredNode
{
    protected PowerShellLoweredExpression(SourceSpan span, Type clrType) : base(span) => ClrType = clrType;
    internal Type ClrType { get; }
}

internal sealed class PowerShellLoweredLiteralExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredLiteralExpression(SourceSpan span, Type clrType, object? value) : base(span, clrType) => Value = value;
    internal object? Value { get; }
}

internal sealed class PowerShellLoweredVariableExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredVariableExpression(SourceSpan span, Type clrType, PowerShellSymbolId symbol) : base(span, clrType) => Symbol = symbol;
    internal PowerShellSymbolId Symbol { get; }
}

internal sealed class PowerShellLoweredConversionExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredConversionExpression(SourceSpan span, Type clrType, PowerShellLoweredExpression operand, bool usePowerShellLanguageRuntime) : base(span, clrType)
    {
        Operand = operand;
        UsePowerShellLanguageRuntime = usePowerShellLanguageRuntime;
    }
    internal PowerShellLoweredExpression Operand { get; }
    internal bool UsePowerShellLanguageRuntime { get; }
}

internal sealed class PowerShellLoweredInvocationExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredInvocationExpression(
        SourceSpan span,
        Type clrType,
        PowerShellSymbolId target,
        PowerShellLoweredExpression[] arguments,
        int[] authoredEvaluationOrder,
        string[] boundParameterNames,
        string?[] evaluationTemporaryNames,
        bool requiresBoundParameters,
        bool requiresPowerShellStreams,
        bool requiresPowerShellCommandRegions,
        bool requiresPowerShellRuntimeState)
        : base(span, clrType)
    {
        Target = target;
        Arguments = arguments;
        AuthoredEvaluationOrder = authoredEvaluationOrder;
        BoundParameterNames = boundParameterNames;
        EvaluationTemporaryNames = evaluationTemporaryNames;
        RequiresBoundParameters = requiresBoundParameters;
        RequiresPowerShellStreams = requiresPowerShellStreams;
        RequiresPowerShellCommandRegions = requiresPowerShellCommandRegions;
        RequiresPowerShellRuntimeState = requiresPowerShellRuntimeState;
    }

    internal PowerShellSymbolId Target { get; }
    internal PowerShellLoweredExpression[] Arguments { get; }
    internal int[] AuthoredEvaluationOrder { get; }
    internal string[] BoundParameterNames { get; }
    internal string?[] EvaluationTemporaryNames { get; }
    internal bool RequiresBoundParameters { get; }
    internal bool RequiresPowerShellStreams { get; }
    internal bool RequiresPowerShellCommandRegions { get; }
    internal bool RequiresPowerShellRuntimeState { get; }
}

internal sealed class PowerShellLoweredReturnStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredReturnStatement(SourceSpan span, PowerShellLoweredExpression? expression, bool emitsValue)
        : base(span)
    {
        Expression = expression;
        EmitsValue = expression is not null && emitsValue;
    }

    internal PowerShellLoweredExpression? Expression { get; }
    internal bool EmitsValue { get; }
}

internal sealed class PowerShellLoweredExpressionStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredExpressionStatement(SourceSpan span, PowerShellLoweredExpression expression)
        : base(span) => Expression = expression;

    internal PowerShellLoweredExpression Expression { get; }
}

internal sealed class PowerShellLoweredAssignmentStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredAssignmentStatement(
        SourceSpan span,
        PowerShellSymbolId target,
        Type clrType,
        PowerShellLoweredExpression value,
        bool declare,
        PowerShellBoundMutationOperator operation = PowerShellBoundMutationOperator.Assign,
        bool normalizeNullString = false,
        bool checkedIntegral = false)
        : base(span)
    {
        Target = target;
        ClrType = clrType;
        Value = value;
        Declare = declare;
        Operation = operation;
        NormalizeNullString = normalizeNullString;
        CheckedIntegral = checkedIntegral;
    }

    internal PowerShellSymbolId Target { get; }
    internal Type ClrType { get; }
    internal PowerShellLoweredExpression Value { get; }
    internal bool Declare { get; }
    internal PowerShellBoundMutationOperator Operation { get; }
    internal bool NormalizeNullString { get; }
    internal bool CheckedIntegral { get; }
}

internal sealed class PowerShellLoweredLocalDeclarationStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredLocalDeclarationStatement(SourceSpan span, PowerShellSymbolId symbol, Type clrType)
        : base(span)
    {
        Symbol = symbol;
        ClrType = clrType;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal Type ClrType { get; }
}

internal sealed class PowerShellLoweredParameter
{
    internal PowerShellLoweredParameter(PowerShellSymbolId symbol, Type clrType, PowerShellCompilationParameter contract)
    {
        Symbol = symbol;
        ClrType = clrType;
        Contract = contract;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal Type ClrType { get; }
    internal PowerShellCompilationParameter Contract { get; }
}

internal sealed class PowerShellLoweredFunction
{
    internal PowerShellLoweredFunction(
        PowerShellSymbolId symbol,
        string generatedName,
        Type returnType,
        PowerShellLoweredParameter[] parameters,
        PowerShellLoweredLocal[] locals,
        PowerShellBoundHelpMetadata? help,
        Type? declaredOutputType,
        bool requiresPowerShellBoundParameters,
        bool requiresPowerShellStreams,
        bool requiresPowerShellCommandRegions,
        bool requiresPowerShellRuntimeState,
        PowerShellLoweredStatement[] statements,
        SourceSpan span)
    {
        Symbol = symbol;
        GeneratedName = generatedName;
        ReturnType = returnType;
        Parameters = parameters;
        Locals = locals;
        Help = help;
        DeclaredOutputType = declaredOutputType;
        RequiresPowerShellBoundParameters = requiresPowerShellBoundParameters;
        RequiresPowerShellStreams = requiresPowerShellStreams;
        RequiresPowerShellCommandRegions = requiresPowerShellCommandRegions;
        RequiresPowerShellRuntimeState = requiresPowerShellRuntimeState;
        Statements = statements;
        Span = span;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal string GeneratedName { get; }
    internal Type ReturnType { get; }
    internal PowerShellLoweredParameter[] Parameters { get; }
    internal PowerShellLoweredLocal[] Locals { get; }
    internal PowerShellBoundHelpMetadata? Help { get; }
    internal Type? DeclaredOutputType { get; }
    internal bool RequiresPowerShellBoundParameters { get; }
    internal bool RequiresPowerShellStreams { get; }
    internal bool RequiresPowerShellCommandRegions { get; }
    internal bool RequiresPowerShellRuntimeState { get; }
    internal PowerShellLoweredStatement[] Statements { get; }
    internal SourceSpan Span { get; }
}

internal sealed class PowerShellLoweredLocal
{
    internal PowerShellLoweredLocal(PowerShellSymbolId symbol, Type clrType)
    {
        Symbol = symbol;
        ClrType = clrType;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal Type ClrType { get; }
}

internal sealed class PowerShellLoweredProgram
{
    internal PowerShellLoweredProgram(
        PowerShellLoweredFunction[] functions,
        PowerShellSemanticDiagnostic[] diagnostics,
        PowerShellCompilationCapability targetCapabilities)
    {
        Functions = functions;
        Diagnostics = diagnostics;
        TargetCapabilities = targetCapabilities;
    }

    internal PowerShellLoweredFunction[] Functions { get; }
    internal PowerShellSemanticDiagnostic[] Diagnostics { get; }
    internal PowerShellCompilationCapability TargetCapabilities { get; }
}
