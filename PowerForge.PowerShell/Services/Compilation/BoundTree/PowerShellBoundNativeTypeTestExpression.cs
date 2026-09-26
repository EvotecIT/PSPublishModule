namespace PowerForge;

/// <summary>Preserves invocation-owned type operands and lazy resolution of an authored type-test name.</summary>
internal sealed class PowerShellBoundNativeTypeTestExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeTypeTestExpression(SourceSpan span, PowerShellBoundExpression operand,
        PowerShellBoundExpression? target, string? authoredTypeName, SourceSpan targetSpan, bool negate)
        : base(span, new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "A native type test returns Boolean."),
            PowerShellValueState.Unknown, operand.Effects | (target?.Effects ?? PowerShellSemanticEffect.None) |
            PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError,
            operand.Capabilities | (target?.Capabilities ?? PowerShellRequiredCapability.None) |
            PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
            PowerShellRequiredCapability.PowerShellStatementErrors)
    {
        if ((target is null) == (authoredTypeName is null)) throw new ArgumentException("A native type test requires exactly one target representation.");
        Operand = operand; Target = target; AuthoredTypeName = authoredTypeName; TargetSpan = targetSpan; Negate = negate;
    }
    internal PowerShellBoundExpression Operand { get; }
    internal PowerShellBoundExpression? Target { get; }
    internal string? AuthoredTypeName { get; }
    internal SourceSpan TargetSpan { get; }
    internal bool Negate { get; }
}
