namespace PowerForge;

/// <summary>Captures one explicit hosted pipeline without creating a separate invocation scope.</summary>
internal sealed class PowerShellBoundNativeCommandExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeCommandExpression(SourceSpan span, string source, string sourcePath, string sourceDocument,
        bool preservePartialOutput, PowerShellBoundCommandStage[] stages, bool preserveReturnRecords = false)
        : base(span, PowerShellTypeFact.Unknown, PowerShellValueState.Unknown,
            PowerShellSemanticEffect.Host | PowerShellSemanticEffect.Mutation | PowerShellSemanticEffect.NonSuccessStream |
            PowerShellSemanticEffect.TerminatingError,
            PowerShellRequiredCapability.CommandRegion | PowerShellRequiredCapability.NativeFunctionBinding |
            PowerShellRequiredCapability.PowerShellHost | PowerShellRequiredCapability.PowerShellStatementErrors)
    {
        Source = source;
        SourcePath = sourcePath;
        SourceDocument = sourceDocument;
        PreservePartialOutput = preservePartialOutput;
        Stages = stages;
        PreserveReturnRecords = preserveReturnRecords;
    }

    internal string Source { get; }
    internal string SourcePath { get; }
    internal string SourceDocument { get; }
    internal bool PreservePartialOutput { get; }
    /// <summary>A direct return pipeline transfers records without expression scalarization or a second enumeration.</summary>
    internal bool PreserveReturnRecords { get; }
    internal PowerShellImmutableArray<PowerShellBoundCommandStage> Stages { get; }
}
