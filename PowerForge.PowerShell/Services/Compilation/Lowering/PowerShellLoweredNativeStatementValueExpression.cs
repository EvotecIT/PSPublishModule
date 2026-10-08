namespace PowerForge;

/// <summary>Owns compiled value statements and their invocation-local output collector.</summary>
internal sealed class PowerShellLoweredNativeStatementValueExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeStatementValueExpression(SourceSpan span, PowerShellLoweredStatement[] statements,
        string recordsTemporary, string sinkTemporary) : base(span, typeof(object))
    {
        Statements = statements;
        RecordsTemporary = recordsTemporary;
        SinkTemporary = sinkTemporary;
    }

    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal string RecordsTemporary { get; }
    internal string SinkTemporary { get; }
}
