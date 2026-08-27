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
    internal PowerShellLoweredConversionExpression(SourceSpan span, Type clrType, PowerShellLoweredExpression operand) : base(span, clrType) => Operand = operand;
    internal PowerShellLoweredExpression Operand { get; }
}

internal sealed class PowerShellLoweredInvocationExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredInvocationExpression(SourceSpan span, Type clrType, PowerShellSymbolId target, PowerShellLoweredExpression[] arguments)
        : base(span, clrType)
    {
        Target = target;
        Arguments = arguments;
    }

    internal PowerShellSymbolId Target { get; }
    internal PowerShellLoweredExpression[] Arguments { get; }
}

internal sealed class PowerShellLoweredReturnStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredReturnStatement(SourceSpan span, PowerShellLoweredExpression? expression) : base(span) => Expression = expression;
    internal PowerShellLoweredExpression? Expression { get; }
}

internal sealed class PowerShellLoweredAssignmentStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredAssignmentStatement(SourceSpan span, PowerShellSymbolId target, Type clrType, PowerShellLoweredExpression value, bool declare)
        : base(span)
    {
        Target = target;
        ClrType = clrType;
        Value = value;
        Declare = declare;
    }

    internal PowerShellSymbolId Target { get; }
    internal Type ClrType { get; }
    internal PowerShellLoweredExpression Value { get; }
    internal bool Declare { get; }
}

internal sealed class PowerShellLoweredParameter
{
    internal PowerShellLoweredParameter(PowerShellSymbolId symbol, Type clrType)
    {
        Symbol = symbol;
        ClrType = clrType;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal Type ClrType { get; }
}

internal sealed class PowerShellLoweredFunction
{
    internal PowerShellLoweredFunction(
        PowerShellSymbolId symbol,
        string generatedName,
        Type returnType,
        PowerShellLoweredParameter[] parameters,
        PowerShellLoweredLocal[] locals,
        PowerShellLoweredStatement[] statements,
        SourceSpan span)
    {
        Symbol = symbol;
        GeneratedName = generatedName;
        ReturnType = returnType;
        Parameters = parameters;
        Locals = locals;
        Statements = statements;
        Span = span;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal string GeneratedName { get; }
    internal Type ReturnType { get; }
    internal PowerShellLoweredParameter[] Parameters { get; }
    internal PowerShellLoweredLocal[] Locals { get; }
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
    internal PowerShellLoweredProgram(PowerShellLoweredFunction[] functions, PowerShellSemanticDiagnostic[] diagnostics)
    {
        Functions = functions;
        Diagnostics = diagnostics;
    }

    internal PowerShellLoweredFunction[] Functions { get; }
    internal PowerShellSemanticDiagnostic[] Diagnostics { get; }
}
