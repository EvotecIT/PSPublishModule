using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellControlFlowBindingPolicy
{
    internal static bool HasAncestor<TAst>(Ast syntax) where TAst : Ast
    {
        for (var parent = syntax.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TAst) return true;
            if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst) return false;
        }
        return false;
    }

    internal static bool HasBreakableAncestor(Ast syntax)
        => HasAncestor<LoopStatementAst>(syntax) || HasAncestor<SwitchStatementAst>(syntax);

    internal static bool HasContinuableAncestor(Ast syntax) => HasBreakableAncestor(syntax);
}
