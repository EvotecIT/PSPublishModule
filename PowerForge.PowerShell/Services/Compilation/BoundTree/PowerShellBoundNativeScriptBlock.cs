namespace PowerForge;

/// <summary>A native script-block value whose executable body is another canonically bound function.</summary>
internal sealed class PowerShellBoundNativeScriptBlockExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeScriptBlockExpression(SourceSpan span, PowerShellSymbolId target, string sourceDocument)
        : base(span, new PowerShellTypeFact(typeof(System.Management.Automation.ScriptBlock), PowerShellTypeFactProvenance.Inferred,
                "A compiled body retains native script-block invocation and source metadata."),
            PowerShellValueState.Known, PowerShellSemanticEffect.Host,
            PowerShellRequiredCapability.PowerShellHostTypes | PowerShellRequiredCapability.NativeFunctionBinding)
    {
        Target = target;
        SourceDocument = sourceDocument;
    }

    internal PowerShellSymbolId Target { get; }
    internal string SourceDocument { get; }
}
