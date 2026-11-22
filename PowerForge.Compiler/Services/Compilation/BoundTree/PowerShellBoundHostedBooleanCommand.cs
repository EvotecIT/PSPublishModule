namespace PowerForge;

internal sealed class PowerShellBoundHostedCommandArgument
{
    internal PowerShellBoundHostedCommandArgument(string parameterName, PowerShellBoundExpression? value)
    {
        ParameterName = parameterName;
        Value = value;
    }

    internal string ParameterName { get; }
    internal PowerShellBoundExpression? Value { get; }
}

internal sealed class PowerShellBoundHostedBooleanCommandExpression : PowerShellBoundExpression
{
    internal PowerShellBoundHostedBooleanCommandExpression(
        SourceSpan span,
        PowerShellCompilationCommandProviderContract provider,
        IReadOnlyList<PowerShellBoundHostedCommandArgument> arguments)
        : base(
            span,
            new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "A bounded hosted Boolean command produces one Boolean result."),
            PowerShellValueState.Known,
            PowerShellSemanticEffect.Host |
            arguments.Aggregate(PowerShellSemanticEffect.None, static (effects, argument) => effects | (argument.Value?.Effects ?? PowerShellSemanticEffect.None)),
            PowerShellRequiredCapability.CommandRegion |
            PowerShellRequiredCapability.PowerShellHostTypes |
            arguments.Aggregate(PowerShellRequiredCapability.None, static (capabilities, argument) => capabilities | (argument.Value?.Capabilities ?? PowerShellRequiredCapability.None)))
    {
        Provider = provider;
        Arguments = arguments;
    }

    internal PowerShellCompilationCommandProviderContract Provider { get; }
    internal IReadOnlyList<PowerShellBoundHostedCommandArgument> Arguments { get; }
}
