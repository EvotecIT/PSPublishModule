namespace PowerForge;

/// <summary>Captures expression output while preserving each native statement's completion and error boundary.</summary>
internal sealed class PowerShellBoundNativeCollectionExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeCollectionExpression(SourceSpan span, string sourcePath,
        PowerShellBoundNativeCollectionItem[] items, bool shareEmptyResult, bool singleExpression = false, bool collapseResult = false)
        : base(span, new PowerShellTypeFact(collapseResult ? typeof(object) : typeof(object[]), PowerShellTypeFactProvenance.Inferred,
                "Native expression statements contribute ordered records to one value collection."),
            collapseResult ? PowerShellValueState.Unknown : PowerShellValueState.Known,
            items.Aggregate(PowerShellSemanticEffect.Mutation | PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError,
                static (effects, item) => effects | item.Value.Effects),
            items.Aggregate(PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellStatementErrors | PowerShellRequiredCapability.PowerShellHost,
                static (capabilities, item) => capabilities | item.Value.Capabilities))
    {
        SourcePath = sourcePath;
        Items = items;
        ShareEmptyResult = shareEmptyResult;
        SingleExpression = singleExpression;
        CollapseResult = collapseResult;
    }

    internal string SourcePath { get; }
    internal PowerShellImmutableArray<PowerShellBoundNativeCollectionItem> Items { get; }
    internal bool ShareEmptyResult { get; }
    /// <summary>Uses the native array operator without an inner statement error boundary.</summary>
    internal bool SingleExpression { get; }
    /// <summary>Collapses zero/one/many records for an authored subexpression instead of returning an array.</summary>
    internal bool CollapseResult { get; }
}

internal sealed class PowerShellBoundNativeCollectionItem
{
    internal PowerShellBoundNativeCollectionItem(SourceSpan span, string sourceText, PowerShellBoundExpression value,
        bool setSuccess, bool emitsOutput = true, bool isPipelineStatement = false)
    {
        Span = span;
        SourceText = sourceText;
        Value = value;
        SetSuccess = setSuccess;
        EmitsOutput = emitsOutput;
        IsPipelineStatement = isPipelineStatement;
    }

    internal SourceSpan Span { get; }
    internal string SourceText { get; }
    internal PowerShellBoundExpression Value { get; }
    internal bool SetSuccess { get; }
    internal bool EmitsOutput { get; }
    /// <summary>Preserves direct pipeline records before expression scalarization.</summary>
    internal bool IsPipelineStatement { get; }
}
