namespace PowerForge;

internal enum PowerShellBoundArrayKind
{
    Literal,
    CollectedExpression
}

internal sealed class PowerShellBoundArrayExpression : PowerShellBoundExpression
{
    internal PowerShellBoundArrayExpression(
        SourceSpan span,
        Type arrayType,
        PowerShellBoundArrayKind kind,
        PowerShellBoundExpression[] elements)
        : base(
            span,
            new PowerShellTypeFact(arrayType, PowerShellTypeFactProvenance.Inferred, "The bound array element contract selects one CLR array representation."),
            PowerShellValueState.Known)
    {
        Kind = kind;
        Elements = elements;
    }

    internal PowerShellBoundArrayKind Kind { get; }
    internal PowerShellBoundExpression[] Elements { get; }
}
