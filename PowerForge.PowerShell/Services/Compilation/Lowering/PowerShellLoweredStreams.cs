namespace PowerForge;

internal sealed class PowerShellLoweredStreamWriteStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredStreamWriteStatement(
        SourceSpan span,
        PowerShellStreamCommandKind kind,
        PowerShellLoweredExpression message)
        : base(span)
    {
        Kind = kind;
        Message = message;
    }

    internal PowerShellStreamCommandKind Kind { get; }
    internal PowerShellLoweredExpression Message { get; }
}
