namespace PowerForge;

/// <summary>Owns the result slot and temporary success sink for a lowered capture block.</summary>
internal sealed class PowerShellLoweredOutputCaptureStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredOutputCaptureStatement(SourceSpan span, PowerShellSymbolId target,
        PowerShellLoweredStatement[] statements, string recordsTemporary, string sinkTemporary, bool usesNativeInvocation = false)
        : base(span)
    {
        Target = target;
        Statements = statements;
        RecordsTemporary = recordsTemporary;
        SinkTemporary = sinkTemporary;
        UsesNativeInvocation = usesNativeInvocation;
    }

    internal PowerShellSymbolId Target { get; }
    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal string RecordsTemporary { get; }
    internal string SinkTemporary { get; }
    internal bool UsesNativeInvocation { get; }
}
