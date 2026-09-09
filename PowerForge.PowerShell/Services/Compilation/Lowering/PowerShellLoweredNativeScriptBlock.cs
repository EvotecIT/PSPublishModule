namespace PowerForge;

/// <summary>Connects a native block value to its separately lowered executable body.</summary>
internal sealed class PowerShellLoweredNativeScriptBlockExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeScriptBlockExpression(SourceSpan span, PowerShellSymbolId target, string sourceDocument)
        : base(span, typeof(System.Management.Automation.ScriptBlock))
    {
        Target = target;
        SourceDocument = sourceDocument;
    }

    internal PowerShellSymbolId Target { get; }
    internal string SourceDocument { get; }
}
