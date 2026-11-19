using System.Management.Automation;
using System.Management.Automation.Language;
using System.Security.Cryptography;

namespace PowerForge;

/// <summary>Finds literal aliases registered for authenticated dependency types without executing module code.</summary>
internal static class PowerShellNativeTypeAliases
{
    internal static void Add(string root, string graphRoot,
        IEnumerable<PowerShellCompilationDependency> dependencies, PowerShellCompilationDependencyGraph graph,
        HashSet<string> names)
    {
        var documents = new List<ScriptBlockAst>();
        foreach (var dependency in dependencies.Where(static dependency => dependency.Exists &&
                     dependency.Kind == PowerShellCompilationDependencyKind.PowerShellSource &&
                     dependency.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent &&
                     dependency.SourcePath is not null))
        {
            var path = Path.GetFullPath(dependency.SourcePath!);
            PowerShellCompilationPathSafety.EnsureContained(root, path, "Type alias source must stay inside the root module.");
            PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(path, "Type alias source must not traverse links.");
            var nodes = graph.Nodes.Where(node => node.Exists &&
                node.Disposition != PowerShellCompilationDependencyGraphDisposition.Rejected &&
                PowerShellCompilationPathSafety.PathEquals(Path.GetFullPath(Path.Combine(graphRoot, node.Identity.Source)), path)).ToArray();
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Identity.Sha256)) continue;
            var bytes = File.ReadAllBytes(path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
            if (!hash.Equals(nodes[0].Identity.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Type alias source changed after dependency locking.");
            using var reader = new StreamReader(new MemoryStream(bytes, writable: false), detectEncodingFromByteOrderMarks: true);
            var syntax = Parser.ParseInput(reader.ReadToEnd(), out _, out var errors);
            if (errors.Length == 0) documents.Add(syntax);
        }

        var setters = documents.SelectMany(document => document.FindAll(static node => node is FunctionDefinitionAst,
                searchNestedScriptBlocks: true).OfType<FunctionDefinitionAst>())
            .SelectMany(FindSetters).GroupBy(static setter => setter.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Select(setter => setter.Parameter).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.First().Parameter, StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        foreach (var command in document.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).OfType<CommandAst>())
        {
            if (command.GetCommandName() is not { } name || !setters.TryGetValue(name, out var parameter)) continue;
            for (var index = 1; index < command.CommandElements.Count; index++)
            {
                if (command.CommandElements[index] is not CommandParameterAst option ||
                    !option.ParameterName.Equals(parameter, StringComparison.OrdinalIgnoreCase)) continue;
                var argument = option.Argument ?? (index + 1 < command.CommandElements.Count ? command.CommandElements[index + 1] : null);
                var table = FindTable(document, argument);
                if (table is null) continue;
                foreach (var pair in table.KeyValuePairs)
                {
                    if (pair.Item1 is not StringConstantExpressionAst key ||
                        PowerShellSemanticBinder.UnwrapExpression(pair.Item2) is not StringConstantExpressionAst value ||
                        !names.Contains(value.Value) || string.IsNullOrEmpty(key.Value) ||
                        key.Value.Any(static character => !char.IsLetterOrDigit(character) && character != '_')) continue;
                    if (names.Contains(key.Value) || aliases.TryGetValue(key.Value, out var previous) &&
                        !previous.Equals(value.Value, StringComparison.OrdinalIgnoreCase)) ambiguous.Add(key.Value);
                    else aliases[key.Value] = value.Value;
                }
            }
        }
        foreach (var alias in aliases.Keys.Where(alias => !ambiguous.Contains(alias))) names.Add(alias);
    }

    private static HashtableAst? FindTable(ScriptBlockAst document, Ast? argument)
    {
        if (argument is HashtableAst literal) return literal;
        if (argument is not VariableExpressionAst variable || !variable.VariablePath.IsUnqualified) return null;
        var assignments = document.FindAll(node => node is AssignmentStatementAst
            { Operator: TokenKind.Equals, Left: VariableExpressionAst left } &&
            left.VariablePath.UserPath.Equals(variable.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase),
            searchNestedScriptBlocks: false).OfType<AssignmentStatementAst>().ToArray();
        return assignments.Length == 1 ? PowerShellSemanticBinder.UnwrapExpression(assignments[0].Right) as HashtableAst : null;
    }

    private static IEnumerable<(string Name, string Parameter)> FindSetters(FunctionDefinitionAst function)
    {
        foreach (var call in function.Body.FindAll(static node => node is InvokeMemberExpressionAst,
                     searchNestedScriptBlocks: false).OfType<InvokeMemberExpressionAst>())
        {
            if (!call.Static || call.Member is not StringConstantExpressionAst member ||
                !member.Value.Equals("Add", StringComparison.OrdinalIgnoreCase) || call.Arguments?.Count != 2 ||
                call.Expression is not VariableExpressionAst receiver ||
                call.Arguments[0] is not VariableExpressionAst key ||
                call.Arguments[1] is not IndexExpressionAst { Target: VariableExpressionAst mapping, Index: VariableExpressionAst index } ||
                !key.VariablePath.UserPath.Equals(index.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase) ||
                !PowerShellParameterSyntax.GetParameters(function.Body).Any(parameter =>
                    parameter.Name.VariablePath.UserPath.Equals(mapping.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase))) continue;
            var assignments = function.Body.FindAll(node => node is AssignmentStatementAst
                { Operator: TokenKind.Equals, Left: VariableExpressionAst left } &&
                left.VariablePath.UserPath.Equals(receiver.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: false).OfType<AssignmentStatementAst>().ToArray();
            if (assignments.Length != 1 || PowerShellSemanticBinder.UnwrapExpression(assignments[0].Right) is not InvokeMemberExpressionAst
                { Static: false, Expression: MemberExpressionAst { Static: false, Expression: TypeExpressionAst sdk,
                    Member: StringConstantExpressionAst assembly }, Member: StringConstantExpressionAst method,
                    Arguments.Count: 1, Arguments: var arguments } || sdk.TypeName.GetReflectionType() != typeof(PSObject) ||
                !assembly.Value.Equals("Assembly", StringComparison.OrdinalIgnoreCase) ||
                !method.Value.Equals("GetType", StringComparison.OrdinalIgnoreCase) ||
                arguments[0] is not StringConstantExpressionAst type ||
                type.Value != "System.Management.Automation.TypeAccelerators") continue;
            yield return (function.Name, mapping.VariablePath.UserPath);
        }
    }
}
