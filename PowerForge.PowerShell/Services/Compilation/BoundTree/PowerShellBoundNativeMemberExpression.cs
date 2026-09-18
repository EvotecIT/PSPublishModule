namespace PowerForge;

/// <summary>Reads a member through the invocation's PowerShell adapter and type table.</summary>
internal sealed class PowerShellBoundNativeMemberExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeMemberExpression(SourceSpan span, PowerShellBoundExpression receiver, string name)
        : this(span, receiver, null, null, name, false) { }

    internal PowerShellBoundNativeMemberExpression(SourceSpan span, PowerShellBoundExpression? receiver,
        Type? literalTargetType, PowerShellBoundExpression nameExpression, bool isStatic)
        : this(span, receiver, literalTargetType, nameExpression, null, isStatic) { }

    internal PowerShellBoundNativeMemberExpression(SourceSpan span, PowerShellBoundExpression? receiver,
        Type? literalTargetType, PowerShellBoundExpression? nameExpression, string? name, bool isStatic)
        : base(span, PowerShellTypeFact.Unknown, PowerShellValueState.Unknown,
            (receiver?.Effects ?? PowerShellSemanticEffect.None) |
            (nameExpression?.Effects ?? PowerShellSemanticEffect.None) |
            PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError,
            (receiver?.Capabilities ?? PowerShellRequiredCapability.None) |
            (nameExpression?.Capabilities ?? PowerShellRequiredCapability.None) |
            PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
            PowerShellRequiredCapability.PowerShellStatementErrors)
    {
        Receiver = receiver;
        LiteralTargetType = literalTargetType;
        NameExpression = nameExpression;
        Name = name;
        IsStatic = isStatic;
    }

    internal PowerShellBoundExpression? Receiver { get; }
    internal Type? LiteralTargetType { get; }
    internal string? Name { get; }
    internal PowerShellBoundExpression? NameExpression { get; }
    internal bool IsStatic { get; }
}
