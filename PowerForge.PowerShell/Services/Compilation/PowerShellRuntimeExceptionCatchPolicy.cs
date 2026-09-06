using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellRuntimeExceptionCatchPolicy
{
    internal static bool Contains(Ast node)
        => Contains(node, static type => type == typeof(RuntimeException));

    internal static bool ContainsNumericErrorWrapping(Ast node)
        => Contains(node, static type => type == typeof(RuntimeException) || type == typeof(PSInvalidCastException));

    /// <summary>
    /// Records local callees whose errors can reach a PowerShell-specific catch,
    /// including callers that cannot bind and will remain authored PowerShell.
    /// Source observations select the boundary; bound expressions determine which
    /// callees actually require an unsupported numeric wrapper.
    /// </summary>
    internal static HashSet<string> FindNumericErrorObservedCallees(IEnumerable<ParsedSourceDocument> documents)
    {
        var roots = documents.Select(static document => document.SyntaxRoot).ToArray();
        var functions = roots.SelectMany(root => root.FindAll(static node => node is FunctionDefinitionAst, true))
            .Cast<FunctionDefinitionAst>().GroupBy(static function => function.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(roots.SelectMany(root => root.FindAll(static node => node is CommandAst, true))
            .Cast<CommandAst>().Where(ContainsNumericErrorWrapping)
            .Select(static command => command.GetCommandName()).OfType<string>());
        while (pending.Count > 0)
        {
            var name = pending.Dequeue();
            if (!functions.TryGetValue(name, out var declarations) || !observed.Add(name)) continue;
            foreach (var command in declarations.SelectMany(declaration => declaration.Body.FindAll(static node => node is CommandAst, true))
                         .Cast<CommandAst>())
            {
                if (command.GetCommandName() is { } callee) pending.Enqueue(callee);
            }
        }
        return observed;
    }

    internal static bool RequiresNumericErrorWrapping(PowerShellBoundFunction function)
        => PowerShellSemanticAnalyzer.EnumerateStatements(function.Body)
            .OfType<PowerShellBoundAssignmentStatement>()
            .Any(static assignment => assignment.IntegralSemantics != PowerShellIntegralMutationSemantics.None ||
                assignment.Operation != PowerShellBoundMutationOperator.Assign && assignment.Value.Type.ClrType == typeof(decimal)) ||
            PowerShellSemanticAnalyzer.EnumerateStatements(function.Body)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
            .Any(static expression => expression switch
            {
                PowerShellBoundMutationExpression mutation => mutation.Operation != PowerShellBoundMutationOperator.Assign &&
                    (mutation.IntegralSemantics != PowerShellIntegralMutationSemantics.None || mutation.TargetClrType == typeof(decimal)),
                PowerShellBoundBinaryExpression binary => binary.Operation == PowerShellBoundBinaryOperator.IntegralRemainder ||
                    binary.Type.ClrType == typeof(decimal) && binary.Operation is PowerShellBoundBinaryOperator.Add or
                        PowerShellBoundBinaryOperator.Subtract or PowerShellBoundBinaryOperator.Multiply or
                        PowerShellBoundBinaryOperator.Divide or PowerShellBoundBinaryOperator.Remainder,
                _ => false
            });

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
