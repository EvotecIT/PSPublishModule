namespace PowerForge;

internal sealed class PowerShellLoweredClrMemberExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredClrMemberExpression(
        SourceSpan span,
        Type clrType,
        Type declaringType,
        string memberName,
        bool isStatic,
        PowerShellLoweredExpression? receiver,
        PowerShellClrReceiverBehavior receiverBehavior)
        : base(span, clrType)
    {
        DeclaringType = declaringType;
        MemberName = memberName;
        IsStatic = isStatic;
        Receiver = receiver;
        ReceiverBehavior = receiverBehavior;
    }

    internal Type DeclaringType { get; }
    internal string MemberName { get; }
    internal bool IsStatic { get; }
    internal PowerShellLoweredExpression? Receiver { get; }
    internal PowerShellClrReceiverBehavior ReceiverBehavior { get; }
}

internal sealed class PowerShellLoweredClrInvocationExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredClrInvocationExpression(
        SourceSpan span,
        Type clrType,
        Type declaringType,
        string memberName,
        PowerShellClrInvocationKind invocationKind,
        PowerShellLoweredExpression? receiver,
        PowerShellClrReceiverBehavior receiverBehavior,
        PowerShellLoweredExpression[] arguments,
        Type[] parameterTypes)
        : base(span, clrType)
    {
        DeclaringType = declaringType;
        MemberName = memberName;
        InvocationKind = invocationKind;
        Receiver = receiver;
        ReceiverBehavior = receiverBehavior;
        Arguments = arguments;
        ParameterTypes = parameterTypes;
    }

    internal Type DeclaringType { get; }
    internal string MemberName { get; }
    internal PowerShellClrInvocationKind InvocationKind { get; }
    internal PowerShellLoweredExpression? Receiver { get; }
    internal PowerShellClrReceiverBehavior ReceiverBehavior { get; }
    internal PowerShellLoweredExpression[] Arguments { get; }
    internal Type[] ParameterTypes { get; }
}
