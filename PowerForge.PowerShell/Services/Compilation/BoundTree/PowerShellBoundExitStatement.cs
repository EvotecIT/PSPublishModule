namespace PowerForge;

internal sealed class PowerShellBoundExitStatement : PowerShellBoundStatement
{
    internal PowerShellBoundExitStatement(SourceSpan span, PowerShellBoundExpression? expression, bool runtimeFree)
        : base(span, PowerShellSemanticEffect.Host | (expression?.Effects ?? PowerShellSemanticEffect.None),
            (runtimeFree ? PowerShellRequiredCapability.None :
                PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost) |
            (expression?.Capabilities ?? PowerShellRequiredCapability.None))
    {
        Expression = expression;
        RuntimeFree = runtimeFree;
    }

    internal PowerShellBoundExpression? Expression { get; }
    internal bool RuntimeFree { get; }
}
