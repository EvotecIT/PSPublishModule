namespace PowerForge;

/// <summary>Native host dispatch for a failed authored statement and its continuation.</summary>
internal sealed class PowerShellLoweredStatementErrorBoundary : PowerShellLoweredStatement
{
    internal PowerShellLoweredStatementErrorBoundary(SourceSpan span, PowerShellLoweredStatement[] statements,
        string sourcePath, string sourceText, string exceptionTemporary, bool? nativeSuccessStatus, bool nativeSequencePoint = false) : base(span)
    {
        Statements = statements;
        SourcePath = sourcePath;
        SourceText = sourceText;
        ExceptionTemporary = exceptionTemporary;
        NativeSuccessStatus = nativeSuccessStatus;
        NativeSequencePoint = nativeSequencePoint;
    }

    internal PowerShellImmutableArray<PowerShellLoweredStatement> Statements { get; }
    internal string SourcePath { get; }
    internal string SourceText { get; }
    internal string ExceptionTemporary { get; }
    internal bool? NativeSuccessStatus { get; }
    internal bool NativeSequencePoint { get; }
}
