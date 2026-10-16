namespace PowerForge;

/// <summary>Owns compiled value statements and their invocation-local output collector.</summary>
internal sealed class PowerShellLoweredNativeStatementValueExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeStatementValueExpression(SourceSpan span, PowerShellLoweredStatement[] statements,
        string recordsTemporary, string sinkTemporary, bool preserveRecords = false, bool shareEmptyResult = false)
        : base(span, preserveRecords ? typeof(object[]) : typeof(object))
    {
        Statements = statements;
        RecordsTemporary = recordsTemporary;
        SinkTemporary = sinkTemporary;
        PreserveRecords = preserveRecords;
        ShareEmptyResult = shareEmptyResult;
    }

    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal string RecordsTemporary { get; }
    internal string SinkTemporary { get; }
    internal bool PreserveRecords { get; }
    internal bool ShareEmptyResult { get; }
}
