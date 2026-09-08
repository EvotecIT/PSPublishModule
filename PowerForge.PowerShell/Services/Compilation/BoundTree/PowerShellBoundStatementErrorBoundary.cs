namespace PowerForge;

/// <summary>One authored statement whose error route may resume at the following statement.</summary>
internal sealed class PowerShellBoundStatementErrorBoundary : PowerShellBoundStatement
{
    internal PowerShellBoundStatementErrorBoundary(PowerShellBoundBlock body, string sourcePath, string sourceText,
        bool? nativeSuccessStatus = null, bool nativeSequencePoint = false)
        : base(body.Span, body.Effects | PowerShellSemanticEffect.NonSuccessStream | PowerShellSemanticEffect.Host |
            (nativeSuccessStatus.HasValue ? PowerShellSemanticEffect.Mutation : PowerShellSemanticEffect.None),
            body.Capabilities | PowerShellRequiredCapability.PowerShellStatementErrors | PowerShellRequiredCapability.PowerShellHostTypes |
            (nativeSuccessStatus.HasValue || nativeSequencePoint ? PowerShellRequiredCapability.NativeFunctionBinding : PowerShellRequiredCapability.None))
    {
        Body = body;
        SourcePath = sourcePath;
        SourceText = sourceText;
        NativeSuccessStatus = nativeSuccessStatus;
        NativeSequencePoint = nativeSequencePoint;
    }

    internal PowerShellBoundBlock Body { get; }
    internal string SourcePath { get; }
    internal string SourceText { get; }
    internal bool? NativeSuccessStatus { get; }
    internal bool NativeSequencePoint { get; }
}
