namespace PowerForge;

/// <summary>One authored statement whose error route may resume at the following statement.</summary>
internal sealed class PowerShellBoundStatementErrorBoundary : PowerShellBoundStatement
{
    internal PowerShellBoundStatementErrorBoundary(PowerShellBoundBlock body, string sourcePath, string sourceText)
        : base(body.Span, body.Effects | PowerShellSemanticEffect.NonSuccessStream | PowerShellSemanticEffect.Host,
            body.Capabilities | PowerShellRequiredCapability.PowerShellStatementErrors | PowerShellRequiredCapability.PowerShellHostTypes)
    {
        Body = body;
        SourcePath = sourcePath;
        SourceText = sourceText;
    }

    internal PowerShellBoundBlock Body { get; }
    internal string SourcePath { get; }
    internal string SourceText { get; }
}
