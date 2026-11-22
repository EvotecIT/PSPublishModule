namespace PowerForge;

/// <summary>One authored statement whose error route may resume at the following statement.</summary>
internal sealed class PowerShellBoundStatementErrorBoundary : PowerShellBoundStatement
{
    internal PowerShellBoundStatementErrorBoundary(PowerShellBoundBlock body, string sourcePath, string sourceText,
        bool? nativeSuccessStatus = null, bool nativeSequencePoint = false, bool runtimeFree = false)
        : base(body.Span, body.Effects | PowerShellSemanticEffect.NonSuccessStream | PowerShellSemanticEffect.Host |
            (nativeSuccessStatus.HasValue ? PowerShellSemanticEffect.Mutation : PowerShellSemanticEffect.None),
            body.Capabilities | (runtimeFree ? PowerShellRequiredCapability.None :
                PowerShellRequiredCapability.PowerShellStatementErrors | PowerShellRequiredCapability.PowerShellHostTypes) |
            (nativeSuccessStatus.HasValue || nativeSequencePoint ? PowerShellRequiredCapability.NativeFunctionBinding : PowerShellRequiredCapability.None))
    {
        Body = body;
        SourcePath = sourcePath;
        SourceText = sourceText;
        NativeSuccessStatus = nativeSuccessStatus;
        NativeSequencePoint = nativeSequencePoint;
        RuntimeFree = runtimeFree;
    }

    internal PowerShellBoundBlock Body { get; }
    internal string SourcePath { get; }
    internal string SourceText { get; }
    internal bool? NativeSuccessStatus { get; }
    internal bool NativeSequencePoint { get; }
    internal bool RuntimeFree { get; }
}
