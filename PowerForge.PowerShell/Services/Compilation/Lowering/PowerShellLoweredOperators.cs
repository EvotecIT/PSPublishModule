namespace PowerForge;

internal sealed class PowerShellLoweredBinaryExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredBinaryExpression(SourceSpan span, Type clrType, PowerShellBoundBinaryOperator operation, PowerShellLoweredExpression left, PowerShellLoweredExpression right)
        : base(span, clrType)
    {
        Operation = operation;
        Left = left;
        Right = right;
    }

    internal PowerShellBoundBinaryOperator Operation { get; }
    internal PowerShellLoweredExpression Left { get; }
    internal PowerShellLoweredExpression Right { get; }
}

internal sealed class PowerShellLoweredUnaryExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredUnaryExpression(SourceSpan span, Type clrType, PowerShellBoundUnaryOperator operation, PowerShellLoweredExpression operand)
        : base(span, clrType)
    {
        Operation = operation;
        Operand = operand;
    }

    internal PowerShellBoundUnaryOperator Operation { get; }
    internal PowerShellLoweredExpression Operand { get; }
}
