namespace PowerForge;

/// <summary>Selects one compiler-bound clause for the current native lifecycle callback.</summary>
internal sealed class PowerShellBoundNativeLifecycleExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeLifecycleExpression(SourceSpan span, int clause)
        : base(span, new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred,
                "The native host selects one compiled lifecycle clause."), PowerShellValueState.Known,
            PowerShellSemanticEffect.Host, PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost)
        => Clause = clause;

    internal int Clause { get; }
}
