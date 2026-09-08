namespace PowerForge;

/// <summary>Captures expression output while preserving each native statement's completion and error boundary.</summary>
internal sealed class PowerShellBoundNativeCollectionExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeCollectionExpression(SourceSpan span, string sourcePath,
        PowerShellBoundNativeCollectionItem[] items, bool shareEmptyResult, bool singleExpression = false)
        : base(span, new PowerShellTypeFact(typeof(object[]), PowerShellTypeFactProvenance.Inferred,
                "Native expression statements contribute records to one collected Object array."), PowerShellValueState.Known,
            items.Aggregate(PowerShellSemanticEffect.Mutation | PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError,
                static (effects, item) => effects | item.Value.Effects),
            items.Aggregate(PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellStatementErrors | PowerShellRequiredCapability.PowerShellHost,
                static (capabilities, item) => capabilities | item.Value.Capabilities))
    {
        SourcePath = sourcePath;
        Items = items;
        ShareEmptyResult = shareEmptyResult;
        SingleExpression = singleExpression;
    }

    internal string SourcePath { get; }
    internal PowerShellImmutableArray<PowerShellBoundNativeCollectionItem> Items { get; }
    internal bool ShareEmptyResult { get; }
    /// <summary>Uses the native array operator without an inner statement error boundary.</summary>
    internal bool SingleExpression { get; }
}

internal sealed class PowerShellBoundNativeCollectionItem
{
    internal PowerShellBoundNativeCollectionItem(SourceSpan span, string sourceText, PowerShellBoundExpression value,
        bool setSuccess)
    {
        Span = span;
        SourceText = sourceText;
        Value = value;
        SetSuccess = setSuccess;
    }

    internal SourceSpan Span { get; }
    internal string SourceText { get; }
    internal PowerShellBoundExpression Value { get; }
    internal bool SetSuccess { get; }
}
