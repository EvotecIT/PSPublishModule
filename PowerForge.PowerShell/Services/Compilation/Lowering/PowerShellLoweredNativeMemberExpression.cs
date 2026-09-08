namespace PowerForge;

internal sealed class PowerShellLoweredNativeMemberExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeMemberExpression(SourceSpan span, PowerShellLoweredExpression receiver, string name)
        : base(span, typeof(object))
    {
        Receiver = receiver;
        Name = name;
    }

    internal PowerShellLoweredExpression Receiver { get; }
    internal string Name { get; }
}
