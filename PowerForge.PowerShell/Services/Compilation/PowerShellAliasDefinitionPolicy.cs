using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Reads literal Set-Alias and New-Alias declarations without executing authored PowerShell.
/// </summary>
internal static class PowerShellAliasDefinitionPolicy
{
    internal static bool IsAliasDefinitionCommand(CommandAst command)
    {
        var name = command.GetCommandName();
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name!.Split('\\').Last();
        return name.Equals("Set-Alias", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("sal", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("New-Alias", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("nal", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool MayWriteOutsideLocalScope(CommandAst command)
    {
        if (!IsAliasDefinitionCommand(command))
            return false;
        var elements = command.CommandElements;
        for (int i = 1; i < elements.Count; i++)
        {
            if (elements[i] is VariableExpressionAst { Splatted: true })
                return true;
            if (elements[i] is not CommandParameterAst parameter ||
                !MatchesParameter(parameter.ParameterName, "Scope"))
                continue;
            var argument = parameter.Argument ?? (i + 1 < elements.Count ? elements[i + 1] as ExpressionAst : null);
            return argument switch
            {
                StringConstantExpressionAst literal => !literal.Value.Equals("Local", StringComparison.OrdinalIgnoreCase) &&
                                                       !literal.Value.Equals("Private", StringComparison.OrdinalIgnoreCase) &&
                                                       !literal.Value.Equals("0", StringComparison.Ordinal),
                ConstantExpressionAst { Value: int scope } => scope != 0,
                _ => true
            };
        }
        return false;
    }

    internal static bool MayChooseWhetherToSetAlias(CommandAst command)
        => command.CommandElements.OfType<CommandParameterAst>().Any(parameter =>
            (MatchesParameter(parameter.ParameterName, "WhatIf") || MatchesParameter(parameter.ParameterName, "Confirm") ||
             parameter.ParameterName.Equals("wi", StringComparison.OrdinalIgnoreCase) ||
             parameter.ParameterName.Equals("cf", StringComparison.OrdinalIgnoreCase)) &&
            !IsDisabledSwitch(parameter.Argument));

    private static bool IsDisabledSwitch(ExpressionAst? argument)
        => argument is VariableExpressionAst flag && flag.VariablePath.IsUnqualified &&
           flag.VariablePath.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase);

    internal static bool MayShareAliasWithChildScopes(CommandAst command)
    {
        for (int i = 1; i < command.CommandElements.Count; i++)
        {
            if (command.CommandElements[i] is not CommandParameterAst parameter ||
                !MatchesParameter(parameter.ParameterName, "Option"))
                continue;
            var argument = parameter.Argument ?? (i + 1 < command.CommandElements.Count ? command.CommandElements[i + 1] as ExpressionAst : null);
            return argument is ArrayLiteralAst array ? array.Elements.Any(MayBeAllScope) : MayBeAllScope(argument);
        }
        return false;
    }

    private static bool MayBeAllScope(ExpressionAst? argument)
    {
        if (argument is ConstantExpressionAst { Value: int integer })
            return ((ScopedItemOptions)integer & ScopedItemOptions.AllScope) != 0;
        return argument is not StringConstantExpressionAst literal ||
               !Enum.TryParse(literal.Value, ignoreCase: true, out ScopedItemOptions options) ||
               (options & ScopedItemOptions.AllScope) != 0;
    }

    internal static bool TryGetLiteralDefinition(CommandAst command, out string aliasName, out string targetName)
    {
        aliasName = string.Empty;
        targetName = string.Empty;
        if (!IsAliasDefinitionCommand(command)) return false;

        string? namedAlias = null;
        string? namedTarget = null;
        var positional = new List<string>();
        var elements = command.CommandElements.Skip(1).ToArray();
        for (var index = 0; index < elements.Length; index++)
        {
            if (elements[index] is CommandParameterAst parameter)
            {
                var isName = MatchesParameter(parameter.ParameterName, "Name");
                var isValue = MatchesParameter(parameter.ParameterName, "Value");
                if (!isName && !isValue)
                {
                    if (ParameterConsumesArgument(parameter.ParameterName) &&
                        parameter.Argument is null && index + 1 < elements.Length &&
                        elements[index + 1] is ExpressionAst)
                        index++;
                    continue;
                }
                var argument = parameter.Argument;
                if (argument is null && index + 1 < elements.Length)
                    argument = elements[++index] as ExpressionAst;
                if (argument is not StringConstantExpressionAst literal || string.IsNullOrWhiteSpace(literal.Value))
                    return false;
                if (isName)
                    namedAlias = literal.Value;
                else
                    namedTarget = literal.Value;
                continue;
            }

            if (elements[index] is StringConstantExpressionAst positionalLiteral &&
                !string.IsNullOrWhiteSpace(positionalLiteral.Value))
            {
                positional.Add(positionalLiteral.Value);
                continue;
            }
            return false;
        }

        aliasName = namedAlias ?? positional.ElementAtOrDefault(0) ?? string.Empty;
        targetName = namedTarget ?? positional.ElementAtOrDefault(namedAlias is null ? 1 : 0) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(aliasName) && !string.IsNullOrWhiteSpace(targetName);
    }

    internal static bool TargetsAliasProvider(CommandAst command, ScriptBlockAst root)
    {
        var commandName = command.GetCommandName();
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        commandName = commandName!.Split('\\').Last();
        if (!new[]
            {
                "Set-Item", "si", "New-Item", "ni", "Copy-Item", "cpi", "cp", "copy",
                "Move-Item", "mi", "mv", "move", "Rename-Item", "rni", "ren"
            }.Contains(commandName, StringComparer.OrdinalIgnoreCase))
            return false;

        var candidates = new List<Ast>();
        var positional = new List<Ast>();
        var elements = command.CommandElements.Skip(1).ToArray();
        for (var index = 0; index < elements.Length; index++)
        {
            if (elements[index] is CommandParameterAst parameter)
            {
                if (!MatchesParameter(parameter.ParameterName, "Path") &&
                    !MatchesParameter(parameter.ParameterName, "LiteralPath") &&
                    !MatchesParameter(parameter.ParameterName, "Destination") &&
                    !MatchesParameter(parameter.ParameterName, "NewName"))
                    continue;
                var argument = parameter.Argument;
                if (argument is null && index + 1 < elements.Length && elements[index + 1] is ExpressionAst following)
                {
                    argument = following;
                    index++;
                }
                if (argument is not null)
                    candidates.Add(argument);
                continue;
            }
            positional.Add(elements[index]);
        }
        candidates.AddRange(positional.Take(2));
        return candidates.Any(candidate => ResolvesToAliasProvider(candidate, command, root, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    private static bool ResolvesToAliasProvider(
        Ast expression,
        CommandAst command,
        ScriptBlockAst root,
        HashSet<string> visited)
    {
        if (expression is CommandExpressionAst commandExpression)
            return ResolvesToAliasProvider(commandExpression.Expression, command, root, visited);
        if (expression is StringConstantExpressionAst literal)
            return literal.Value.StartsWith("Alias:", StringComparison.OrdinalIgnoreCase);
        if (expression is not VariableExpressionAst variable || !visited.Add(variable.VariablePath.UserPath))
            return false;
        var assignment = root.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: true)
            .Cast<AssignmentStatementAst>()
            .Where(candidate => candidate.Extent.StartOffset < command.Extent.StartOffset &&
                                candidate.Left is VariableExpressionAst target &&
                                target.VariablePath.UserPath.Equals(variable.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static candidate => candidate.Extent.StartOffset)
            .FirstOrDefault();
        return assignment is not null && ResolvesToAliasProvider(assignment.Right, command, root, visited);
    }

    private static bool MatchesParameter(string authoredName, string fullName)
        => fullName.StartsWith(authoredName, StringComparison.OrdinalIgnoreCase);

    private static bool ParameterConsumesArgument(string name)
        => new[]
        {
            "Description", "Option", "Scope", "ErrorAction", "WarningAction", "InformationAction",
            "ProgressAction", "ErrorVariable", "WarningVariable", "InformationVariable", "OutVariable",
            "OutBuffer", "PipelineVariable"
        }.Any(candidate => MatchesParameter(name, candidate)) ||
        name.Equals("EA", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("WA", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("IA", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("EV", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("WV", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("IV", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("OV", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("OB", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PV", StringComparison.OrdinalIgnoreCase);
}
