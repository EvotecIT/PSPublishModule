using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellRuntimeExceptionCatchPolicy
{
    internal static bool Contains(Ast node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is not TryStatementAst tryStatement ||
                !ContainsExtent(tryStatement.Body, node))
                continue;

            if (tryStatement.CatchClauses.Any(static clause =>
                    clause.CatchTypes.Any(static constraint =>
                        constraint.TypeName.GetReflectionType() == typeof(RuntimeException))))
                return true;
        }

        return false;
    }

    private static bool ContainsExtent(Ast container, Ast candidate)
        => candidate.Extent.StartOffset >= container.Extent.StartOffset &&
           candidate.Extent.EndOffset <= container.Extent.EndOffset;
}
