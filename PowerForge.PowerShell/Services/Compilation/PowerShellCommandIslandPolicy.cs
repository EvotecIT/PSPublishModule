using System.Management.Automation.Language;

namespace PowerForge;

internal enum PowerShellStreamCommandKind
{
    Success,
    Verbose,
    Debug,
    Warning,
    Information,
    Host,
    Error
}

internal static class PowerShellCommandIslandPolicy
{
    internal static int FindRuntimeTailStart(
        IReadOnlyList<StatementAst> statements,
        ScriptBlockAst body,
        ISet<string>? localFunctionNames = null,
        PowerShellCommandSemanticRegistry? commandRegistry = null)
    {
        var parameters = body.ParamBlock?.Parameters
            .Select(static parameter => parameter.Name.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < statements.Count; index++)
        {
            var assignmentCommands = statements[index]
                .FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true)
                .OfType<CommandAst>()
                .ToArray();
            if (statements[index] is not AssignmentStatementAst assignment ||
                IsDiscardAssignment(assignment) ||
                assignmentCommands.Length == 0 ||
                assignmentCommands.All(command => IsRuntimeFreeCompilerIntrinsic(command, commandRegistry)))
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
            if (TryGetCapturedRuntimeAssignment(statements[index], body, localFunctionNames, available, out _))
                continue;
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
            if (commands.Length == 0 || commands.Any(static command => command.Redirections.Count != 0) ||
                commands.Any(IsVariableSessionStateCommand))
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
                        return IsNestedPipelineVariable(variable, statements[index], name) || IsLiteralAutomaticVariable(name) || available.Contains(name) || assigned.Contains(name);
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
        ISet<string>? allowedVariables = null,
        PowerShellCommandSemanticRegistry? commandRegistry = null)
    {
        if (!ReferenceEquals(statement.Parent, body.EndBlock))
            return false;
        var commands = statement.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).Cast<CommandAst>().ToArray();
        if (commands.Length == 0 || commands.Any(static command => command.Redirections.Count != 0) ||
            commands.Any(IsVariableSessionStateCommand) ||
            commands.All(command => IsRuntimeFreeCompilerIntrinsic(command, commandRegistry)))
            return false;
        if (localFunctionNames is not null && commands.Any(command =>
                command.InvocationOperator == TokenKind.Unknown &&
                command.GetCommandName() is { } name &&
                localFunctionNames.Contains(name)))
            return false;
        if (statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
            pipeline.PipelineElements[0] is CommandAst stream &&
            IsStreamCommand(stream, commandRegistry))
            return false;
        if (statement.FindAll(static node => node is ReturnStatementAst or BreakStatementAst or ContinueStatementAst or ThrowStatementAst, searchNestedScriptBlocks: true).Any() ||
            statement.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: true)
                .OfType<AssignmentStatementAst>()
                .Any(static assignment => !IsDiscardAssignment(assignment)))
            return false;

        var parameters = GetAvailableVariablesBefore(body, statement, allowedVariables);
        return statement.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
            .Cast<VariableExpressionAst>()
            .All(variable =>
            {
                var name = variable.VariablePath.UserPath;
                if (HasNestedScriptBlockAncestor(variable, statement))
                    return IsNestedPipelineVariable(variable, statement, name) || IsLiteralAutomaticVariable(name);
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

    internal static bool TryGetCapturedRuntimeRegion(
        CommandAst command,
        ScriptBlockAst body,
        ISet<string>? localFunctionNames,
        ISet<string>? allowedVariables,
        out AssignmentStatementAst assignment)
    {
        for (Ast? current = command; current is not null && !ReferenceEquals(current, body); current = current.Parent)
        {
            if (current is StatementAst statement &&
                TryGetCapturedRuntimeAssignment(statement, body, localFunctionNames, allowedVariables, out assignment))
                return true;
        }
        assignment = null!;
        return false;
    }

    internal static bool TryGetCapturedRuntimeAssignment(
        StatementAst statement,
        ScriptBlockAst body,
        ISet<string>? localFunctionNames,
        ISet<string>? allowedVariables,
        out AssignmentStatementAst assignment)
    {
        assignment = null!;
        if (!ReferenceEquals(statement.Parent, body.EndBlock) ||
            statement is not AssignmentStatementAst candidate ||
            candidate.Operator.ToString() != "Equals" ||
            candidate.Left is not ConvertExpressionAst
            {
                Child: VariableExpressionAst target
            } ||
            target.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase) ||
            PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticVariable(target.VariablePath.UserPath))
            return false;

        var commands = candidate.Right.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true)
            .OfType<CommandAst>()
            .ToArray();
        if (commands.Length == 0 || commands.Any(static command => command.Redirections.Count != 0) ||
            commands.Any(IsVariableSessionStateCommand) ||
            commands.All(command => IsRuntimeFreeCompilerIntrinsic(command)))
            return false;
        if (localFunctionNames is not null && commands.Any(command =>
                command.InvocationOperator == TokenKind.Unknown &&
                command.GetCommandName() is { } name &&
                localFunctionNames.Contains(name)))
            return false;

        var parameters = GetAvailableVariablesBefore(body, candidate, allowedVariables);
        var variablesSafe = candidate.Right.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
            .OfType<VariableExpressionAst>()
            .All(variable =>
            {
                var name = variable.VariablePath.UserPath;
                if (HasNestedScriptBlockAncestor(variable, candidate))
                    return IsNestedPipelineVariable(variable, candidate, name) || IsLiteralAutomaticVariable(name) || parameters.Contains(name);
                return parameters.Contains(name) ||
                       name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("null", StringComparison.OrdinalIgnoreCase);
            });
        if (!variablesSafe)
            return false;

        assignment = candidate;
        return true;
    }

    private static bool IsVariableSessionStateCommand(CommandAst command)
    {
        var commandName = command.GetCommandName();
        if (!string.IsNullOrWhiteSpace(commandName))
        {
            var separator = commandName.LastIndexOf('\\');
            var leafName = separator >= 0 ? commandName.Substring(separator + 1) : commandName;
            if (leafName.Equals("Get-Variable", StringComparison.OrdinalIgnoreCase) ||
                leafName.Equals("Set-Variable", StringComparison.OrdinalIgnoreCase) ||
                leafName.Equals("New-Variable", StringComparison.OrdinalIgnoreCase) ||
                leafName.Equals("Remove-Variable", StringComparison.OrdinalIgnoreCase) ||
                leafName.Equals("Clear-Variable", StringComparison.OrdinalIgnoreCase) ||
                leafName.Equals("gv", StringComparison.OrdinalIgnoreCase) ||
                leafName.Equals("sv", StringComparison.OrdinalIgnoreCase) ||
                leafName.Equals("nv", StringComparison.OrdinalIgnoreCase) ||
                leafName.Equals("rv", StringComparison.OrdinalIgnoreCase) ||
                leafName.Equals("clv", StringComparison.OrdinalIgnoreCase))
                return !HasExplicitSharedScope(command);
            if (IsProviderPathCommand(leafName) && HasNonLiteralProviderPath(command))
                return true;
        }

        return command.CommandElements
            .OfType<StringConstantExpressionAst>()
            .Any(static element =>
                element.Value.StartsWith("Variable:", StringComparison.OrdinalIgnoreCase) &&
                !element.Value.StartsWith("Variable:script:", StringComparison.OrdinalIgnoreCase) &&
                !element.Value.StartsWith("Variable:global:", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProviderPathCommand(string commandName)
        => commandName.ToUpperInvariant() is
            "GET-ITEM" or "GI" or
            "GET-CONTENT" or "GC" or "CAT" or "TYPE" or
            "GET-CHILDITEM" or "GCI" or "DIR" or "LS" or
            "TEST-PATH" or
            "SET-ITEM" or "SI" or "SET-CONTENT" or "SC" or
            "ADD-CONTENT" or "AC" or "CLEAR-ITEM" or "CLI" or
            "CLEAR-CONTENT" or "CLC" or "NEW-ITEM" or "NI" or
            "REMOVE-ITEM" or "RI" or "RM" or "DEL" or "ERASE" or "RD" or "RMDIR" or
            "RENAME-ITEM" or "RNI" or "REN" or
            "COPY-ITEM" or "CPI" or "CP" or "COPY" or
            "MOVE-ITEM" or "MI" or "MV" or "MOVE";

    private static bool HasNonLiteralProviderPath(CommandAst command)
    {
        var arguments = command.CommandElements.Skip(1).ToArray();
        if (arguments.Length == 0)
            return true;
        if (arguments.Any(static argument => argument is not CommandParameterAst and not StringConstantExpressionAst))
            return true;
        return !arguments.OfType<StringConstantExpressionAst>().Any();
    }

    private static bool HasExplicitSharedScope(CommandAst command)
    {
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is not CommandParameterAst parameter ||
                !parameter.ParameterName.Equals("Scope", StringComparison.OrdinalIgnoreCase))
                continue;
            var argument = parameter.Argument ??
                           (index + 1 < command.CommandElements.Count
                               ? command.CommandElements[index + 1] as ExpressionAst
                               : null);
            return argument is StringConstantExpressionAst scope &&
                   (scope.Value.Equals("Script", StringComparison.OrdinalIgnoreCase) ||
                    scope.Value.Equals("Global", StringComparison.OrdinalIgnoreCase));
        }
        return false;
    }

    private static HashSet<string> GetAvailableVariablesBefore(
        ScriptBlockAst body,
        StatementAst boundary,
        ISet<string>? allowedVariables)
    {
        var available = body.ParamBlock?.Parameters
            .Select(static parameter => parameter.Name.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var earlierAssignments = body.EndBlock?.Statements
            .Where(statement => statement.Extent.StartOffset < boundary.Extent.StartOffset)
            .SelectMany(static statement => statement.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: false))
            .OfType<AssignmentStatementAst>()
            .Select(static assignment => PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left)?.VariablePath.UserPath)
            .Where(static name => name is not null)
            .Cast<string>() ?? Enumerable.Empty<string>();
        foreach (var name in earlierAssignments)
        {
            if (allowedVariables is null || allowedVariables.Contains(name))
                available.Add(name);
        }
        if (allowedVariables is not null)
            available.RemoveWhere(name => !allowedVariables.Contains(name) &&
                                          body.ParamBlock?.Parameters.Any(parameter =>
                                              parameter.Name.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)) != true);
        return available;
    }

    internal static bool IsDiscardAssignment(AssignmentStatementAst assignment)
        => assignment.Operator.ToString() == "Equals" &&
           PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is { } variable &&
           variable.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase) &&
           assignment.Right.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).Any();

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

    private static bool IsLiteralAutomaticVariable(string name)
        => name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("null", StringComparison.OrdinalIgnoreCase);

    internal static bool TryGetStreamCommand(
        CommandAst command,
        out PowerShellStreamCommandKind kind,
        out ExpressionAst message,
        PowerShellCommandSemanticRegistry? registry = null)
        => TryGetStreamCommand(command, out kind, out message, out _, registry);

    internal static bool TryGetStreamCommand(
        CommandAst command,
        out PowerShellStreamCommandKind kind,
        out ExpressionAst message,
        out PowerShellCompilationCommandProviderContract? provider,
        PowerShellCommandSemanticRegistry? registry = null)
    {
        kind = default;
        message = null!;
        provider = null;
        var resolution = (registry ?? PowerShellCommandSemanticRegistry.Default).Resolve(command.GetCommandName());
        if (resolution.Status != PowerShellCommandResolutionStatus.Resolved || resolution.Contract is null ||
            !PowerShellStreamCommandSemanticBinder.TryBind(command, resolution.Contract, out kind, out message))
            return false;
        provider = resolution.Contract;
        return true;
    }

    internal static bool TryGetTargetStreamCommand(
        CommandAst command,
        PowerShellCompilationCapability capabilities,
        out PowerShellStreamCommandKind kind,
        out ExpressionAst message,
        out PowerShellCompilationCommandProviderContract? provider,
        PowerShellCommandSemanticRegistry? registry = null)
    {
        if (!TryGetStreamCommand(command, out kind, out message, out provider, registry))
            return false;

        return capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) ||
               capabilities.HasFlag(PowerShellCompilationCapability.RuntimeFreeProviderOperations) &&
               provider!.Adapter.RuntimeFree &&
               provider.Adapter.EntryPoint is not null;
    }

    private static bool IsStreamCommand(CommandAst command, PowerShellCommandSemanticRegistry? registry = null)
    {
        if (TryGetStreamCommand(command, out _, out _, registry))
            return true;
        var contract = (registry ?? PowerShellCommandSemanticRegistry.Default).Resolve(command.GetCommandName()).Contract;
        return (contract?.Family is PowerShellCompilationCommandFamily.Stream or PowerShellCompilationCommandFamily.ExternalOperation) &&
               !contract.Stream.Equals("Success", StringComparison.Ordinal);
    }

    private static bool IsRuntimeFreeCompilerIntrinsic(
        CommandAst command,
        PowerShellCommandSemanticRegistry? registry = null)
        => (registry ?? PowerShellCommandSemanticRegistry.Default).Resolve(command.GetCommandName()) is
           {
               Status: PowerShellCommandResolutionStatus.Resolved,
               Contract.Family: PowerShellCompilationCommandFamily.ClrConstruction
           } && PowerShellNewObjectSemanticBinder.IsSupportedShape(command);

}
