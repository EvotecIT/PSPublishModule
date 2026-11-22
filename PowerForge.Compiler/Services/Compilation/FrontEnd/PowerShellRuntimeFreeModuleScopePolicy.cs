using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Proves that an inherited read has no compiled caller scope that can shadow its module field.</summary>
internal static class PowerShellRuntimeFreeModuleScopePolicy
{
    internal static void Validate(IReadOnlyList<ParsedSourceDocument> documents,
        IEnumerable<PowerShellBoundLocal> fields, ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var names = fields.Select(static field => field.Symbol.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var functions = documents.SelectMany(document => document.SyntaxRoot.FindAll(
                static syntax => syntax is FunctionDefinitionAst, false).Cast<FunctionDefinitionAst>()
            .Select(function => (Document: document, Function: function))).ToArray();
        var callers = functions.SelectMany(caller => caller.Function.Body.FindAll(
                static syntax => syntax is CommandAst, false).Cast<CommandAst>()
            .Select(command => (Name: command.GetCommandName(), Caller: caller.Function)))
            .Where(static call => call.Name is not null)
            .GroupBy(static call => call.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Select(call => call.Caller).ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var item in functions)
        {
            foreach (var read in item.Function.Body.FindAll(static syntax => syntax is VariableExpressionAst, false)
                         .Cast<VariableExpressionAst>().Where(variable => variable.VariablePath.IsUnqualified &&
                             names.Contains(variable.VariablePath.UserPath)))
            {
                var name = read.VariablePath.UserPath;
                if (DeclaresLocal(item.Function, name)) continue;
                var pending = new Stack<string>();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                pending.Push(item.Function.Name);
                while (pending.Count > 0)
                {
                    var callee = pending.Pop();
                    if (!visited.Add(callee) || !callers.TryGetValue(callee, out var parents)) continue;
                    foreach (var parent in parents)
                    {
                        if (DeclaresLocal(parent, name))
                        {
                            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2952",
                                $"Inherited variable '${name}' can be shadowed by caller '{parent.Name}'; runtime-free compilation requires a qualified dynamic-scope contract for this call chain.",
                                PowerShellSourceParser.GetSpan(item.Document, read.Extent)));
                            pending.Clear();
                            break;
                        }
                        pending.Push(parent.Name);
                    }
                }
            }
        }
    }

    private static bool DeclaresLocal(FunctionDefinitionAst function, string name)
        => (function.Parameters ?? Enumerable.Empty<ParameterAst>())
               .Concat(function.Body.ParamBlock?.Parameters ?? Enumerable.Empty<ParameterAst>())
               .Any(parameter => parameter.Name.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
           function.Body.FindAll(static syntax => syntax is AssignmentStatementAst or ForEachStatementAst or UnaryExpressionAst, false)
               .Any(syntax => GetWrittenVariable(syntax) is { } variable &&
                   (variable.VariablePath.IsUnqualified || variable.VariablePath.IsLocal) &&
                   (variable.VariablePath.IsUnqualified ? variable.VariablePath.UserPath :
                       variable.VariablePath.UserPath.Substring(variable.VariablePath.UserPath.IndexOf(':') + 1))
                   .Equals(name, StringComparison.OrdinalIgnoreCase));

    private static VariableExpressionAst? GetWrittenVariable(Ast syntax)
        => syntax switch
        {
            AssignmentStatementAst assignment => PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left),
            ForEachStatementAst loop => loop.Variable,
            UnaryExpressionAst unary when unary.TokenKind is TokenKind.PlusPlus or TokenKind.MinusMinus or TokenKind.PostfixPlusPlus or TokenKind.PostfixMinusMinus
                => unary.Child as VariableExpressionAst,
            _ => null
        };
}
