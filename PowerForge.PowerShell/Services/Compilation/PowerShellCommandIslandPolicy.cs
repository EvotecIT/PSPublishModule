using System.Management.Automation.Language;

namespace PowerForge;

internal enum PowerShellStreamCommandKind
{
    Verbose,
    Debug,
    Warning
}

internal static class PowerShellCommandIslandPolicy
{
    internal static bool IsRuntimeRegion(StatementAst statement, ScriptBlockAst body)
    {
        if (!ReferenceEquals(statement.Parent, body.EndBlock))
            return false;
        var commands = statement.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).Cast<CommandAst>().ToArray();
        if (commands.Length == 0 || commands.Any(static command => command.Redirections.Count != 0))
            return false;
        if (statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
            pipeline.PipelineElements[0] is CommandAst stream &&
            TryGetStreamCommand(stream, out _, out _))
            return false;
        if (statement.FindAll(static node => node is AssignmentStatementAst or ReturnStatementAst or BreakStatementAst or ContinueStatementAst or ThrowStatementAst, searchNestedScriptBlocks: true).Any())
            return false;

        var parameters = body.ParamBlock?.Parameters
            .Select(static parameter => parameter.Name.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return statement.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
            .Cast<VariableExpressionAst>()
            .All(variable =>
            {
                var name = variable.VariablePath.UserPath;
                return parameters.Contains(name) ||
                       name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                       IsNestedPipelineVariable(variable, statement, name);
            });
    }

    internal static bool TryGetRuntimeRegion(CommandAst command, ScriptBlockAst body, out StatementAst region)
    {
        for (Ast? current = command; current is not null && !ReferenceEquals(current, body); current = current.Parent)
        {
            if (current is StatementAst statement && ReferenceEquals(statement.Parent, body.EndBlock) && IsRuntimeRegion(statement, body))
            {
                region = statement;
                return true;
            }
        }
        region = null!;
        return false;
    }

    private static bool IsNestedPipelineVariable(
        VariableExpressionAst variable,
        StatementAst region,
        string name)
    {
        if (!name.Equals("_", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("PSItem", StringComparison.OrdinalIgnoreCase))
            return false;

        for (Ast? ancestor = variable.Parent;
             ancestor is not null && !ReferenceEquals(ancestor, region);
             ancestor = ancestor.Parent)
        {
            if (ancestor is ScriptBlockExpressionAst)
                return true;
        }

        return false;
    }

    internal static bool TryGetStreamCommand(
        CommandAst command,
        out PowerShellStreamCommandKind kind,
        out ExpressionAst message)
    {
        kind = default;
        message = null!;
        if (command.Parent is not PipelineAst pipeline ||
            pipeline.PipelineElements.Count != 1 ||
            command.Redirections.Count != 0)
            return false;

        switch (command.GetCommandName()?.ToUpperInvariant())
        {
            case "WRITE-VERBOSE": kind = PowerShellStreamCommandKind.Verbose; break;
            case "WRITE-DEBUG": kind = PowerShellStreamCommandKind.Debug; break;
            case "WRITE-WARNING": kind = PowerShellStreamCommandKind.Warning; break;
            default: return false;
        }

        var arguments = command.CommandElements.Skip(1).ToArray();
        if (arguments.Length == 1 && arguments[0] is ExpressionAst positional)
        {
            message = positional;
            return true;
        }
        if (arguments.Length == 2 &&
            arguments[0] is CommandParameterAst parameter &&
            parameter.ParameterName.Equals("Message", StringComparison.OrdinalIgnoreCase) &&
            arguments[1] is ExpressionAst named)
        {
            message = named;
            return true;
        }
        return false;
    }
}
