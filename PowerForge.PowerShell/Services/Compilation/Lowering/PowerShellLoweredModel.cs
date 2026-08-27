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

internal sealed class PowerShellLoweredReturnStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredReturnStatement(SourceSpan span, PowerShellLoweredExpression? expression) : base(span) => Expression = expression;
    internal PowerShellLoweredExpression? Expression { get; }
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
        PowerShellLoweredStatement[] statements,
        SourceSpan span)
    {
        Symbol = symbol;
        GeneratedName = generatedName;
        ReturnType = returnType;
        Parameters = parameters;
        Statements = statements;
        Span = span;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal string GeneratedName { get; }
    internal Type ReturnType { get; }
    internal PowerShellLoweredParameter[] Parameters { get; }
    internal PowerShellLoweredStatement[] Statements { get; }
    internal SourceSpan Span { get; }
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
