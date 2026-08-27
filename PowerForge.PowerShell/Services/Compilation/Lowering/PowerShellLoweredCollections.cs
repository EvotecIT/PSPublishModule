namespace PowerForge;

internal sealed class PowerShellLoweredArrayExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredArrayExpression(SourceSpan span, Type clrType, PowerShellBoundArrayKind kind, PowerShellLoweredExpression[] elements)
        : base(span, clrType)
    {
        Kind = kind;
        Elements = elements;
    }

    internal PowerShellBoundArrayKind Kind { get; }
    internal PowerShellLoweredExpression[] Elements { get; }
}
