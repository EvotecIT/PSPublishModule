namespace PowerForge;

/// <summary>Lowered construction of one compiler-owned closed value alternative.</summary>
internal sealed class PowerShellLoweredRegionValueAlternativeExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredRegionValueAlternativeExpression(
        SourceSpan span,
        int alternativeIndex,
        PowerShellLoweredExpression value)
        : base(span, typeof(PowerForge.Generated.Runtime.PowerShellRegionValueAlternative))
    {
        AlternativeIndex = alternativeIndex;
        Value = value;
    }

    internal int AlternativeIndex { get; }
    internal PowerShellLoweredExpression Value { get; }
}
