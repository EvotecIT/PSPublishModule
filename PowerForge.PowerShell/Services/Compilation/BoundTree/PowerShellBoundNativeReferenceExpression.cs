namespace PowerForge;

/// <summary>Acquires an invocation-owned reference cell with its native local storage metadata.</summary>
internal sealed class PowerShellBoundNativeReferenceExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeReferenceExpression(SourceSpan span, string name, bool directLocal = false)
        : base(span, PowerShellTypeFact.Unknown, PowerShellValueState.Unknown,
            PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError,
            PowerShellRequiredCapability.PowerShellHost | PowerShellRequiredCapability.NativeFunctionBinding |
            PowerShellRequiredCapability.PowerShellStatementErrors)
    {
        Name = name;
        DirectLocal = directLocal;
    }

    internal string Name { get; }
    internal bool DirectLocal { get; }
}
