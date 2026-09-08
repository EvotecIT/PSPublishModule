namespace PowerForge;

/// <summary>Selected overflow evaluation and constrained-conversion contract.</summary>
internal enum PowerShellIntegralMutationSemantics
{
    None,
    CheckedConversion,
    PromotedBigIntegerProduct,
    UnconstrainedInt32OrDouble,
    UnsignedDecrement
}

internal enum PowerShellBoundMutationOperator
{
    Assign,
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
    Increment,
    Decrement,
    PostIncrement,
    PostDecrement
}

internal sealed class PowerShellBoundMutationExpression : PowerShellBoundExpression
{
    internal PowerShellBoundMutationExpression(
        SourceSpan span,
        PowerShellSymbolId target,
        Type targetClrType,
        PowerShellBoundMutationOperator operation,
        PowerShellBoundExpression? value,
        PowerShellTypeFact type,
        bool normalizeNullString,
        PowerShellIntegralMutationSemantics integralSemantics,
        bool preserveStatementErrors = false,
        PowerShellBoundNativeVariableExpression? nativeTargetRead = null,
        string nativeSourceText = "")
        : base(
            span,
            type,
            PowerShellValueState.Unknown,
            PowerShellSemanticEffect.Mutation | (value?.Effects ?? PowerShellSemanticEffect.None) |
                (preserveStatementErrors ? PowerShellSemanticEffect.TerminatingError : PowerShellSemanticEffect.None) |
                (nativeTargetRead?.Effects ?? PowerShellSemanticEffect.None),
            (value?.Capabilities ?? PowerShellRequiredCapability.None) |
                (preserveStatementErrors ? PowerShellRequiredCapability.PowerShellStatementErrors : PowerShellRequiredCapability.None) |
                (nativeTargetRead?.Capabilities ?? PowerShellRequiredCapability.None))
    {
        Target = target;
        TargetClrType = targetClrType;
        Operation = operation;
        Value = value;
        NormalizeNullString = normalizeNullString;
        IntegralSemantics = integralSemantics;
        PreserveStatementErrors = preserveStatementErrors;
        NativeTargetRead = nativeTargetRead;
        NativeSourceText = nativeSourceText;
    }

    internal PowerShellSymbolId Target { get; }
    internal Type TargetClrType { get; }
    internal PowerShellBoundMutationOperator Operation { get; }
    internal PowerShellBoundExpression? Value { get; }
    internal bool NormalizeNullString { get; }
    internal PowerShellIntegralMutationSemantics IntegralSemantics { get; }
    internal bool PreserveStatementErrors { get; }
    internal PowerShellBoundNativeVariableExpression? NativeTargetRead { get; }
    internal bool UsesNativeInvocation => NativeTargetRead is not null;
    internal string NativeSourceText { get; }

    /// <summary>Preserves the operation while selecting its expression-result representation.</summary>
    internal PowerShellBoundMutationExpression WithResultType(PowerShellTypeFact type)
        => new(Span, Target, TargetClrType, Operation, Value, type, NormalizeNullString,
            IntegralSemantics, PreserveStatementErrors, NativeTargetRead, NativeSourceText);
}
