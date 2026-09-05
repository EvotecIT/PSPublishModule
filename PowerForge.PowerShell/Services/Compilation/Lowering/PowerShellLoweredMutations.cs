namespace PowerForge;

internal sealed class PowerShellLoweredMutationExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredMutationExpression(
        SourceSpan span,
        Type clrType,
        PowerShellSymbolId target,
        Type targetClrType,
        PowerShellBoundMutationOperator operation,
        PowerShellLoweredExpression? value,
        bool normalizeNullString,
        bool checkedIntegral)
        : base(span, clrType)
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
    internal PowerShellLoweredExpression? Value { get; }
    internal bool NormalizeNullString { get; }
    internal bool CheckedIntegral { get; }
}
