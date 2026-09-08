namespace PowerForge;

/// <summary>Reads an instance member through the invocation's PowerShell adapter and type table.</summary>
internal sealed class PowerShellBoundNativeMemberExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeMemberExpression(SourceSpan span, PowerShellBoundExpression receiver, string name)
        : base(span, PowerShellTypeFact.Unknown, PowerShellValueState.Unknown,
            receiver.Effects | PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError,
            receiver.Capabilities | PowerShellRequiredCapability.NativeFunctionBinding |
            PowerShellRequiredCapability.PowerShellHost | PowerShellRequiredCapability.PowerShellStatementErrors)
    {
        Receiver = receiver;
        Name = name;
    }

    internal PowerShellBoundExpression Receiver { get; }
    internal string Name { get; }
}
