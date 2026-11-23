namespace PowerForge;

internal sealed class PowerShellLoweredExitStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredExitStatement(SourceSpan span, PowerShellLoweredExpression? expression) : base(span)
        => Expression = expression;

    internal PowerShellLoweredExpression? Expression { get; }
}
