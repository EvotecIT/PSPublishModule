namespace PowerForge;

internal sealed class PowerShellLoweredNativeTypeTestExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeTypeTestExpression(SourceSpan span, PowerShellLoweredExpression operand,
        PowerShellLoweredExpression? target, string? authoredTypeName, SourceSpan targetSpan, bool negate) : base(span, typeof(bool))
    {
        Operand = operand; Target = target; AuthoredTypeName = authoredTypeName; TargetSpan = targetSpan; Negate = negate;
    }
    internal PowerShellLoweredExpression Operand { get; }
    internal PowerShellLoweredExpression? Target { get; }
    internal string? AuthoredTypeName { get; }
    internal SourceSpan TargetSpan { get; }
    internal bool Negate { get; }
}
