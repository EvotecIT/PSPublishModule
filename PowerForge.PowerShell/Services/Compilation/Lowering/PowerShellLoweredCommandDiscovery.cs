namespace PowerForge;

internal sealed class PowerShellLoweredCommandAvailabilityExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredCommandAvailabilityExpression(
        SourceSpan span,
        PowerShellLoweredExpression name,
        PowerShellCommandDiscoveryErrorAction errorAction,
        PowerShellCompilationCommandProviderContract provider)
        : base(span, typeof(bool))
    {
        Name = name;
        ErrorAction = errorAction;
        Provider = provider;
    }

    internal PowerShellLoweredExpression Name { get; }
    internal PowerShellCommandDiscoveryErrorAction ErrorAction { get; }
    internal PowerShellCompilationCommandProviderContract Provider { get; }
}
