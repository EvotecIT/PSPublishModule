namespace PowerForge;

internal sealed class PowerShellBoundStreamWriteStatement : PowerShellBoundStatement
{
    internal PowerShellBoundStreamWriteStatement(
        SourceSpan span,
        PowerShellStreamCommandKind kind,
        PowerShellCompilationCommandProviderContract? provider,
        PowerShellBoundExpression message)
        : base(
            span,
            (kind == PowerShellStreamCommandKind.Success
                ? PowerShellSemanticEffect.SuccessOutput
                : PowerShellSemanticEffect.NonSuccessStream) | message.Effects,
            (provider is { Adapter.RuntimeFree: true, Adapter.EntryPoint: not null }
                ? PowerShellRequiredCapability.RuntimeFreeProviderOperations
                : PowerShellRequiredCapability.PowerShellStreams) | message.Capabilities)
    {
        if (provider is null && kind != PowerShellStreamCommandKind.Success)
            throw new ArgumentException("Only implicit success output can omit a command provider.", nameof(provider));
        Kind = kind;
        Provider = provider;
        Message = message;
    }

    internal PowerShellStreamCommandKind Kind { get; }
    /// <summary>Null for language-owned implicit success output, which cannot be shadowed by a command.</summary>
    internal PowerShellCompilationCommandProviderContract? Provider { get; }
    internal PowerShellBoundExpression Message { get; }
}
