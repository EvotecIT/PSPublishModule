namespace PowerForge;

internal enum PowerShellClrReceiverBehavior
{
    None,
    NormalizeNullString,
    NormalizeNullArrayLength,
    PropagateNull
}

internal enum PowerShellClrInvocationKind
{
    Constructor,
    StaticMethod,
    InstanceMethod
}

/// <summary>Resolved CLR field or property read. Reflection is complete before this node is created.</summary>
internal sealed class PowerShellBoundClrMemberExpression : PowerShellBoundExpression
{
    internal PowerShellBoundClrMemberExpression(
        SourceSpan span,
        Type declaringType,
        string memberName,
        bool isStatic,
        PowerShellBoundExpression? receiver,
        PowerShellClrReceiverBehavior receiverBehavior,
        PowerShellTypeFact type)
        : base(span, type, PowerShellValueState.Unknown)
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
    internal PowerShellBoundExpression? Receiver { get; }
    internal PowerShellClrReceiverBehavior ReceiverBehavior { get; }
}

/// <summary>Exact CLR constructor or method call selected by the semantic binder.</summary>
internal sealed class PowerShellBoundClrInvocationExpression : PowerShellBoundExpression
{
    internal PowerShellBoundClrInvocationExpression(
        SourceSpan span,
        Type declaringType,
        string memberName,
        PowerShellClrInvocationKind invocationKind,
        PowerShellBoundExpression? receiver,
        PowerShellClrReceiverBehavior receiverBehavior,
        PowerShellBoundExpression[] arguments,
        Type[] parameterTypes,
        PowerShellTypeFact type)
        : base(span, type, PowerShellValueState.Unknown)
    {
        DeclaringType = declaringType;
        MemberName = memberName;
        InvocationKind = invocationKind;
        Receiver = receiver;
        ReceiverBehavior = receiverBehavior;
        Arguments = arguments ?? Array.Empty<PowerShellBoundExpression>();
        ParameterTypes = parameterTypes ?? Array.Empty<Type>();
    }

    internal Type DeclaringType { get; }
    internal string MemberName { get; }
    internal PowerShellClrInvocationKind InvocationKind { get; }
    internal PowerShellBoundExpression? Receiver { get; }
    internal PowerShellClrReceiverBehavior ReceiverBehavior { get; }
    internal PowerShellBoundExpression[] Arguments { get; }
    internal Type[] ParameterTypes { get; }
}
