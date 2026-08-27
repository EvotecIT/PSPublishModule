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

internal sealed class PowerShellLoweredTypeTestExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredTypeTestExpression(SourceSpan span, PowerShellLoweredExpression operand, Type targetType, bool negate)
        : base(span, typeof(bool))
    {
        Operand = operand;
        TargetType = targetType;
        Negate = negate;
    }

    internal PowerShellLoweredExpression Operand { get; }
    internal Type TargetType { get; }
    internal bool Negate { get; }
}

internal sealed class PowerShellLoweredRegexExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredRegexExpression(SourceSpan span, Type clrType, PowerShellBoundRegexOperation operation, PowerShellLoweredExpression input, PowerShellLoweredExpression pattern, PowerShellLoweredExpression? replacement, bool ignoreCase)
        : base(span, clrType)
    {
        Operation = operation;
        Input = input;
        Pattern = pattern;
        Replacement = replacement;
        IgnoreCase = ignoreCase;
    }

    internal PowerShellBoundRegexOperation Operation { get; }
    internal PowerShellLoweredExpression Input { get; }
    internal PowerShellLoweredExpression Pattern { get; }
    internal PowerShellLoweredExpression? Replacement { get; }
    internal bool IgnoreCase { get; }
}
