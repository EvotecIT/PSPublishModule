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
        PowerShellClrReceiverBehavior receiverBehavior,
        string dictionaryTemporary,
        string valueTemporary)
        : base(span, clrType)
    {
        DeclaringType = declaringType;
        MemberName = memberName;
        IsStatic = isStatic;
        Receiver = receiver;
        ReceiverBehavior = receiverBehavior;
        DictionaryTemporary = dictionaryTemporary ?? string.Empty;
        ValueTemporary = valueTemporary ?? string.Empty;
    }

    internal Type DeclaringType { get; }
    internal string MemberName { get; }
    internal bool IsStatic { get; }
    internal PowerShellLoweredExpression? Receiver { get; }
    internal PowerShellClrReceiverBehavior ReceiverBehavior { get; }
    internal string DictionaryTemporary { get; }
    internal string ValueTemporary { get; }
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
        Type[] parameterTypes,
        bool preserveStatementErrors = false,
        string receiverTemporary = "",
        string[]? argumentTemporaries = null,
        string exceptionTemporary = "",
        bool receiverByReference = false,
        PowerShellClrArgumentConversion[]? argumentConversions = null,
        string[]? rawArgumentTemporaries = null)
        : base(span, clrType)
    {
        DeclaringType = declaringType;
        MemberName = memberName;
        InvocationKind = invocationKind;
        Receiver = receiver;
        ReceiverBehavior = receiverBehavior;
        Arguments = arguments;
        ParameterTypes = parameterTypes;
        PreserveStatementErrors = preserveStatementErrors;
        ReceiverTemporary = receiverTemporary;
        ArgumentTemporaries = argumentTemporaries ?? Array.Empty<string>();
        ExceptionTemporary = exceptionTemporary;
        ReceiverByReference = receiverByReference;
        ArgumentConversions = argumentConversions ?? new PowerShellClrArgumentConversion[Arguments.Length];
        RawArgumentTemporaries = rawArgumentTemporaries ?? Array.Empty<string>();
    }

    internal Type DeclaringType { get; }
    internal string MemberName { get; }
    internal PowerShellClrInvocationKind InvocationKind { get; }
    internal PowerShellLoweredExpression? Receiver { get; }
    internal PowerShellClrReceiverBehavior ReceiverBehavior { get; }
    internal PowerShellImmutableArray<PowerShellLoweredExpression> Arguments { get; }
    internal PowerShellImmutableArray<Type> ParameterTypes { get; }
    internal bool PreserveStatementErrors { get; }
    internal string ReceiverTemporary { get; }
    internal PowerShellImmutableArray<string> ArgumentTemporaries { get; }
    internal string ExceptionTemporary { get; }
    internal bool ReceiverByReference { get; }
    internal PowerShellImmutableArray<PowerShellClrArgumentConversion> ArgumentConversions { get; }
    internal PowerShellImmutableArray<string> RawArgumentTemporaries { get; }
}

internal sealed class PowerShellLoweredClrMemberAssignmentStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredClrMemberAssignmentStatement(SourceSpan span, PowerShellLoweredExpression? receiver, Type declaringType, string memberName, PowerShellClrReceiverBehavior receiverBehavior, PowerShellLoweredExpression value)
        : base(span)
    {
        Receiver = receiver;
        DeclaringType = declaringType;
        MemberName = memberName;
        ReceiverBehavior = receiverBehavior;
        Value = value;
    }

    internal PowerShellLoweredExpression? Receiver { get; }
    internal Type DeclaringType { get; }
    internal string MemberName { get; }
    internal PowerShellClrReceiverBehavior ReceiverBehavior { get; }
    internal PowerShellLoweredExpression Value { get; }
}
