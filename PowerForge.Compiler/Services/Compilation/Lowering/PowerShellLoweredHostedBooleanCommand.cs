namespace PowerForge;

internal sealed class PowerShellLoweredHostedCommandArgument
{
    internal PowerShellLoweredHostedCommandArgument(string parameterName, PowerShellLoweredExpression? value)
    {
        ParameterName = parameterName;
        Value = value;
    }

    internal string ParameterName { get; }
    internal PowerShellLoweredExpression? Value { get; }
}

internal sealed class PowerShellLoweredHostedBooleanCommandExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredHostedBooleanCommandExpression(
        SourceSpan span,
        PowerShellCompilationCommandProviderContract provider,
        IReadOnlyList<PowerShellLoweredHostedCommandArgument> arguments)
        : base(span, typeof(bool))
    {
        Provider = provider;
        Arguments = arguments;
    }

    internal PowerShellCompilationCommandProviderContract Provider { get; }
    internal IReadOnlyList<PowerShellLoweredHostedCommandArgument> Arguments { get; }
}
