namespace PowerForge;

internal enum PowerShellOutputCaptureKind
{
    CollapsedPowerShellValue,
    StableScalarVector,
    NativeObjectArray
}

/// <summary>Collects an authored statement's success records before assigning its collapsed result.</summary>
internal sealed class PowerShellBoundOutputCaptureStatement : PowerShellBoundStatement
{
    internal PowerShellBoundOutputCaptureStatement(SourceSpan span, PowerShellSymbolId? target, PowerShellBoundBlock body,
        PowerShellNativeAssignmentTarget? nativeTarget = null, PowerShellBoundMutationOperator operation = PowerShellBoundMutationOperator.Assign,
        PowerShellOutputCaptureKind kind = PowerShellOutputCaptureKind.CollapsedPowerShellValue, Type? capturedElementType = null,
        bool shareEmptyArray = false)
        : base(span, (body.Effects & ~PowerShellSemanticEffect.SuccessOutput) | PowerShellSemanticEffect.Mutation |
            (nativeTarget is not null ? PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError : 0),
            (kind == PowerShellOutputCaptureKind.StableScalarVector
                ? body.Capabilities & ~PowerShellRequiredCapability.PowerShellStreams
                : body.Capabilities | PowerShellRequiredCapability.PowerShellStreams) |
            (nativeTarget is not null ? PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
                PowerShellRequiredCapability.PowerShellStatementErrors : 0))
    {
        if (nativeTarget is null && (target is null || operation != PowerShellBoundMutationOperator.Assign))
            throw new ArgumentException("A non-native capture requires a local destination and simple assignment.");
        if (kind == PowerShellOutputCaptureKind.StableScalarVector &&
            (nativeTarget is not null || capturedElementType is null ||
             !PowerShellStableScalarTypePolicy.IsSupported(capturedElementType)))
            throw new ArgumentException("A stable scalar vector capture requires one local target and a stable scalar element type.");
        if (kind == PowerShellOutputCaptureKind.CollapsedPowerShellValue && capturedElementType is not null)
            throw new ArgumentException("A collapsed PowerShell capture cannot declare a vector element type.");
        if (kind == PowerShellOutputCaptureKind.NativeObjectArray && (nativeTarget is null || capturedElementType is not null))
            throw new ArgumentException("A native object-array capture requires one native assignment target.");
        Target = target;
        Body = body;
        NativeTarget = nativeTarget;
        Operation = operation;
        Kind = kind;
        CapturedElementType = capturedElementType;
        ShareEmptyArray = shareEmptyArray;
    }

    internal PowerShellSymbolId? Target { get; }
    internal PowerShellBoundBlock Body { get; }
    internal PowerShellNativeAssignmentTarget? NativeTarget { get; }
    internal PowerShellBoundMutationOperator Operation { get; }
    internal PowerShellOutputCaptureKind Kind { get; }
    internal Type? CapturedElementType { get; }
    internal bool ShareEmptyArray { get; }
    internal Type? CapturedVectorType => CapturedElementType?.MakeArrayType();
    internal bool UsesNativeInvocation => NativeTarget is not null;
    internal bool CapturesStableScalarVector => Kind == PowerShellOutputCaptureKind.StableScalarVector;
}
