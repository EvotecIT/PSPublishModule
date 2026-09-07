namespace PowerForge;

/// <summary>Collects an authored statement's success records before assigning its collapsed result.</summary>
internal sealed class PowerShellBoundOutputCaptureStatement : PowerShellBoundStatement
{
    internal PowerShellBoundOutputCaptureStatement(SourceSpan span, PowerShellSymbolId target, PowerShellBoundBlock body)
        : base(span, (body.Effects & ~PowerShellSemanticEffect.SuccessOutput) | PowerShellSemanticEffect.Mutation,
            body.Capabilities | PowerShellRequiredCapability.PowerShellStreams)
    {
        Target = target;
        Body = body;
    }

    internal PowerShellSymbolId Target { get; }
    internal PowerShellBoundBlock Body { get; }
}
