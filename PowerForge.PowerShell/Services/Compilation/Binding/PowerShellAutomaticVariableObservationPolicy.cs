using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Finds reads of automatic variables whose runtime-owned state a bounded typed operation would otherwise hide.
/// </summary>
internal static class PowerShellAutomaticVariableObservationPolicy
{
    internal static bool Observes(Ast syntax, params string[] names)
    {
        var observedNames = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Ast root = syntax;
        while (root.Parent is not null && root is not FunctionDefinitionAst) root = root.Parent;
        if (root is FunctionDefinitionAst function) root = function.Body;
        return root.FindAll(
            node => node is VariableExpressionAst variable &&
                    observedNames.Contains(variable.VariablePath.UserPath) &&
                    !PowerShellAssignmentTargetPolicy.IsDirectAssignmentTarget(variable),
            searchNestedScriptBlocks: true).Any();
    }
}
