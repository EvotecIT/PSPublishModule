namespace PowerForge;

internal sealed class PowerShellLoweredNativeCommandExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeCommandExpression(SourceSpan span, string source, string sourcePath, string sourceDocument,
        bool preservePartialOutput, PowerShellLoweredCommandStage[] stages, bool preserveReturnRecords = false,
        int[]? guardedPathOperands = null) : base(span, typeof(object))
    {
        Source = source;
        SourcePath = sourcePath;
        SourceDocument = sourceDocument;
        PreservePartialOutput = preservePartialOutput;
        Stages = stages;
        PreserveReturnRecords = preserveReturnRecords;
        GuardedPathOperands = guardedPathOperands ?? Array.Empty<int>();
    }

    internal string Source { get; }
    internal string SourcePath { get; }
    internal string SourceDocument { get; }
    internal bool PreservePartialOutput { get; }
    internal bool PreserveReturnRecords { get; }
    internal PowerShellImmutableArray<int> GuardedPathOperands { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandStage> Stages { get; }
}
