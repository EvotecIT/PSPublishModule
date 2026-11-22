namespace PowerForge;

internal sealed class PowerShellLoweredParameterPresenceExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredParameterPresenceExpression(SourceSpan span, string parameterName)
        : base(span, typeof(bool))
    {
        ParameterName = parameterName;
    }

    internal string ParameterName { get; }
}
