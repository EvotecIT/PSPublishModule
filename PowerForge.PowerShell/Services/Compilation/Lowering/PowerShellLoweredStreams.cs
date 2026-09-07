namespace PowerForge;

internal sealed class PowerShellLoweredStreamWriteStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredStreamWriteStatement(
        SourceSpan span,
        PowerShellStreamCommandKind kind,
        PowerShellCompilationCommandProviderContract? provider,
        PowerShellLoweredExpression message,
        bool enumerateAuthoredArray = false,
        PowerShellOutputBindingKind outputBinding = PowerShellOutputBindingKind.Default)
        : base(span)
    {
        Kind = kind;
        Provider = provider;
        Message = message;
        EnumerateAuthoredArray = enumerateAuthoredArray;
        OutputBinding = outputBinding;
    }

    internal PowerShellStreamCommandKind Kind { get; }
    internal PowerShellCompilationCommandProviderContract? Provider { get; }
    internal PowerShellLoweredExpression Message { get; }
    internal bool EnumerateAuthoredArray { get; }
    internal PowerShellOutputBindingKind OutputBinding { get; }
}
