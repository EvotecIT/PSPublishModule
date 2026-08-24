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
    internal static int FindRuntimeTailStart(
        IReadOnlyList<StatementAst> statements,
        ScriptBlockAst body,
        ISet<string>? localFunctionNames = null)
    {
        var parameters = body.ParamBlock?.Parameters
            .Select(static parameter => parameter.Name.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < statements.Count; index++)
        {
            if (statements[index] is not AssignmentStatementAst assignment ||
                !assignment.Right.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).Any())
                continue;
            if (statements.Take(index).Any(static statement =>
                    statement.FindAll(static node => node is ReturnStatementAst or BreakStatementAst or ContinueStatementAst or ThrowStatementAst, searchNestedScriptBlocks: true).Any()))
                continue;

            var prefixAssignments = statements.Take(index)
                .SelectMany(static statement => statement.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: false))
                .OfType<AssignmentStatementAst>()
                .Select(static item => PowerShellAssignmentTargetPolicy.FindDirectVariable(item.Left)?.VariablePath.UserPath)
                .Where(static name => name is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var available = new HashSet<string>(parameters, StringComparer.OrdinalIgnoreCase);
            available.UnionWith(prefixAssignments);
            var tail = statements.Skip(index).ToArray();
            var assigned = tail
                .SelectMany(static statement => statement.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: true))
                .OfType<AssignmentStatementAst>()
                .Select(static item => PowerShellAssignmentTargetPolicy.FindDirectVariable(item.Left)?.VariablePath.UserPath)
                .Where(static name => name is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (assigned.Overlaps(available))
                continue;
            var commands = tail
                .SelectMany(static statement => statement.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true))
                .OfType<CommandAst>()
                .ToArray();
            if (commands.Length == 0 || commands.Any(static command => command.Redirections.Count != 0))
                continue;
            if (localFunctionNames is not null && commands.Any(command =>
                    command.InvocationOperator == TokenKind.Unknown &&
                    command.GetCommandName() is { } name &&
                    localFunctionNames.Contains(name)))
                continue;
            var variablesSafe = tail
                .SelectMany(static statement => statement.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true))
                .OfType<VariableExpressionAst>()
                .All(variable =>
                {
                    var name = variable.VariablePath.UserPath;
                    if (HasNestedScriptBlockAncestor(variable, statements[index]))
                        return IsNestedPipelineVariable(variable, statements[index], name) || available.Contains(name) || assigned.Contains(name);
                    return available.Contains(name) || assigned.Contains(name) ||
                           name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("null", StringComparison.OrdinalIgnoreCase);
                });
            if (variablesSafe)
                return index;
        }
        return -1;
    }

    internal static bool TryGetRuntimeTailRegion(
        CommandAst command,
        ScriptBlockAst body,
        ISet<string>? localFunctionNames,
        out StatementAst region)
    {
        var statements = body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>();
        var start = FindRuntimeTailStart(statements, body, localFunctionNames);
        if (start >= 0 && command.Extent.StartOffset >= statements[start].Extent.StartOffset)
        {
            region = statements[start];
            return true;
        }
        region = null!;
        return false;
    }

    internal static bool IsRuntimeRegion(
        StatementAst statement,
        ScriptBlockAst body,
        ISet<string>? localFunctionNames = null,
        ISet<string>? allowedVariables = null)
    {
        if (!ReferenceEquals(statement.Parent, body.EndBlock))
            return false;
        var commands = statement.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).Cast<CommandAst>().ToArray();
        if (commands.Length == 0 || commands.Any(static command => command.Redirections.Count != 0))
            return false;
        if (localFunctionNames is not null && commands.Any(command =>
                command.InvocationOperator == TokenKind.Unknown &&
                command.GetCommandName() is { } name &&
                localFunctionNames.Contains(name)))
            return false;
        if (statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
            pipeline.PipelineElements[0] is CommandAst stream &&
            TryGetStreamCommand(stream, out _, out _))
            return false;
        if (statement.FindAll(static node => node is AssignmentStatementAst or ReturnStatementAst or BreakStatementAst or ContinueStatementAst or ThrowStatementAst, searchNestedScriptBlocks: true).Any())
            return false;

        var parameters = allowedVariables ?? body.ParamBlock?.Parameters
            .Select(static parameter => parameter.Name.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return statement.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
            .Cast<VariableExpressionAst>()
            .All(variable =>
            {
                var name = variable.VariablePath.UserPath;
                if (HasNestedScriptBlockAncestor(variable, statement))
                    return IsNestedPipelineVariable(variable, statement, name);
                return parameters.Contains(name) ||
                       name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                       IsNestedPipelineVariable(variable, statement, name);
            });
    }

    private static bool HasNestedScriptBlockAncestor(VariableExpressionAst variable, StatementAst region)
    {
        for (Ast? ancestor = variable.Parent;
             ancestor is not null && !ReferenceEquals(ancestor, region);
             ancestor = ancestor.Parent)
        {
            if (ancestor is ScriptBlockExpressionAst)
                return true;
        }
        return false;
    }

    internal static bool TryGetRuntimeRegion(
        CommandAst command,
        ScriptBlockAst body,
        ISet<string>? localFunctionNames,
        ISet<string>? allowedVariables,
        out StatementAst region)
    {
        for (Ast? current = command; current is not null && !ReferenceEquals(current, body); current = current.Parent)
        {
            if (current is StatementAst statement && ReferenceEquals(statement.Parent, body.EndBlock) && IsRuntimeRegion(statement, body, localFunctionNames, allowedVariables))
            {
                region = statement;
                return true;
            }
        }
        region = null!;
        return false;
    }

    internal static bool TryGetRuntimeRegion(CommandAst command, ScriptBlockAst body, out StatementAst region)
        => TryGetRuntimeRegion(command, body, localFunctionNames: null, allowedVariables: null, out region);

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
