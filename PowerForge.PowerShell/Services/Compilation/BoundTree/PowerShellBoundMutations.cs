namespace PowerForge;

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
        bool checkedIntegral)
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
        CheckedIntegral = checkedIntegral;
    }

    internal PowerShellSymbolId Target { get; }
    internal Type TargetClrType { get; }
    internal PowerShellBoundMutationOperator Operation { get; }
    internal PowerShellBoundExpression? Value { get; }
    internal bool NormalizeNullString { get; }
    internal bool CheckedIntegral { get; }
}
