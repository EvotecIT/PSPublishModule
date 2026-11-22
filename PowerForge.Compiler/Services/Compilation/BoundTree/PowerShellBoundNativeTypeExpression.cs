namespace PowerForge;

/// <summary>A qualified authored type name resolved by the active native lexical scope.</summary>
internal sealed class PowerShellBoundNativeTypeExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeTypeExpression(SourceSpan span, string name)
        : base(span, new PowerShellTypeFact(typeof(Type), PowerShellTypeFactProvenance.Explicit,
            "The native lexical owner resolves a qualified document-owned class."), PowerShellValueState.Unknown,
            PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError,
            PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
            PowerShellRequiredCapability.PowerShellStatementErrors) => Name = name;
    internal string Name { get; }
}
