namespace PowerForge;

internal sealed class PowerShellBoundInterpolatedStringPart
{
    internal PowerShellBoundInterpolatedStringPart(string? text, PowerShellBoundExpression? expression)
    {
        Text = text;
        Expression = expression;
    }

    internal string? Text { get; }
    internal PowerShellBoundExpression? Expression { get; }
}

internal sealed class PowerShellBoundInterpolatedStringExpression : PowerShellBoundExpression
{
    internal PowerShellBoundInterpolatedStringExpression(SourceSpan span, PowerShellBoundInterpolatedStringPart[] parts)
        : base(
            span,
            new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Inferred, "Every interpolation is statically represented as a String."),
            PowerShellValueState.Known,
            parts.Where(static part => part.Expression is not null)
                .Aggregate(PowerShellSemanticEffect.None, static (effects, part) => effects | part.Expression!.Effects),
            parts.Where(static part => part.Expression is not null)
                .Aggregate(PowerShellRequiredCapability.None, static (capabilities, part) => capabilities | part.Expression!.Capabilities))
    {
        Parts = parts;
    }

    internal PowerShellBoundInterpolatedStringPart[] Parts { get; }
}
