namespace PowerForge;

/// <summary>Captures a compiled statement block as a value without creating an authored variable.</summary>
internal sealed class PowerShellBoundNativeStatementValueExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeStatementValueExpression(SourceSpan span, PowerShellBoundBlock body,
        bool preserveRecords = false, bool shareEmptyResult = false)
        : base(span, preserveRecords
                ? new PowerShellTypeFact(typeof(object[]), PowerShellTypeFactProvenance.Inferred,
                    "An authored array subexpression preserves every collected success record.")
                : PowerShellTypeFact.Unknown, PowerShellValueState.Unknown,
            body.Effects & ~PowerShellSemanticEffect.SuccessOutput,
            body.Capabilities | PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellStreams |
                PowerShellRequiredCapability.PowerShellStatementErrors)
    {
        Body = body;
        PreserveRecords = preserveRecords;
        ShareEmptyResult = shareEmptyResult;
    }

    internal PowerShellBoundBlock Body { get; }
    internal bool PreserveRecords { get; }
    internal bool ShareEmptyResult { get; }
}
