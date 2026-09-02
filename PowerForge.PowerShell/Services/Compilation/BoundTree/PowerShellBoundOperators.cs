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
    NullEqual,
    NullNotEqual,
    EqualIgnoreCase,
    NotEqualIgnoreCase,
    EqualCaseSensitive,
    NotEqualCaseSensitive,
    PowerShellEqualIgnoreCase,
    PowerShellNotEqualIgnoreCase,
    PowerShellEqualCaseSensitive,
    PowerShellNotEqualCaseSensitive,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    NullOrderedLessThan,
    NullOrderedLessThanOrEqual,
    NullOrderedGreaterThan,
    NullOrderedGreaterThanOrEqual,
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
        : base(
            span,
            type,
            PowerShellValueState.Unknown,
            left.Effects | right.Effects,
            left.Capabilities | right.Capabilities | GetRequiredCapabilities(operation))
    {
        Operation = operation;
        Left = left;
        Right = right;
    }

    internal PowerShellBoundBinaryOperator Operation { get; }
    internal PowerShellBoundExpression Left { get; }
    internal PowerShellBoundExpression Right { get; }

    internal static bool RequiresPowerShellLanguageRuntime(PowerShellBoundBinaryOperator operation)
        => GetRequiredCapabilities(operation).HasFlag(PowerShellRequiredCapability.PowerShellLanguageOperators);

    private static PowerShellRequiredCapability GetRequiredCapabilities(PowerShellBoundBinaryOperator operation)
        => operation is PowerShellBoundBinaryOperator.PowerShellEqualIgnoreCase or
            PowerShellBoundBinaryOperator.PowerShellNotEqualIgnoreCase or
            PowerShellBoundBinaryOperator.PowerShellEqualCaseSensitive or
            PowerShellBoundBinaryOperator.PowerShellNotEqualCaseSensitive
                ? PowerShellRequiredCapability.PowerShellLanguageOperators
                : PowerShellRequiredCapability.None;
}

internal sealed class PowerShellBoundUnaryExpression : PowerShellBoundExpression
{
    internal PowerShellBoundUnaryExpression(
        SourceSpan span,
        PowerShellBoundUnaryOperator operation,
        PowerShellBoundExpression operand,
        PowerShellTypeFact type)
        : base(span, type, PowerShellValueState.Unknown, operand.Effects, operand.Capabilities)
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
        : base(span, new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "A statically resolved CLR type test returns Boolean."), PowerShellValueState.Known, operand.Effects, operand.Capabilities)
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
            PowerShellValueState.Unknown,
            input.Effects | pattern.Effects | (replacement?.Effects ?? PowerShellSemanticEffect.None),
            input.Capabilities | pattern.Capabilities | (replacement?.Capabilities ?? PowerShellRequiredCapability.None))
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

internal sealed class PowerShellBoundWildcardExpression : PowerShellBoundExpression
{
    internal PowerShellBoundWildcardExpression(
        SourceSpan span,
        PowerShellBoundExpression input,
        PowerShellBoundExpression pattern,
        bool ignoreCase,
        bool negate)
        : base(
            span,
            new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "The wildcard operator binds one PowerShell-hosted WildcardPattern operation."),
            PowerShellValueState.Unknown,
            input.Effects | pattern.Effects,
            input.Capabilities | pattern.Capabilities | PowerShellRequiredCapability.PowerShellLanguageOperators)
    {
        Input = input;
        Pattern = pattern;
        IgnoreCase = ignoreCase;
        Negate = negate;
    }

    internal PowerShellBoundExpression Input { get; }
    internal PowerShellBoundExpression Pattern { get; }
    internal bool IgnoreCase { get; }
    internal bool Negate { get; }
}

internal sealed class PowerShellBoundMembershipExpression : PowerShellBoundExpression
{
    internal PowerShellBoundMembershipExpression(
        SourceSpan span,
        PowerShellBoundExpression left,
        PowerShellBoundExpression right,
        Type elementType,
        bool collectionOnRight,
        bool ignoreCase,
        bool negate)
        : base(
            span,
            new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "The membership operator binds one invariant PowerShell LanguagePrimitives comparison."),
            PowerShellValueState.Unknown,
            left.Effects | right.Effects,
            left.Capabilities | right.Capabilities | PowerShellRequiredCapability.PowerShellLanguageOperators)
    {
        Left = left;
        Right = right;
        ElementType = elementType;
        CollectionOnRight = collectionOnRight;
        IgnoreCase = ignoreCase;
        Negate = negate;
    }

    internal PowerShellBoundExpression Left { get; }
    internal PowerShellBoundExpression Right { get; }
    internal Type ElementType { get; }
    internal bool CollectionOnRight { get; }
    internal bool IgnoreCase { get; }
    internal bool Negate { get; }
}

internal sealed class PowerShellBoundStringSplitExpression : PowerShellBoundExpression
{
    internal PowerShellBoundStringSplitExpression(SourceSpan span, PowerShellBoundExpression input, PowerShellBoundExpression pattern, bool ignoreCase)
        : base(
            span,
            new PowerShellTypeFact(typeof(string[]), PowerShellTypeFactProvenance.Inferred, "The string split operator binds one Regex.Split operation."),
            PowerShellValueState.Known,
            input.Effects | pattern.Effects,
            input.Capabilities | pattern.Capabilities)
    {
        Input = input;
        Pattern = pattern;
        IgnoreCase = ignoreCase;
    }

    internal PowerShellBoundExpression Input { get; }
    internal PowerShellBoundExpression Pattern { get; }
    internal bool IgnoreCase { get; }
}

internal sealed class PowerShellBoundStringJoinExpression : PowerShellBoundExpression
{
    internal PowerShellBoundStringJoinExpression(SourceSpan span, PowerShellBoundExpression values, PowerShellBoundExpression separator)
        : base(
            span,
            new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Inferred, "The string join operator binds one String.Join operation."),
            PowerShellValueState.Known,
            values.Effects | separator.Effects,
            values.Capabilities | separator.Capabilities)
    {
        Values = values;
        Separator = separator;
    }

    internal PowerShellBoundExpression Values { get; }
    internal PowerShellBoundExpression Separator { get; }
}
