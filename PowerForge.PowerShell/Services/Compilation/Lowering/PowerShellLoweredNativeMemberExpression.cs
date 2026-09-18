namespace PowerForge;

internal sealed class PowerShellLoweredNativeMemberExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeMemberExpression(SourceSpan span, PowerShellLoweredExpression receiver, string name)
        : this(span, receiver, null, null, name, false) { }

    internal PowerShellLoweredNativeMemberExpression(SourceSpan span, PowerShellLoweredExpression? receiver,
        Type? literalTargetType, PowerShellLoweredExpression nameExpression, bool isStatic)
        : this(span, receiver, literalTargetType, nameExpression, null, isStatic) { }

    internal PowerShellLoweredNativeMemberExpression(SourceSpan span, PowerShellLoweredExpression? receiver,
        Type? literalTargetType, PowerShellLoweredExpression? nameExpression, string? name, bool isStatic)
        : base(span, typeof(object))
    {
        Receiver = receiver;
        LiteralTargetType = literalTargetType;
        NameExpression = nameExpression;
        Name = name;
        IsStatic = isStatic;
    }

    internal PowerShellLoweredExpression? Receiver { get; }
    internal Type? LiteralTargetType { get; }
    internal string? Name { get; }
    internal PowerShellLoweredExpression? NameExpression { get; }
    internal bool IsStatic { get; }
}
