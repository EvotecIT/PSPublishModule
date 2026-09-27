namespace PowerForge;

/// <summary>Acquires an invocation-owned reference cell for an unoptimized local variable.</summary>
internal sealed class PowerShellBoundNativeReferenceExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeReferenceExpression(SourceSpan span, string name)
        : base(span, PowerShellTypeFact.Unknown, PowerShellValueState.Unknown,
            PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError,
            PowerShellRequiredCapability.PowerShellHost | PowerShellRequiredCapability.NativeFunctionBinding |
            PowerShellRequiredCapability.PowerShellStatementErrors)
        => Name = name;

    internal string Name { get; }
}
