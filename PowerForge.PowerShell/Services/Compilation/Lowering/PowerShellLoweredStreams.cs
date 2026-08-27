namespace PowerForge;

internal sealed class PowerShellLoweredStreamWriteStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredStreamWriteStatement(
        SourceSpan span,
        PowerShellStreamCommandKind kind,
        PowerShellCompilationCommandProviderContract provider,
        PowerShellLoweredExpression message)
        : base(span)
    {
        Kind = kind;
        Provider = provider;
        Message = message;
    }

    internal PowerShellStreamCommandKind Kind { get; }
    internal PowerShellCompilationCommandProviderContract Provider { get; }
    internal PowerShellLoweredExpression Message { get; }
}
