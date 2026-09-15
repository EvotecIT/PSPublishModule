namespace PowerForge;

/// <summary>Preserves a region's ordered local-transfer return through CLR emission and graph construction.</summary>
internal sealed class PowerShellLoweredRegionTransferStatement : PowerShellLoweredReturnStatement
{
    internal PowerShellLoweredRegionTransferStatement(SourceSpan span, PowerShellLoweredExpression expression, PowerShellSymbolId[] locals)
        : base(span, expression, emitsValue: true, emitsSuccessOutput: false)
        => Locals = locals;

    internal PowerShellImmutableArray<PowerShellSymbolId> Locals { get; }
}
