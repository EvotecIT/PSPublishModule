namespace PowerForge;

internal sealed class PowerShellLoweredBinaryExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredBinaryExpression(
        SourceSpan span,
        Type clrType,
        PowerShellBoundBinaryOperator operation,
        PowerShellLoweredExpression left,
        PowerShellLoweredExpression right,
        string? leftTemporary,
        string? rightTemporary,
        bool preserveStatementErrors = false,
        bool usesNativeInvocation = false,
        bool nativeIgnoreCase = true,
        SourceSpan? operatorSpan = null,
        string? operatorSourceText = null)
        : base(span, clrType)
    {
        Operation = operation;
        Left = left;
        Right = right;
        LeftTemporary = leftTemporary;
        RightTemporary = rightTemporary;
        PreserveStatementErrors = preserveStatementErrors;
        UsesNativeInvocation = usesNativeInvocation;
        NativeIgnoreCase = nativeIgnoreCase;
        OperatorSpan = operatorSpan;
        OperatorSourceText = operatorSourceText;
    }

    internal PowerShellBoundBinaryOperator Operation { get; }
    internal PowerShellLoweredExpression Left { get; }
    internal PowerShellLoweredExpression Right { get; }
    internal string? LeftTemporary { get; }
    internal string? RightTemporary { get; }
    internal bool PreserveStatementErrors { get; }
    internal bool UsesNativeInvocation { get; }
    internal bool NativeIgnoreCase { get; }
    internal SourceSpan? OperatorSpan { get; }
    internal string? OperatorSourceText { get; }
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
    internal PowerShellLoweredTypeTestExpression(SourceSpan span, PowerShellLoweredExpression operand, Type targetType, bool negate, bool usesPowerShellSemantics = false)
        : base(span, typeof(bool))
    {
        Operand = operand;
        TargetType = targetType;
        Negate = negate;
        UsesPowerShellSemantics = usesPowerShellSemantics;
    }

    internal PowerShellLoweredExpression Operand { get; }
    internal Type TargetType { get; }
    internal bool Negate { get; }
    internal bool UsesPowerShellSemantics { get; }
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

internal sealed class PowerShellLoweredWildcardExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredWildcardExpression(
        SourceSpan span,
        PowerShellLoweredExpression input,
        PowerShellLoweredExpression pattern,
        bool ignoreCase,
        bool negate,
        string inputTemporary,
        string patternTemporary)
        : base(span, typeof(bool))
    {
        Input = input;
        Pattern = pattern;
        IgnoreCase = ignoreCase;
        Negate = negate;
        InputTemporary = inputTemporary;
        PatternTemporary = patternTemporary;
    }

    internal PowerShellLoweredExpression Input { get; }
    internal PowerShellLoweredExpression Pattern { get; }
    internal bool IgnoreCase { get; }
    internal bool Negate { get; }
    internal string InputTemporary { get; }
    internal string PatternTemporary { get; }
}

internal sealed class PowerShellLoweredMembershipExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredMembershipExpression(
        SourceSpan span,
        PowerShellLoweredExpression left,
        PowerShellLoweredExpression right,
        Type elementType,
        bool collectionOnRight,
        bool ignoreCase,
        bool negate,
        string leftTemporary,
        string rightTemporary,
        string itemTemporary,
        bool usesNativeInvocation = false,
        bool usesCommandHostInvocation = false)
        : base(span, typeof(bool))
    {
        Left = left;
        Right = right;
        ElementType = elementType;
        CollectionOnRight = collectionOnRight;
        IgnoreCase = ignoreCase;
        Negate = negate;
        LeftTemporary = leftTemporary;
        RightTemporary = rightTemporary;
        ItemTemporary = itemTemporary;
        UsesNativeInvocation = usesNativeInvocation;
        UsesCommandHostInvocation = usesCommandHostInvocation;
    }

    internal PowerShellLoweredExpression Left { get; }
    internal PowerShellLoweredExpression Right { get; }
    internal Type ElementType { get; }
    internal bool CollectionOnRight { get; }
    internal bool IgnoreCase { get; }
    internal bool Negate { get; }
    internal string LeftTemporary { get; }
    internal string RightTemporary { get; }
    internal string ItemTemporary { get; }
    internal bool UsesNativeInvocation { get; }
    internal bool UsesCommandHostInvocation { get; }
}

internal sealed class PowerShellLoweredStringSplitExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredStringSplitExpression(SourceSpan span, PowerShellLoweredExpression input, PowerShellLoweredExpression pattern, bool ignoreCase)
        : base(span, typeof(string[]))
    {
        Input = input;
        Pattern = pattern;
        IgnoreCase = ignoreCase;
    }

    internal PowerShellLoweredExpression Input { get; }
    internal PowerShellLoweredExpression Pattern { get; }
    internal bool IgnoreCase { get; }
}

internal sealed class PowerShellLoweredStringJoinExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredStringJoinExpression(SourceSpan span, PowerShellLoweredExpression values, PowerShellLoweredExpression separator, string valuesTemporary, string separatorTemporary,
        string? nativeSourcePath = null, string nativeSourceText = "", bool isUnary = false)
        : base(span, typeof(string))
    {
        Values = values;
        Separator = separator;
        ValuesTemporary = valuesTemporary;
        SeparatorTemporary = separatorTemporary;
        NativeSourcePath = nativeSourcePath;
        NativeSourceText = nativeSourceText;
        IsUnary = isUnary;
    }

    internal PowerShellLoweredExpression Values { get; }
    internal PowerShellLoweredExpression Separator { get; }
    internal string ValuesTemporary { get; }
    internal string SeparatorTemporary { get; }
    internal string? NativeSourcePath { get; }
    internal string NativeSourceText { get; }
    internal bool IsUnary { get; }
}
