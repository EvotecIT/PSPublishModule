using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellRuntimeExceptionCatchPolicy
{
    internal static bool Contains(Ast node)
        => Contains(node, static type => type == typeof(RuntimeException));

    internal static bool ContainsNumericErrorWrapping(Ast node)
        => Contains(node, static type => type == typeof(RuntimeException) || type == typeof(PSInvalidCastException));

    private static bool Contains(Ast node, Func<Type?, bool> matches)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is not TryStatementAst tryStatement ||
                !ContainsExtent(tryStatement.Body, node))
                continue;

            if (tryStatement.CatchClauses.Any(clause =>
                    clause.CatchTypes.Any(constraint => matches(constraint.TypeName.GetReflectionType()))))
                return true;
        }

        return false;
    }

    private static bool ContainsExtent(Ast container, Ast candidate)
        => candidate.Extent.StartOffset >= container.Extent.StartOffset &&
           candidate.Extent.EndOffset <= container.Extent.EndOffset;
}
