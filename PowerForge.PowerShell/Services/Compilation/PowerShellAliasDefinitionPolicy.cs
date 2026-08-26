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
