using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Finds reads of automatic variables whose runtime-owned state a bounded typed operation would otherwise hide.
/// </summary>
internal static class PowerShellAutomaticVariableObservationPolicy
{
    /// <summary>Separates a catch-owned error item from the enclosing switch's current item.</summary>
    internal static bool ObservesSwitchState(SwitchStatementAst statement)
        => statement.FindAll(node => node is VariableExpressionAst variable &&
                !PowerShellAssignmentTargetPolicy.IsDirectAssignmentTarget(variable) &&
                (variable.VariablePath.UserPath.Equals("switch", StringComparison.OrdinalIgnoreCase) ||
                 (variable.VariablePath.UserPath.Equals("_", StringComparison.OrdinalIgnoreCase) ||
                  variable.VariablePath.UserPath.Equals("PSItem", StringComparison.OrdinalIgnoreCase)) &&
                 !HasImmediateCatchOwner(variable, statement)),
            searchNestedScriptBlocks: true).Any();

    private static bool HasImmediateCatchOwner(VariableExpressionAst variable, SwitchStatementAst statement)
    {
        for (Ast? ancestor = variable.Parent; ancestor is not null && !ReferenceEquals(ancestor, statement);
             ancestor = ancestor.Parent)
        {
            // A deferred block can execute after catch has restored its item, and an
            // inner switch supplies its own item. Neither inherits this exemption.
            if (ancestor is ScriptBlockAst or FunctionDefinitionAst or SwitchStatementAst) return false;
            if (ancestor is CatchClauseAst) return true;
        }
        return false;
    }

    internal static bool ObservesWithin(Ast syntax, params string[] names)
    {
        var observedNames = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return syntax.FindAll(
            node => node is VariableExpressionAst variable &&
                    observedNames.Contains(variable.VariablePath.UserPath) &&
                    !PowerShellAssignmentTargetPolicy.IsDirectAssignmentTarget(variable),
            searchNestedScriptBlocks: true).Any();
    }

    internal static bool Observes(Ast syntax, params string[] names)
    {
        Ast root = syntax;
        while (root.Parent is not null && root is not FunctionDefinitionAst) root = root.Parent;
        if (root is FunctionDefinitionAst function) root = function.Body;
        return ObservesWithin(root, names);
    }
}
