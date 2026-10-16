using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Identifies an authored declaration owned by a source root rather than a callback or enclosing function.</summary>
internal sealed class PowerShellRootFunctionDeclaration
{
    internal PowerShellRootFunctionDeclaration(FunctionDefinitionAst syntax, ScriptBlockAst root)
    {
        Syntax = syntax;
        IsConditional = syntax.Parent is not NamedBlockAst || !ReferenceEquals(syntax.Parent.Parent, root);
    }

    internal FunctionDefinitionAst Syntax { get; }
    internal bool IsConditional { get; }

    // Module initialization keeps control flow in PowerShell. The native body is registered
    // only when the original declaration executes; executable-root rewriting has a separate owner.
    internal bool UsesConditionalNativeBinding(PowerShellCompilationCapability capabilities)
        => IsConditional && !Syntax.IsWorkflow && capabilities == PowerShellCompilationCapabilities.HybridModule;

    internal string GetBindingName(ParsedSourceDocument document, PowerShellCompilationCapability capabilities)
    {
        if (!UsesConditionalNativeBinding(capabilities)) return Syntax.Name;
        var name = "__PowerForgeConditionalFunction_" + document.DocumentId + "_" +
                   Syntax.Extent.StartOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        while (document.RootFunctionDeclarations.Any(declaration => declaration.Syntax.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name += "_";
        return name;
    }
}
