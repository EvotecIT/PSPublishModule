using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellStrictCatchObservation
{
    private static readonly HashSet<string> SupportedDataCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "Write-Output", "Write-Progress", "Get-ChildItem", "Select-Object", "select", "Where-Object", "where",
        "ForEach-Object", "foreach", "%", "?"
    };

    internal static bool TryClassify(Ast syntax, IEnumerable<string>? localFunctionNames, out PowerShellRuntimeStateIntrinsicKind kind)
    {
        kind = syntax switch
        {
            IndexExpressionAst
            {
                Target: VariableExpressionAst { VariablePath.IsUnqualified: true } error,
                Index: ConstantExpressionAst { Value: 0 }
            } when error.VariablePath.UserPath.Equals("Error", StringComparison.OrdinalIgnoreCase) =>
                PowerShellRuntimeStateIntrinsicKind.CaughtCommandErrorMessage,
            MemberExpressionAst
            {
                Static: false, Member: StringConstantExpressionAst line,
                Expression: MemberExpressionAst
                {
                    Static: false, Member: StringConstantExpressionAst invocation,
                    Expression: VariableExpressionAst { VariablePath.IsUnqualified: true } item
                }
            } when line.Value.Equals("ScriptLineNumber", StringComparison.OrdinalIgnoreCase) &&
                invocation.Value.Equals("InvocationInfo", StringComparison.OrdinalIgnoreCase) &&
                (item.VariablePath.UserPath == "_" || item.VariablePath.UserPath.Equals("PSItem", StringComparison.OrdinalIgnoreCase)) =>
                PowerShellRuntimeStateIntrinsicKind.CaughtCommandErrorLine,
            _ => PowerShellRuntimeStateIntrinsicKind.None
        };
        return kind != PowerShellRuntimeStateIntrinsicKind.None &&
               syntax.Parent is CommandExpressionAst &&
               syntax.Parent.Parent is PipelineAst { PipelineElements.Count: 1 } value &&
               value.Parent is StatementBlockAst { Statements.Count: 1 } &&
               value.Parent.Parent is SubExpressionAst &&
               value.Parent.Parent.Parent is ExpandableStringExpressionAst text &&
               text.Parent is CommandExpressionAst { Redirections.Count: 0 } &&
               text.Parent.Parent is PipelineAst { PipelineElements.Count: 1 } statement &&
               statement.Parent is StatementBlockAst body &&
               ReferenceEquals(body.Statements.FirstOrDefault(), statement) &&
               body.Parent is CatchClauseAst { CatchTypes.Count: 0, Parent: TryStatementAst attempt } &&
               HasBoundedErrorProducers(attempt.Body, localFunctionNames);
    }

    internal static bool Contains(Ast syntax, IEnumerable<string>? localFunctionNames)
    {
        for (Ast? current = syntax; current is not null && current is not StatementAst; current = current.Parent)
            if (TryClassify(current, localFunctionNames, out _)) return true;
        return false;
    }

    internal static bool HasContainingUntypedCatch(Ast syntax)
    {
        for (Ast? child = syntax, parent = syntax.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (parent is TryStatementAst attempt && ReferenceEquals(child, attempt.Body) && attempt.CatchClauses.Count > 0)
                return attempt.CatchClauses.All(static clause => clause.CatchTypes.Count == 0);
            if (parent is ScriptBlockAst) return false;
        }
        return false;
    }

    internal static int GetAuthoredLine(ParsedSourceDocument document, IScriptExtent extent)
        => document.AuthoredProjection?.Select(document, new[] { extent }).Statements.Single().StartLine ?? extent.StartLineNumber;

    private static bool HasBoundedErrorProducers(StatementBlockAst body, IEnumerable<string>? localFunctionNames)
    {
        var names = localFunctionNames as HashSet<string> ?? localFunctionNames?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = false;
        foreach (var operation in body.FindAll(static node => node is ThrowStatementAst or CommandAst or InvokeMemberExpressionAst,
                     searchNestedScriptBlocks: false))
        {
            if (operation is ThrowStatementAst) return false;
            if (operation is InvokeMemberExpressionAst invocation)
            {
                if (invocation is not { Static: true, Expression: TypeExpressionAst timer, Arguments: null or { Count: 0 },
                        Member: StringConstantExpressionAst method } ||
                    timer.TypeName.GetReflectionType() != typeof(System.Diagnostics.Stopwatch) ||
                    !method.Value.Equals(nameof(System.Diagnostics.Stopwatch.StartNew), StringComparison.OrdinalIgnoreCase))
                    return false;
                continue;
            }
            var command = (CommandAst)operation;
            if (IsConsoleCommand(command, names) && PowerShellRuntimeStateCommandSemanticBinder.TryGetConsoleErrorRoute(command, out _) ||
                PowerShellStrictCsvPolicy.TryGetRead(command, names, out _, out _, out _) ||
                PowerShellStrictFileCommandPolicy.TryGetResolvePath(command, names, out _, out _))
            {
                found = true;
                continue;
            }
            var name = command.GetCommandName();
            if (name is null || names?.Contains(name) == true || !SupportedDataCommands.Contains(name.Split('\\').Last())) return false;
        }
        return found;
    }

    private static bool IsConsoleCommand(CommandAst command, ISet<string>? localFunctionNames)
    {
        var name = command.GetCommandName();
        return string.Equals(name, "Read-Host", StringComparison.OrdinalIgnoreCase) && localFunctionNames?.Contains(name!) != true ||
               string.Equals(name, "Microsoft.PowerShell.Utility\\Read-Host", StringComparison.OrdinalIgnoreCase);
    }
}
