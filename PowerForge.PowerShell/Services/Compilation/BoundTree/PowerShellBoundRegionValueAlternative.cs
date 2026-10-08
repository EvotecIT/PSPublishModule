namespace PowerForge;

/// <summary>Constructs one compiler-owned closed scalar-or-vector alternative.</summary>
internal sealed class PowerShellBoundRegionValueAlternativeExpression : PowerShellBoundExpression
{
    internal PowerShellBoundRegionValueAlternativeExpression(
        SourceSpan span,
        int alternativeIndex,
        PowerShellBoundExpression value,
        PowerShellTypeFact envelopeType)
        : base(span, envelopeType, PowerShellValueState.Known, value.Effects, value.Capabilities)
    {
        if (alternativeIndex < 0 || alternativeIndex >= envelopeType.ClosedAlternativeTypes.Count)
            throw new ArgumentOutOfRangeException(nameof(alternativeIndex));
        if (envelopeType.ClrType != typeof(PowerForge.Generated.Runtime.PowerShellRegionValueAlternative) ||
            envelopeType.ClosedAlternativeTypes[alternativeIndex] != value.Type.ClrType)
            throw new ArgumentException("The region value does not match its closed alternative contract.", nameof(value));
        AlternativeIndex = alternativeIndex;
        Value = value;
    }

    internal int AlternativeIndex { get; }
    internal PowerShellBoundExpression Value { get; }
}
