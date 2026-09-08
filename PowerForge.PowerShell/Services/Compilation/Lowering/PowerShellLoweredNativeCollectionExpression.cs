namespace PowerForge;

internal sealed class PowerShellLoweredNativeCollectionExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeCollectionExpression(SourceSpan span, string sourcePath,
        PowerShellLoweredNativeCollectionItem[] items, bool shareEmptyResult, string resultTemporary, bool singleExpression = false)
        : base(span, typeof(object[]))
    {
        SourcePath = sourcePath;
        Items = items;
        ShareEmptyResult = shareEmptyResult;
        ResultTemporary = resultTemporary;
        SingleExpression = singleExpression;
    }

    internal string SourcePath { get; }
    internal PowerShellImmutableArray<PowerShellLoweredNativeCollectionItem> Items { get; }
    internal bool ShareEmptyResult { get; }
    internal string ResultTemporary { get; }
    internal bool SingleExpression { get; }
}

internal sealed class PowerShellLoweredNativeCollectionItem
{
    internal PowerShellLoweredNativeCollectionItem(SourceSpan span, string sourceText, PowerShellLoweredExpression value,
        bool setSuccess, string valueTemporary, string exceptionTemporary)
    {
        Span = span;
        SourceText = sourceText;
        Value = value;
        SetSuccess = setSuccess;
        ValueTemporary = valueTemporary;
        ExceptionTemporary = exceptionTemporary;
    }

    internal SourceSpan Span { get; }
    internal string SourceText { get; }
    internal PowerShellLoweredExpression Value { get; }
    internal bool SetSuccess { get; }
    internal string ValueTemporary { get; }
    internal string ExceptionTemporary { get; }
}
