namespace PowerForge;

/// <summary>Owns the result slot and temporary success sink for a lowered capture block.</summary>
internal sealed class PowerShellLoweredOutputCaptureStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredOutputCaptureStatement(SourceSpan span, PowerShellSymbolId? target,
        PowerShellLoweredStatement[] statements, string recordsTemporary, string sinkTemporary,
        PowerShellNativeAssignmentTarget? nativeTarget = null, PowerShellBoundMutationOperator operation = PowerShellBoundMutationOperator.Assign,
        PowerShellOutputCaptureKind kind = PowerShellOutputCaptureKind.CollapsedPowerShellValue, Type? capturedElementType = null,
        bool declareTarget = false, bool shareEmptyArray = false)
        : base(span)
    {
        if (nativeTarget is null && (target is null || operation != PowerShellBoundMutationOperator.Assign))
            throw new ArgumentException("A non-native capture requires a local destination and simple assignment.");
        Target = target;
        Statements = statements;
        RecordsTemporary = recordsTemporary;
        SinkTemporary = sinkTemporary;
        NativeTarget = nativeTarget;
        Operation = operation;
        Kind = kind;
        CapturedElementType = capturedElementType;
        DeclareTarget = declareTarget;
        ShareEmptyArray = shareEmptyArray;
    }

    internal PowerShellSymbolId? Target { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal string RecordsTemporary { get; }
    internal string SinkTemporary { get; }
    internal PowerShellNativeAssignmentTarget? NativeTarget { get; }
    internal PowerShellBoundMutationOperator Operation { get; }
    internal PowerShellOutputCaptureKind Kind { get; }
    internal Type? CapturedElementType { get; }
    internal bool DeclareTarget { get; }
    internal bool ShareEmptyArray { get; }
    internal bool UsesNativeInvocation => NativeTarget is not null;
    internal bool CapturesStableScalarVector => Kind == PowerShellOutputCaptureKind.StableScalarVector;
}
