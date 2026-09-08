namespace PowerForge;

/// <summary>Collects an authored statement's success records before assigning its collapsed result.</summary>
internal sealed class PowerShellBoundOutputCaptureStatement : PowerShellBoundStatement
{
    internal PowerShellBoundOutputCaptureStatement(SourceSpan span, PowerShellSymbolId target, PowerShellBoundBlock body,
        bool usesNativeInvocation = false)
        : base(span, (body.Effects & ~PowerShellSemanticEffect.SuccessOutput) | PowerShellSemanticEffect.Mutation |
            (usesNativeInvocation ? PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError : 0),
            body.Capabilities | PowerShellRequiredCapability.PowerShellStreams |
            (usesNativeInvocation ? PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
                PowerShellRequiredCapability.PowerShellStatementErrors : 0))
    {
        Target = target;
        Body = body;
        UsesNativeInvocation = usesNativeInvocation;
    }

    internal PowerShellSymbolId Target { get; }
    internal PowerShellBoundBlock Body { get; }
    internal bool UsesNativeInvocation { get; }
}
