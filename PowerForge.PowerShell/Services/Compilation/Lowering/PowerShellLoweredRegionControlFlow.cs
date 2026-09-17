namespace PowerForge;

/// <summary>Lowered construction of a compiler-owned return-or-fallthrough envelope.</summary>
internal sealed class PowerShellLoweredRegionControlFlowExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredRegionControlFlowExpression(
        SourceSpan span,
        PowerShellRegionControlFlowKind kind,
        PowerShellLoweredExpression? value)
        : base(span, typeof(PowerForge.Generated.Runtime.PowerShellRegionControlFlowEnvelope))
    {
        Kind = kind;
        Value = value;
    }

    internal PowerShellRegionControlFlowKind Kind { get; }
    internal PowerShellLoweredExpression? Value { get; }
}

/// <summary>Preserves the helper-only envelope return through graph construction and emission.</summary>
internal sealed class PowerShellLoweredRegionControlFlowReturnStatement : PowerShellLoweredReturnStatement
{
    internal PowerShellLoweredRegionControlFlowReturnStatement(
        SourceSpan span,
        PowerShellLoweredRegionControlFlowExpression expression)
        : base(span, expression, emitsValue: true, emitsSuccessOutput: false)
    {
    }
}
