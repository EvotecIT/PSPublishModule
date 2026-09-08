namespace PowerForge;

internal sealed class PowerShellLoweredNativeInvocationExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeInvocationExpression(SourceSpan span, PowerShellLoweredExpression? receiver,
        Type? literalTargetType, string name, bool isStatic, PowerShellLoweredExpression[] arguments,
        Type? targetConstraint, Type?[] argumentConstraints) : base(span, typeof(object))
    {
        Receiver = receiver;
        LiteralTargetType = literalTargetType;
        Name = name;
        IsStatic = isStatic;
        Arguments = arguments;
        TargetConstraint = targetConstraint;
        ArgumentConstraints = argumentConstraints;
    }

    internal PowerShellLoweredExpression? Receiver { get; }
    internal Type? LiteralTargetType { get; }
    internal string Name { get; }
    internal bool IsStatic { get; }
    internal PowerShellImmutableArray<PowerShellLoweredExpression> Arguments { get; }
    internal Type? TargetConstraint { get; }
    internal PowerShellImmutableArray<Type?> ArgumentConstraints { get; }
}
