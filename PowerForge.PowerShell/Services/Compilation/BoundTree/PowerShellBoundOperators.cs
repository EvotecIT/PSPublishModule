namespace PowerForge;

internal enum PowerShellBoundBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
    PowerShellScalarFormat,
    RuntimeFreeScalarFormat,
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
    ShiftRight,
    IntegralRemainder,
    PromotingAdd,
    PromotingSubtract,
    PromotingMultiply,
    NumericUnionFloatingDivide,
    NumericUnionFloatingRemainder,
    NumericUnionNonzeroDivide,
    NumericUnionNonzeroRemainder,
    NumericUnionEqual,
    NumericUnionNotEqual,
    NumericUnionLessThan,
    NumericUnionLessThanOrEqual,
    NumericUnionGreaterThan,
    NumericUnionGreaterThanOrEqual,
    NativeStringConcatenate
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
        PowerShellTypeFact type,
        bool preserveStatementErrors = false,
        bool usesNativeInvocation = false,
        bool nativeIgnoreCase = true)
        : base(
            span,
            type,
            operation == PowerShellBoundBinaryOperator.NativeStringConcatenate ? PowerShellValueState.Known : PowerShellValueState.Unknown,
            left.Effects | right.Effects | (preserveStatementErrors || operation == PowerShellBoundBinaryOperator.PowerShellScalarFormat
                ? PowerShellSemanticEffect.TerminatingError : PowerShellSemanticEffect.None) |
                (usesNativeInvocation || operation == PowerShellBoundBinaryOperator.NativeStringConcatenate
                    ? PowerShellSemanticEffect.Host | PowerShellSemanticEffect.Mutation | PowerShellSemanticEffect.TerminatingError
                    : PowerShellSemanticEffect.None),
            left.Capabilities | right.Capabilities | GetRequiredCapabilities(operation) |
                (usesNativeInvocation ? PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
                    PowerShellRequiredCapability.PowerShellStatementErrors : PowerShellRequiredCapability.None) |
                (preserveStatementErrors ? PowerShellRequiredCapability.PowerShellStatementErrors : PowerShellRequiredCapability.None))
    {
        Operation = operation;
        Left = left;
        Right = right;
        PreserveStatementErrors = preserveStatementErrors;
        UsesNativeInvocation = usesNativeInvocation;
        NativeIgnoreCase = nativeIgnoreCase;
    }

    internal PowerShellBoundBinaryOperator Operation { get; }
    internal PowerShellBoundExpression Left { get; }
    internal PowerShellBoundExpression Right { get; }
    internal bool PreserveStatementErrors { get; }
    internal bool UsesNativeInvocation { get; }
    internal bool NativeIgnoreCase { get; }

    internal static bool RequiresPowerShellLanguageRuntime(PowerShellBoundBinaryOperator operation)
        => GetRequiredCapabilities(operation).HasFlag(PowerShellRequiredCapability.PowerShellLanguageOperators);

    private static PowerShellRequiredCapability GetRequiredCapabilities(PowerShellBoundBinaryOperator operation)
        => operation == PowerShellBoundBinaryOperator.NativeStringConcatenate
            ? PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost | PowerShellRequiredCapability.PowerShellStatementErrors
            : operation == PowerShellBoundBinaryOperator.PowerShellScalarFormat
            ? PowerShellRequiredCapability.PowerShellStatementErrors
            : operation is PowerShellBoundBinaryOperator.PowerShellEqualIgnoreCase or
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
    internal PowerShellBoundStringJoinExpression(SourceSpan span, PowerShellBoundExpression values, PowerShellBoundExpression separator,
        string? nativeSourcePath = null, string nativeSourceText = "", bool isUnary = false)
        : base(
            span,
            new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Inferred, "The join operator produces a string using its selected conversion and enumeration contract."),
            PowerShellValueState.Known,
            values.Effects | separator.Effects | (nativeSourcePath is not null
                ? PowerShellSemanticEffect.Host | PowerShellSemanticEffect.Mutation | PowerShellSemanticEffect.TerminatingError
                : PowerShellSemanticEffect.None),
            values.Capabilities | separator.Capabilities | (nativeSourcePath is not null
                ? PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost | PowerShellRequiredCapability.PowerShellStatementErrors
                : PowerShellRequiredCapability.None))
    {
        Values = values;
        Separator = separator;
        NativeSourcePath = nativeSourcePath;
        NativeSourceText = nativeSourceText;
        IsUnary = isUnary;
    }

    internal PowerShellBoundExpression Values { get; }
    internal PowerShellBoundExpression Separator { get; }
    internal string? NativeSourcePath { get; }
    internal string NativeSourceText { get; }
    internal bool IsUnary { get; }
}
