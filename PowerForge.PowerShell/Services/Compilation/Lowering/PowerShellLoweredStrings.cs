namespace PowerForge;

internal sealed class PowerShellLoweredInterpolatedStringPart
{
    internal PowerShellLoweredInterpolatedStringPart(string? text, PowerShellLoweredExpression? expression, string numericTemporary = "")
    {
        Text = text;
        Expression = expression;
        NumericTemporary = numericTemporary;
    }

    internal string? Text { get; }
    internal PowerShellLoweredExpression? Expression { get; }
    internal string NumericTemporary { get; }
}

internal sealed class PowerShellLoweredInterpolatedStringExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredInterpolatedStringExpression(SourceSpan span, PowerShellLoweredInterpolatedStringPart[] parts, bool usesNativeInvocation = false)
        : base(span, typeof(string))
    {
        Parts = parts;
        UsesNativeInvocation = usesNativeInvocation;
    }

    internal PowerShellImmutableArray<PowerShellLoweredInterpolatedStringPart> Parts { get; }
    internal bool UsesNativeInvocation { get; }
}
