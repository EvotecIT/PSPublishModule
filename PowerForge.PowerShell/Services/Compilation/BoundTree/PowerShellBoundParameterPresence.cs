namespace PowerForge;

internal sealed class PowerShellBoundParameterPresenceExpression : PowerShellBoundExpression
{
    internal PowerShellBoundParameterPresenceExpression(SourceSpan span, string parameterName)
        : base(
            span,
            new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "$PSBoundParameters.ContainsKey returns Boolean binding-presence state."),
            PowerShellValueState.Known,
            PowerShellSemanticEffect.None,
            PowerShellRequiredCapability.None)
    {
        ParameterName = parameterName;
    }

    internal string ParameterName { get; }
}
