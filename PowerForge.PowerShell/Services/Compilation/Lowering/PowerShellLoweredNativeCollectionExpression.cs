namespace PowerForge;

internal sealed class PowerShellLoweredNativeCollectionExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeCollectionExpression(SourceSpan span, string sourcePath,
        PowerShellLoweredNativeCollectionItem[] items, bool shareEmptyResult, string resultTemporary, bool singleExpression = false, bool collapseResult = false)
        : base(span, collapseResult ? typeof(object) : typeof(object[]))
    {
        SourcePath = sourcePath;
        Items = items;
        ShareEmptyResult = shareEmptyResult;
        ResultTemporary = resultTemporary;
        SingleExpression = singleExpression;
        CollapseResult = collapseResult;
    }

    internal string SourcePath { get; }
    internal PowerShellImmutableArray<PowerShellLoweredNativeCollectionItem> Items { get; }
    internal bool ShareEmptyResult { get; }
    internal string ResultTemporary { get; }
    internal bool SingleExpression { get; }
    internal bool CollapseResult { get; }
}

internal sealed class PowerShellLoweredNativeCollectionItem
{
    internal PowerShellLoweredNativeCollectionItem(SourceSpan span, string sourceText, PowerShellLoweredExpression value,
        bool setSuccess, string valueTemporary, string exceptionTemporary, bool emitsOutput = true, bool isPipelineStatement = false)
    {
        Span = span;
        SourceText = sourceText;
        Value = value;
        SetSuccess = setSuccess;
        EmitsOutput = emitsOutput;
        IsPipelineStatement = isPipelineStatement;
        ValueTemporary = valueTemporary;
        ExceptionTemporary = exceptionTemporary;
    }

    internal SourceSpan Span { get; }
    internal string SourceText { get; }
    internal PowerShellLoweredExpression Value { get; }
    internal bool SetSuccess { get; }
    internal bool EmitsOutput { get; }
    /// <summary>Preserves direct pipeline records before expression scalarization.</summary>
    internal bool IsPipelineStatement { get; }
    internal string ValueTemporary { get; }
    internal string ExceptionTemporary { get; }
}
