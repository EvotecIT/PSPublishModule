namespace PowerForge;

/// <summary>Collects an authored statement's success records before assigning its collapsed result.</summary>
internal sealed class PowerShellBoundOutputCaptureStatement : PowerShellBoundStatement
{
    internal PowerShellBoundOutputCaptureStatement(SourceSpan span, PowerShellSymbolId? target, PowerShellBoundBlock body,
        PowerShellNativeAssignmentTarget? nativeTarget = null, PowerShellBoundMutationOperator operation = PowerShellBoundMutationOperator.Assign)
        : base(span, (body.Effects & ~PowerShellSemanticEffect.SuccessOutput) | PowerShellSemanticEffect.Mutation |
            (nativeTarget is not null ? PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError : 0),
            body.Capabilities | PowerShellRequiredCapability.PowerShellStreams |
            (nativeTarget is not null ? PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
                PowerShellRequiredCapability.PowerShellStatementErrors : 0))
    {
        if (nativeTarget is null && (target is null || operation != PowerShellBoundMutationOperator.Assign))
            throw new ArgumentException("A non-native capture requires a local destination and simple assignment.");
        Target = target;
        Body = body;
        NativeTarget = nativeTarget;
        Operation = operation;
    }

    internal PowerShellSymbolId? Target { get; }
    internal PowerShellBoundBlock Body { get; }
    internal PowerShellNativeAssignmentTarget? NativeTarget { get; }
    internal PowerShellBoundMutationOperator Operation { get; }
    internal bool UsesNativeInvocation => NativeTarget is not null;
}
