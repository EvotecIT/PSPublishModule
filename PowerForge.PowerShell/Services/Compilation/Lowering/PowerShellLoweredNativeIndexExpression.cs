namespace PowerForge;

internal sealed class PowerShellLoweredNativeIndexExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeIndexExpression(SourceSpan span, PowerShellLoweredExpression receiver,
        PowerShellLoweredExpression[] arguments, Type? targetConstraint, Type? indexConstraint) : base(span, typeof(object))
    {
        Receiver = receiver;
        Arguments = arguments;
        TargetConstraint = targetConstraint;
        IndexConstraint = indexConstraint;
    }

    internal PowerShellLoweredExpression Receiver { get; }
    internal PowerShellImmutableArray<PowerShellLoweredExpression> Arguments { get; }
    internal Type? TargetConstraint { get; }
    internal Type? IndexConstraint { get; }
}
