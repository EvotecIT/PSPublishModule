namespace PowerForge;

internal enum PowerShellBoundBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
    Equal,
    NotEqual,
    EqualIgnoreCase,
    NotEqualIgnoreCase,
    EqualCaseSensitive,
    NotEqualCaseSensitive,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LogicalAnd,
    LogicalOr,
    BitwiseAnd,
    BitwiseOr,
    BitwiseExclusiveOr,
    ShiftLeft,
    ShiftRight
}

internal enum PowerShellBoundUnaryOperator
{
    Identity,
    Negate,
    LogicalNot,
    BitwiseNot
}

internal sealed class PowerShellBoundBinaryExpression : PowerShellBoundExpression
{
    internal PowerShellBoundBinaryExpression(
        SourceSpan span,
        PowerShellBoundBinaryOperator operation,
        PowerShellBoundExpression left,
        PowerShellBoundExpression right,
        PowerShellTypeFact type)
        : base(span, type, PowerShellValueState.Unknown)
    {
        Operation = operation;
        Left = left;
        Right = right;
    }

    internal PowerShellBoundBinaryOperator Operation { get; }
    internal PowerShellBoundExpression Left { get; }
    internal PowerShellBoundExpression Right { get; }
}

internal sealed class PowerShellBoundUnaryExpression : PowerShellBoundExpression
{
    internal PowerShellBoundUnaryExpression(
        SourceSpan span,
        PowerShellBoundUnaryOperator operation,
        PowerShellBoundExpression operand,
        PowerShellTypeFact type)
        : base(span, type, PowerShellValueState.Unknown)
    {
        Operation = operation;
        Operand = operand;
    }

    internal PowerShellBoundUnaryOperator Operation { get; }
    internal PowerShellBoundExpression Operand { get; }
}

internal sealed class PowerShellBoundTypeTestExpression : PowerShellBoundExpression
{
    internal PowerShellBoundTypeTestExpression(SourceSpan span, PowerShellBoundExpression operand, Type targetType, bool negate)
        : base(span, new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "A statically resolved CLR type test returns Boolean."), PowerShellValueState.Known)
    {
        Operand = operand;
        TargetType = targetType;
        Negate = negate;
    }

    internal PowerShellBoundExpression Operand { get; }
    internal Type TargetType { get; }
    internal bool Negate { get; }
}

internal enum PowerShellBoundRegexOperation
{
    Match,
    NotMatch,
    Replace
}

internal sealed class PowerShellBoundRegexExpression : PowerShellBoundExpression
{
    internal PowerShellBoundRegexExpression(
        SourceSpan span,
        PowerShellBoundRegexOperation operation,
        PowerShellBoundExpression input,
        PowerShellBoundExpression pattern,
        PowerShellBoundExpression? replacement,
        bool ignoreCase)
        : base(
            span,
            new PowerShellTypeFact(operation == PowerShellBoundRegexOperation.Replace ? typeof(string) : typeof(bool), PowerShellTypeFactProvenance.Inferred, "The regex operator binds one invariant direct Regex operation."),
            PowerShellValueState.Unknown)
    {
        Operation = operation;
        Input = input;
        Pattern = pattern;
        Replacement = replacement;
        IgnoreCase = ignoreCase;
    }

    internal PowerShellBoundRegexOperation Operation { get; }
    internal PowerShellBoundExpression Input { get; }
    internal PowerShellBoundExpression Pattern { get; }
    internal PowerShellBoundExpression? Replacement { get; }
    internal bool IgnoreCase { get; }
}
