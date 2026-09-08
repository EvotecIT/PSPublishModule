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

    internal static bool HasTransferLeavingFinally(StatementBlockAst block)
        => block.FindAll(static node => node is ReturnStatementAst or BreakStatementAst or ContinueStatementAst,
                searchNestedScriptBlocks: false)
            .Any(node => LeavesFinally(node, block));

    private static bool LeavesFinally(Ast transfer, StatementBlockAst block)
    {
        if (transfer is ReturnStatementAst or BreakStatementAst { Label: not null } or ContinueStatementAst { Label: not null })
            return true;
        for (var parent = transfer.Parent; parent is not null && !ReferenceEquals(parent, block); parent = parent.Parent)
            if (parent is LoopStatementAst or SwitchStatementAst) return false;
        return true;
    }
}
