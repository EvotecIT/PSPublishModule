using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Detects exported PowerShell script functions using the PowerShell AST parser.
/// </summary>
public sealed class PowerShellScriptFunctionExportDetector : IScriptFunctionExportDetector, IScriptAliasExportDetector
{
    /// <inheritdoc />
    public IReadOnlyList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in scriptFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                continue;

            try
            {
                Token[] tokens;
                ParseError[] errors;
                var ast = Parser.ParseFile(file, out tokens, out errors);
                if (errors is { Length: > 0 })
                    continue;

                var functions = ast.FindAll(node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                    .Cast<FunctionDefinitionAst>();
                foreach (var function in functions)
                {
                    if (!string.IsNullOrWhiteSpace(function.Name))
                        result.Add(function.Name);
                }
            }
            catch
            {
                // best effort
            }
        }

        return result.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DetectScriptAliases(IEnumerable<string> scriptFiles)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in scriptFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                continue;

            try
            {
                Token[] tokens;
                ParseError[] errors;
                var ast = Parser.ParseFile(file, out tokens, out errors);
                if (errors is { Length: > 0 })
                    continue;

                var commands = ast.FindAll(node => node is CommandAst, searchNestedScriptBlocks: false)
                    .Cast<CommandAst>()
                    .Where(static command =>
                        string.Equals(command.GetCommandName(), "Set-Alias", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(command.GetCommandName(), "New-Alias", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                foreach (var command in commands)
                {
                    if (TryGetLiteralAliasName(command, out var alias))
                        result.Add(alias);
                }

                foreach (var command in commands)
                {
                    foreach (var hashtable in ResolveAliasHashtables(ast, command))
                    {
                        foreach (var pair in hashtable.KeyValuePairs)
                        {
                            var alias = NormalizeHashtableKey(pair.Item1.Extent.Text);
                            if (!string.IsNullOrWhiteSpace(alias))
                                result.Add(alias);
                        }
                    }
                }
            }
            catch
            {
                // best effort
            }
        }

        return result.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryGetLiteralAliasName(CommandAst command, out string alias)
    {
        alias = string.Empty;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is CommandParameterAst parameter &&
                string.Equals(parameter.ParameterName, "Name", StringComparison.OrdinalIgnoreCase))
            {
                if (parameter.Argument is StringConstantExpressionAst inline)
                {
                    alias = inline.Value;
                    return !string.IsNullOrWhiteSpace(alias);
                }

                if (index + 1 < command.CommandElements.Count &&
                    command.CommandElements[index + 1] is StringConstantExpressionAst following)
                {
                    alias = following.Value;
                    return !string.IsNullOrWhiteSpace(alias);
                }
            }
        }

        if (command.CommandElements.Count > 1 && command.CommandElements[1] is StringConstantExpressionAst positional)
        {
            alias = positional.Value;
            return !string.IsNullOrWhiteSpace(alias);
        }

        return false;
    }

    private static IEnumerable<HashtableAst> ResolveAliasHashtables(ScriptBlockAst script, CommandAst command)
    {
        var keyMember = command.CommandElements
            .OfType<MemberExpressionAst>()
            .FirstOrDefault(static member =>
                string.Equals(member.Member.Extent.Text, "Key", StringComparison.OrdinalIgnoreCase) &&
                member.Expression is VariableExpressionAst);
        if (keyMember?.Expression is not VariableExpressionAst loopVariable)
            yield break;

        Ast? ancestor = command.Parent;
        while (ancestor is not null && ancestor is not ForEachStatementAst)
            ancestor = ancestor.Parent;
        if (ancestor is not ForEachStatementAst forEach ||
            !string.Equals(forEach.Variable.VariablePath.UserPath, loopVariable.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var match = Regex.Match(
            forEach.Condition.Extent.Text.Trim(),
            @"^\$(?<name>[A-Za-z_][A-Za-z0-9_]*)\.GetEnumerator\(\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            yield break;
        var tableVariable = match.Groups["name"].Value;

        foreach (var assignment in script.FindAll(node => node is AssignmentStatementAst, searchNestedScriptBlocks: false).Cast<AssignmentStatementAst>())
        {
            if (assignment.Left is not VariableExpressionAst variable ||
                !string.Equals(variable.VariablePath.UserPath, tableVariable, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hashtable = assignment.Right.Find(node => node is HashtableAst, searchNestedScriptBlocks: false) as HashtableAst;
            if (hashtable is not null)
                yield return hashtable;
        }
    }

    private static string NormalizeHashtableKey(string text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length >= 2 &&
            ((value[0] == '\'' && value[value.Length - 1] == '\'') ||
             (value[0] == '"' && value[value.Length - 1] == '"')))
        {
            value = value.Substring(1, value.Length - 2);
        }

        return value.All(static character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            ? value
            : string.Empty;
    }
}
