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
        PowerShellIntegralMutationSemantics integralSemantics,
        bool preserveStatementErrors = false,
        PowerShellLoweredNativeVariableExpression? nativeTargetRead = null,
        string nativeSourceText = "", bool nativeSetSequencePoint = true, PowerShellNativeAssignmentTarget? nativeAssignmentTarget = null)
        : base(span, clrType)
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
        NativeSetSequencePoint = nativeSetSequencePoint;
        NativeAssignmentTarget = nativeAssignmentTarget;
    }

    internal PowerShellSymbolId Target { get; }
    internal Type TargetClrType { get; }
    internal PowerShellBoundMutationOperator Operation { get; }
    internal PowerShellLoweredExpression? Value { get; }
    internal bool NormalizeNullString { get; }
    internal PowerShellIntegralMutationSemantics IntegralSemantics { get; }
    internal bool PreserveStatementErrors { get; }
    internal PowerShellLoweredNativeVariableExpression? NativeTargetRead { get; }
    internal string NativeSourceText { get; }
    internal bool NativeSetSequencePoint { get; }
    internal PowerShellNativeAssignmentTarget? NativeAssignmentTarget { get; }
}
