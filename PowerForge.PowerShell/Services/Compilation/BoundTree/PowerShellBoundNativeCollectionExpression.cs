namespace PowerForge;

/// <summary>Captures expression output while preserving each native statement's completion and error boundary.</summary>
internal sealed class PowerShellBoundNativeCollectionExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeCollectionExpression(SourceSpan span, string sourcePath,
        PowerShellBoundNativeCollectionItem[] items, bool shareEmptyResult)
        : base(span, new PowerShellTypeFact(typeof(object[]), PowerShellTypeFactProvenance.Inferred,
                "Native expression statements contribute records to one collected Object array."), PowerShellValueState.Known,
            items.Aggregate(PowerShellSemanticEffect.Mutation, static (effects, item) => effects | item.Value.Effects),
            items.Aggregate(PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellStatementErrors,
                static (capabilities, item) => capabilities | item.Value.Capabilities))
    {
        SourcePath = sourcePath;
        Items = items;
        ShareEmptyResult = shareEmptyResult;
    }

    internal string SourcePath { get; }
    internal PowerShellImmutableArray<PowerShellBoundNativeCollectionItem> Items { get; }
    internal bool ShareEmptyResult { get; }
}

internal sealed class PowerShellBoundNativeCollectionItem
{
    internal PowerShellBoundNativeCollectionItem(SourceSpan span, string sourceText, PowerShellBoundExpression value,
        bool enumerate, bool setSuccess)
    {
        Span = span;
        SourceText = sourceText;
        Value = value;
        Enumerate = enumerate;
        SetSuccess = setSuccess;
    }

    internal SourceSpan Span { get; }
    internal string SourceText { get; }
    internal PowerShellBoundExpression Value { get; }
    internal bool Enumerate { get; }
    internal bool SetSuccess { get; }
}
