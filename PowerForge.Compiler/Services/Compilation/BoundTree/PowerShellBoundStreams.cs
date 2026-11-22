namespace PowerForge;

internal enum PowerShellOutputBindingKind
{
    Default,
    NoEnumerate,
    PositionalNoEnumerate,
    PositionalNoEnumeratePowerShell7
}

internal sealed class PowerShellBoundStreamWriteStatement : PowerShellBoundStatement
{
    internal PowerShellBoundStreamWriteStatement(
        SourceSpan span,
        PowerShellStreamCommandKind kind,
        PowerShellCompilationCommandProviderContract? provider,
        PowerShellBoundExpression message,
        PowerShellOutputBindingKind outputBinding = PowerShellOutputBindingKind.Default,
        bool usesNativeInvocation = false,
        bool usesCommandHostEnumeration = false,
        bool runtimeFree = false)
        : base(
            span,
            (kind == PowerShellStreamCommandKind.Success
                ? PowerShellSemanticEffect.SuccessOutput
                : PowerShellSemanticEffect.NonSuccessStream) | message.Effects |
                (usesNativeInvocation || usesCommandHostEnumeration ? PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError : 0),
            (runtimeFree ? PowerShellRequiredCapability.RuntimeFreeSuccessOutput :
                provider is { Adapter.RuntimeFree: true, Adapter.EntryPoint: not null }
                ? PowerShellRequiredCapability.RuntimeFreeProviderOperations
                : PowerShellRequiredCapability.PowerShellStreams) | message.Capabilities |
                (usesNativeInvocation ? PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
                    PowerShellRequiredCapability.PowerShellStatementErrors : 0) |
                (usesCommandHostEnumeration ? PowerShellRequiredCapability.PowerShellHost | PowerShellRequiredCapability.PowerShellStatementErrors : 0))
    {
        if (provider is null && kind != PowerShellStreamCommandKind.Success)
            throw new ArgumentException("Only implicit success output can omit a command provider.", nameof(provider));
        if (runtimeFree && (provider is not null || usesNativeInvocation || usesCommandHostEnumeration))
            throw new ArgumentException("Runtime-free implicit output uses only the CLR success stream.", nameof(runtimeFree));
        RuntimeFree = runtimeFree;
        Kind = kind;
        Provider = provider;
        Message = message;
        OutputBinding = outputBinding;
        UsesNativeInvocation = usesNativeInvocation;
        UsesCommandHostEnumeration = usesCommandHostEnumeration;
    }

    internal PowerShellStreamCommandKind Kind { get; }
    /// <summary>Null for language-owned implicit success output, which cannot be shadowed by a command.</summary>
    internal PowerShellCompilationCommandProviderContract? Provider { get; }
    internal PowerShellBoundExpression Message { get; }
    internal PowerShellOutputBindingKind OutputBinding { get; }
    internal bool UsesNativeInvocation { get; }
    internal bool UsesCommandHostEnumeration { get; }
    internal bool RuntimeFree { get; }
}
