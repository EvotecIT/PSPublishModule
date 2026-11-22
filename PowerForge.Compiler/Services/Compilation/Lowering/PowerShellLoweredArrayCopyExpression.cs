namespace PowerForge;

/// <summary>Execution contract for one-level CLR vector collection.</summary>
internal sealed class PowerShellLoweredArrayCopyExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredArrayCopyExpression(SourceSpan span, PowerShellLoweredExpression source,
        bool shareEmptyResult, string sourceTemporary, string resultTemporary, string indexTemporary)
        : base(span, typeof(object[]))
    {
        Source = source;
        ShareEmptyResult = shareEmptyResult;
        SourceTemporary = sourceTemporary;
        ResultTemporary = resultTemporary;
        IndexTemporary = indexTemporary;
    }

    internal PowerShellLoweredExpression Source { get; }
    internal bool ShareEmptyResult { get; }
    internal string SourceTemporary { get; }
    internal string ResultTemporary { get; }
    internal string IndexTemporary { get; }
}
