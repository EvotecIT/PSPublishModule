namespace PowerForge;

internal sealed class PowerShellBoundInterpolatedStringPart
{
    internal PowerShellBoundInterpolatedStringPart(string? text, PowerShellBoundExpression? expression, bool isInt32OrDouble = false)
    {
        Text = text;
        Expression = expression;
        IsInt32OrDouble = isInt32OrDouble;
    }

    internal string? Text { get; }
    internal PowerShellBoundExpression? Expression { get; }
    internal bool IsInt32OrDouble { get; }
}

internal sealed class PowerShellBoundInterpolatedStringExpression : PowerShellBoundExpression
{
    internal PowerShellBoundInterpolatedStringExpression(SourceSpan span, PowerShellBoundInterpolatedStringPart[] parts, bool usesNativeInvocation = false)
        : base(
            span,
            new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Inferred, "Every interpolation is statically represented as a String."),
            PowerShellValueState.Known,
            parts.Where(static part => part.Expression is not null)
                .Aggregate(usesNativeInvocation ? PowerShellSemanticEffect.Host | PowerShellSemanticEffect.Mutation | PowerShellSemanticEffect.TerminatingError : PowerShellSemanticEffect.None,
                    static (effects, part) => effects | part.Expression!.Effects),
            parts.Where(static part => part.Expression is not null)
                .Aggregate(usesNativeInvocation ? PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellStatementErrors : PowerShellRequiredCapability.None,
                    static (capabilities, part) => capabilities | part.Expression!.Capabilities))
    {
        Parts = parts;
        UsesNativeInvocation = usesNativeInvocation;
    }

    internal PowerShellImmutableArray<PowerShellBoundInterpolatedStringPart> Parts { get; }
    internal bool UsesNativeInvocation { get; }
}
