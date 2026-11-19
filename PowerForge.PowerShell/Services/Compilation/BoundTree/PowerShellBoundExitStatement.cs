namespace PowerForge;

internal sealed class PowerShellBoundExitStatement : PowerShellBoundStatement
{
    internal PowerShellBoundExitStatement(SourceSpan span, PowerShellBoundExpression? expression)
        : base(span, PowerShellSemanticEffect.Host | (expression?.Effects ?? PowerShellSemanticEffect.None),
            PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
            (expression?.Capabilities ?? PowerShellRequiredCapability.None)) => Expression = expression;

    internal PowerShellBoundExpression? Expression { get; }
}
