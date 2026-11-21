namespace PowerForge;

/// <summary>The caller's standalone success stream, passed to a compiler-owned lifecycle runner.</summary>
internal sealed class PowerShellBoundStrictSuccessSinkExpression : PowerShellBoundExpression
{
    internal PowerShellBoundStrictSuccessSinkExpression(SourceSpan span, PowerShellSymbolId upstream,
        Type intermediateType, PowerShellSymbolId downstream, Type recordType, bool acceptsNullIntermediate, bool filtersNull) : base(span,
        new PowerShellTypeFact(typeof(Action<object>), PowerShellTypeFactProvenance.CommandContract,
            "A standalone pipeline sends its records to the caller's success stream."),
        PowerShellValueState.Known, PowerShellSemanticEffect.None, PowerShellRequiredCapability.RuntimeFreeSuccessOutput)
    {
        Upstream = upstream;
        IntermediateType = intermediateType;
        Downstream = downstream;
        RecordType = recordType;
        AcceptsNullIntermediate = acceptsNullIntermediate;
        FiltersNull = filtersNull;
    }

    internal PowerShellSymbolId Upstream { get; }
    internal Type IntermediateType { get; }
    internal PowerShellSymbolId Downstream { get; }
    internal Type RecordType { get; }
    internal bool AcceptsNullIntermediate { get; }
    internal bool FiltersNull { get; }
}
