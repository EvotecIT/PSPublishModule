namespace PowerForge;

/// <summary>Selected overflow evaluation and constrained-conversion contract.</summary>
internal enum PowerShellIntegralMutationSemantics
{
    None,
    CheckedConversion,
    PromotedBigIntegerProduct
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
        PowerShellIntegralMutationSemantics integralSemantics)
        : base(
            span,
            type,
            PowerShellValueState.Unknown,
            PowerShellSemanticEffect.Mutation | (value?.Effects ?? PowerShellSemanticEffect.None),
            value?.Capabilities ?? PowerShellRequiredCapability.None)
    {
        Target = target;
        TargetClrType = targetClrType;
        Operation = operation;
        Value = value;
        NormalizeNullString = normalizeNullString;
        IntegralSemantics = integralSemantics;
    }

    internal PowerShellSymbolId Target { get; }
    internal Type TargetClrType { get; }
    internal PowerShellBoundMutationOperator Operation { get; }
    internal PowerShellBoundExpression? Value { get; }
    internal bool NormalizeNullString { get; }
    internal PowerShellIntegralMutationSemantics IntegralSemantics { get; }
}
