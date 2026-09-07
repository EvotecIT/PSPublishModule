namespace PowerForge;

/// <summary>Collects one CLR vector as Object records, preserving null and one enumeration level.</summary>
internal sealed class PowerShellBoundArrayCopyExpression : PowerShellBoundExpression
{
    internal PowerShellBoundArrayCopyExpression(SourceSpan span, PowerShellBoundExpression source, bool shareEmptyResult)
        : base(span,
            new PowerShellTypeFact(typeof(object[]), PowerShellTypeFactProvenance.Inferred,
                "Collecting a CLR vector produces an Object array with one record per element, or one null record."),
            PowerShellValueState.Known, source.Effects, source.Capabilities)
    {
        if (!source.Type.ClrType.IsArray || source.Type.ClrType.GetArrayRank() != 1 ||
            source.Type.ClrType != source.Type.ClrType.GetElementType()!.MakeArrayType())
            throw new ArgumentException("Array collection copying requires a zero-based CLR vector.", nameof(source));
        Source = source;
        ShareEmptyResult = shareEmptyResult;
    }

    internal PowerShellBoundExpression Source { get; }
    internal bool ShareEmptyResult { get; }
}
