namespace PowerForge;

internal sealed class PowerShellLoweredInterpolatedStringPart
{
    internal PowerShellLoweredInterpolatedStringPart(string? text, PowerShellLoweredExpression? expression)
    {
        Text = text;
        Expression = expression;
    }

    internal string? Text { get; }
    internal PowerShellLoweredExpression? Expression { get; }
}

internal sealed class PowerShellLoweredInterpolatedStringExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredInterpolatedStringExpression(SourceSpan span, PowerShellLoweredInterpolatedStringPart[] parts)
        : base(span, typeof(string)) => Parts = parts;

    internal PowerShellLoweredInterpolatedStringPart[] Parts { get; }
}
