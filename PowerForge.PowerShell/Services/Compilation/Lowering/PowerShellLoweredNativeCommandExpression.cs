namespace PowerForge;

internal sealed class PowerShellLoweredNativeCommandExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeCommandExpression(SourceSpan span, string source, string sourcePath, string sourceDocument,
        bool preservePartialOutput, PowerShellLoweredCommandStage[] stages) : base(span, typeof(object))
    {
        Source = source;
        SourcePath = sourcePath;
        SourceDocument = sourceDocument;
        PreservePartialOutput = preservePartialOutput;
        Stages = stages;
    }

    internal string Source { get; }
    internal string SourcePath { get; }
    internal string SourceDocument { get; }
    internal bool PreservePartialOutput { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandStage> Stages { get; }
}
