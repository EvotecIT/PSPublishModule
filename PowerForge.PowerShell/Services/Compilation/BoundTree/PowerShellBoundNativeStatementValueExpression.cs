namespace PowerForge;

/// <summary>Captures a compiled statement block as a value without creating an authored variable.</summary>
internal sealed class PowerShellBoundNativeStatementValueExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeStatementValueExpression(SourceSpan span, PowerShellBoundBlock body)
        : base(span, PowerShellTypeFact.Unknown, PowerShellValueState.Unknown,
            body.Effects & ~PowerShellSemanticEffect.SuccessOutput,
            body.Capabilities | PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellStreams |
                PowerShellRequiredCapability.PowerShellStatementErrors)
        => Body = body;

    internal PowerShellBoundBlock Body { get; }
}
