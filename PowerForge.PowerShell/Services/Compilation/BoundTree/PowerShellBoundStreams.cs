namespace PowerForge;

internal sealed class PowerShellBoundStreamWriteStatement : PowerShellBoundStatement
{
    internal PowerShellBoundStreamWriteStatement(
        SourceSpan span,
        PowerShellStreamCommandKind kind,
        PowerShellBoundExpression message)
        : base(
            span,
            PowerShellSemanticEffect.NonSuccessStream | message.Effects,
            PowerShellRequiredCapability.PowerShellStreams | message.Capabilities)
    {
        Kind = kind;
        Message = message;
    }

    internal PowerShellStreamCommandKind Kind { get; }
    internal PowerShellBoundExpression Message { get; }
}
