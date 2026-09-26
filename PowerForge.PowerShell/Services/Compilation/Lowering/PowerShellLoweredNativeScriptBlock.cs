namespace PowerForge;

/// <summary>Connects a native block value to its separately lowered executable body.</summary>
internal sealed class PowerShellLoweredNativeScriptBlockExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeScriptBlockExpression(SourceSpan span, PowerShellSymbolId target, string sourceDocument,
        string? declarationName = null, bool isSwitchPredicate = false)
        : base(span, typeof(System.Management.Automation.ScriptBlock))
    {
        Target = target;
        SourceDocument = sourceDocument;
        DeclarationName = declarationName;
        IsSwitchPredicate = isSwitchPredicate;
    }

    internal PowerShellSymbolId Target { get; }
    internal string SourceDocument { get; }
    internal string? DeclarationName { get; }
    internal bool IsSwitchPredicate { get; }
}
