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
