namespace PowerForge;

internal enum PowerShellCommandDiscoveryErrorAction
{
    Ignore,
    SilentlyContinue
}

internal sealed class PowerShellBoundCommandAvailabilityExpression : PowerShellBoundExpression
{
    internal PowerShellBoundCommandAvailabilityExpression(
        SourceSpan span,
        PowerShellBoundExpression name,
        PowerShellCommandDiscoveryErrorAction errorAction,
        PowerShellCompilationCommandProviderContract provider)
        : base(
            span,
            new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "Bounded command discovery produces one Boolean availability result."),
            PowerShellValueState.Known,
            PowerShellSemanticEffect.Host | PowerShellSemanticEffect.NonSuccessStream | name.Effects,
            PowerShellRequiredCapability.CommandRegion | name.Capabilities)
    {
        Name = name;
        ErrorAction = errorAction;
        Provider = provider;
    }

    internal PowerShellBoundExpression Name { get; }
    internal PowerShellCommandDiscoveryErrorAction ErrorAction { get; }
    internal PowerShellCompilationCommandProviderContract Provider { get; }
}
