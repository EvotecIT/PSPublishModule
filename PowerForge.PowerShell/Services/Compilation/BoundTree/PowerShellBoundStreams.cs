namespace PowerForge;

internal sealed class PowerShellBoundStreamWriteStatement : PowerShellBoundStatement
{
    internal PowerShellBoundStreamWriteStatement(
        SourceSpan span,
        PowerShellStreamCommandKind kind,
        PowerShellCompilationCommandProviderContract provider,
        PowerShellBoundExpression message)
        : base(
            span,
            (kind == PowerShellStreamCommandKind.Success
                ? PowerShellSemanticEffect.SuccessOutput
                : PowerShellSemanticEffect.NonSuccessStream) | message.Effects,
            (provider.Adapter.RuntimeFree && provider.Adapter.EntryPoint is not null
                ? PowerShellRequiredCapability.RuntimeFreeProviderOperations
                : PowerShellRequiredCapability.PowerShellStreams) | message.Capabilities)
    {
        Kind = kind;
        Provider = provider;
        Message = message;
    }

    internal PowerShellStreamCommandKind Kind { get; }
    internal PowerShellCompilationCommandProviderContract Provider { get; }
    internal PowerShellBoundExpression Message { get; }
}
