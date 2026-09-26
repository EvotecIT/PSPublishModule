namespace PowerForge;

/// <summary>A native script-block value whose executable body is another canonically bound function.</summary>
internal sealed class PowerShellBoundNativeScriptBlockExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeScriptBlockExpression(SourceSpan span, PowerShellSymbolId target, string sourceDocument,
        string? declarationName = null, bool isSwitchPredicate = false)
        : base(span, new PowerShellTypeFact(typeof(System.Management.Automation.ScriptBlock), PowerShellTypeFactProvenance.Inferred,
                "A compiled body retains native script-block invocation and source metadata."),
            PowerShellValueState.Known, PowerShellSemanticEffect.Host,
            PowerShellRequiredCapability.PowerShellHostTypes | PowerShellRequiredCapability.NativeFunctionBinding |
            (declarationName is null ? PowerShellRequiredCapability.None : PowerShellRequiredCapability.PowerShellStatementErrors))
    {
        Target = target;
        SourceDocument = sourceDocument;
        DeclarationName = declarationName;
        IsSwitchPredicate = isSwitchPredicate;
    }

    internal PowerShellSymbolId Target { get; }
    internal string SourceDocument { get; }
    /// <summary>Names a statement-time nested declaration; null denotes an ordinary literal value.</summary>
    internal string? DeclarationName { get; }
    /// <summary>Retains the native constant-block identity for a switch predicate.</summary>
    internal bool IsSwitchPredicate { get; }
}
