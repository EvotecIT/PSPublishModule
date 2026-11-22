namespace PowerForge;

internal enum PowerShellRegionControlFlowKind
{
    FallThrough,
    Return
}

/// <summary>Creates a control-flow envelope without observing its return value.</summary>
internal sealed class PowerShellBoundRegionControlFlowExpression : PowerShellBoundExpression
{
    internal PowerShellBoundRegionControlFlowExpression(
        SourceSpan span,
        PowerShellRegionControlFlowKind kind,
        PowerShellBoundExpression? value)
        : base(
            span,
            new PowerShellTypeFact(
                typeof(PowerForge.Generated.Runtime.PowerShellRegionControlFlowEnvelope),
                PowerShellTypeFactProvenance.Inferred,
                "A promoted region returns a compiler-owned return-or-fallthrough envelope."),
            PowerShellValueState.Known,
            value?.Effects ?? PowerShellSemanticEffect.None,
            value?.Capabilities ?? PowerShellRequiredCapability.None)
    {
        if (kind == PowerShellRegionControlFlowKind.FallThrough && value is not null)
            throw new ArgumentException("A fallthrough envelope cannot carry a value.", nameof(value));
        Kind = kind;
        Value = value;
    }

    internal PowerShellRegionControlFlowKind Kind { get; }
    internal PowerShellBoundExpression? Value { get; }
}

/// <summary>Returns a control-flow envelope through the helper ABI without writing success output.</summary>
internal sealed class PowerShellBoundRegionControlFlowReturnStatement : PowerShellBoundReturnStatement
{
    internal PowerShellBoundRegionControlFlowReturnStatement(
        SourceSpan span,
        PowerShellRegionControlFlowKind kind,
        PowerShellBoundExpression? value = null)
        : base(span, new PowerShellBoundRegionControlFlowExpression(span, kind, value), emitsValue: true, emitsSuccessOutput: false)
    {
    }
}
