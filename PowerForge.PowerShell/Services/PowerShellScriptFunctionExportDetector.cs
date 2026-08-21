using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Detects exported PowerShell script functions using the PowerShell AST parser.
/// </summary>
public sealed class PowerShellScriptFunctionExportDetector : IScriptFunctionExportDetector, IScriptAliasExportDetector, IScriptAliasExportAnalysisDetector
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
        => AnalyzeScriptAliases(scriptFiles).Aliases;

    /// <inheritdoc />
    public ScriptAliasExportAnalysis AnalyzeScriptAliases(IEnumerable<string> scriptFiles)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isComplete = true;
        foreach (var file in scriptFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(file))
                continue;
            if (!File.Exists(file))
            {
                isComplete = false;
                continue;
            }

            try
            {
                Token[] tokens;
                ParseError[] errors;
                var ast = Parser.ParseFile(file, out tokens, out errors);
                if (errors is { Length: > 0 })
                {
                    isComplete = false;
                    continue;
                }

                var commands = ast.FindAll(node => node is CommandAst, searchNestedScriptBlocks: false)
                    .Cast<CommandAst>()
                    .Where(static command => IsAliasCommand(command.GetCommandName()))
                    .ToArray();
                foreach (var command in commands)
                {
                    if (TryGetAliasName(ast, command, out var alias))
                    {
                        result.Add(alias);
                        continue;
                    }

                    var resolvedHashtable = false;
                    foreach (var hashtable in ResolveAliasHashtables(ast, command))
                    {
                        resolvedHashtable = true;
                        foreach (var pair in hashtable.KeyValuePairs)
                        {
                            var hashtableAlias = NormalizeHashtableKey(pair.Item1.Extent.Text);
                            if (!string.IsNullOrWhiteSpace(hashtableAlias))
                                result.Add(hashtableAlias);
                            else
                                isComplete = false;
                        }
                    }

                    if (!resolvedHashtable)
                        isComplete = false;
                }
            }
            catch
            {
                isComplete = false;
            }
        }

        return new ScriptAliasExportAnalysis(result, isComplete);
    }

    private static bool TryGetAliasName(ScriptBlockAst script, CommandAst command, out string alias)
    {
        alias = string.Empty;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is CommandParameterAst parameter &&
                string.Equals(parameter.ParameterName, "Name", StringComparison.OrdinalIgnoreCase))
            {
                if (TryResolveAliasExpression(script, command, parameter.Argument, out alias))
                    return true;

                if (index + 1 < command.CommandElements.Count &&
                    TryResolveAliasExpression(script, command, command.CommandElements[index + 1], out alias))
                    return true;
            }
        }

        if (command.CommandElements.Count > 1 &&
            TryResolveAliasExpression(script, command, command.CommandElements[1], out alias))
            return true;

        return false;
    }

    private static bool TryResolveAliasExpression(ScriptBlockAst script, CommandAst command, Ast? expression, out string alias)
    {
        alias = string.Empty;
        if (expression is StringConstantExpressionAst literal)
        {
            alias = literal.Value;
            return !string.IsNullOrWhiteSpace(alias);
        }

        if (expression is not VariableExpressionAst variable)
            return false;

        var assignments = script
            .FindAll(node => node is AssignmentStatementAst, searchNestedScriptBlocks: false)
            .Cast<AssignmentStatementAst>()
            .Where(assignment =>
                assignment.Extent.EndOffset <= command.Extent.StartOffset &&
                assignment.Parent is NamedBlockAst &&
                assignment.Left is VariableExpressionAst assignedVariable &&
                string.Equals(assignedVariable.VariablePath.UserPath, variable.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (assignments.Length != 1)
            return false;

        if (!TryGetDirectStringLiteral(assignments[0].Right, out var assignedLiteral))
            return false;

        alias = assignedLiteral;
        return !string.IsNullOrWhiteSpace(alias);
    }

    private static bool IsAliasCommand(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var leafName = commandName!.Substring(commandName.LastIndexOf('\\') + 1);
        return string.Equals(leafName, "Set-Alias", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "New-Alias", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "sal", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "nal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetDirectStringLiteral(StatementAst statement, out string value)
    {
        value = string.Empty;
        if (statement is not CommandExpressionAst commandExpression ||
            commandExpression.Expression is not StringConstantExpressionAst literal)
        {
            return false;
        }

        value = literal.Value;
        return !string.IsNullOrWhiteSpace(value);
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

        var assignments = script
            .FindAll(node => node is AssignmentStatementAst, searchNestedScriptBlocks: false)
            .Cast<AssignmentStatementAst>()
            .Where(assignment =>
                assignment.Extent.EndOffset <= forEach.Extent.StartOffset &&
                assignment.Parent is NamedBlockAst &&
                assignment.Left is VariableExpressionAst variable &&
                string.Equals(variable.VariablePath.UserPath, tableVariable, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (assignments.Length != 1)
            yield break;

        var hashtable = assignments[0].Right.Find(node => node is HashtableAst, searchNestedScriptBlocks: false) as HashtableAst;
        if (hashtable is not null)
            yield return hashtable;
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
