using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Detects exported PowerShell script functions using the PowerShell AST parser.
/// </summary>
public sealed class PowerShellScriptFunctionExportDetector : IScriptFunctionExportDetector, IScriptAliasExportDetector, IScriptAliasExportAnalysisDetector, IScriptAliasExternalSourceDetector
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
                    .Where(static command => IsAliasLifecycleCommand(command.GetCommandName()))
                    .OrderBy(static command => command.Extent.StartOffset)
                    .ToArray();
                foreach (var command in commands)
                {
                    var shouldProcess = GetShouldProcessDisposition(command);
                    if (shouldProcess == ShouldProcessDisposition.Skip)
                        continue;
                    if (shouldProcess == ShouldProcessDisposition.Unknown)
                    {
                        isComplete = false;
                        continue;
                    }

                    if (IsAliasRemovalCommand(command.GetCommandName()))
                    {
                        if (IsRemoveAliasCommand(command.GetCommandName()))
                        {
                            var removalScope = GetAliasScopeDisposition(ast, command);
                            if (removalScope == AliasScopeDisposition.OutsideModule)
                                continue;
                            if (removalScope == AliasScopeDisposition.Unknown)
                            {
                                isComplete = false;
                                continue;
                            }
                        }

                        if (!TryGetRemovedAliasNames(
                                ast,
                                command,
                                out var removals,
                                out var isRelevantRemoval,
                                out var isDeterministicLoopRemoval))
                        {
                            if (isRelevantRemoval)
                                isComplete = false;
                            continue;
                        }

                        if (!isRelevantRemoval)
                            continue;

                        if (!isDeterministicLoopRemoval && !IsUnconditionalModuleScopeCommand(command))
                        {
                            isComplete = false;
                            continue;
                        }

                        foreach (var removal in removals)
                        {
                            if (ContainsWildcardCharacters(removal))
                            {
                                var pattern = new System.Management.Automation.WildcardPattern(
                                    removal,
                                    System.Management.Automation.WildcardOptions.IgnoreCase);
                                result.RemoveWhere(pattern.IsMatch);
                                isComplete = false;
                            }
                            else
                            {
                                result.Remove(removal);
                            }
                        }

                        continue;
                    }

                    if (IsAliasProviderCreationCommand(command.GetCommandName()))
                    {
                        if (!TryGetProviderCreatedAliasNames(
                                ast,
                                command,
                                out var providerAliases,
                                out var isRelevantCreation))
                        {
                            if (isRelevantCreation)
                                isComplete = false;
                            continue;
                        }

                        if (!isRelevantCreation)
                            continue;
                        if (!IsUnconditionalModuleScopeCommand(command))
                        {
                            isComplete = false;
                            continue;
                        }

                        foreach (var providerAlias in providerAliases)
                            result.Add(providerAlias);
                        continue;
                    }

                    var creationScope = GetAliasScopeDisposition(ast, command);
                    if (creationScope == AliasScopeDisposition.OutsideModule)
                        continue;
                    if (creationScope == AliasScopeDisposition.Unknown)
                    {
                        isComplete = false;
                        continue;
                    }

                    var hasEnclosingHashtable = TryResolveEnclosingHashtable(ast, command, out var hashtable, out var loopVariableName);
                    if (hasEnclosingHashtable && IsHashtableKeyAliasName(command, loopVariableName))
                    {
                        foreach (var pair in hashtable!.KeyValuePairs)
                        {
                            var hashtableAlias = NormalizeHashtableKey(pair.Item1.Extent.Text);
                            if (!string.IsNullOrWhiteSpace(hashtableAlias))
                                result.Add(hashtableAlias);
                            else
                                isComplete = false;
                        }

                        continue;
                    }

                    if (hasEnclosingHashtable && hashtable!.KeyValuePairs.Count == 0)
                        continue;

                    if (!hasEnclosingHashtable && !IsUnconditionalModuleScopeCommand(command))
                    {
                        isComplete = false;
                        continue;
                    }

                    if (TryGetAliasName(ast, command, out var alias))
                    {
                        result.Add(alias);
                        continue;
                    }

                    isComplete = false;
                }

                if (ast.FindAll(
                        node => node is CommandAst nestedCommand &&
                                IsPotentialNestedAliasLifecycleCommand(ast, nestedCommand),
                        searchNestedScriptBlocks: true)
                    .Cast<CommandAst>()
                    .Any(command => !commands.Contains(command) && !IsDeferredFunctionCommand(command)))
                {
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

    /// <inheritdoc />
    public bool HasModuleScopeDotSources(IEnumerable<string> scriptFiles)
    {
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

                if (ast.FindAll(
                        node => node is CommandAst { InvocationOperator: TokenKind.Dot } dotSource &&
                                !IsDeferredFunctionCommand(dotSource),
                        searchNestedScriptBlocks: true)
                    .Any())
                {
                    return true;
                }
            }
            catch
            {
                // Alias analysis already treats unreadable or invalid scripts as incomplete.
            }
        }

        return false;
    }

    private static bool IsUnconditionalModuleScopeCommand(CommandAst command)
    {
        for (Ast? current = command.Parent; current is not null; current = current.Parent)
        {
            if (current is NamedBlockAst)
                return true;
            if (current is StatementBlockAst or ScriptBlockAst or FunctionDefinitionAst)
                return false;
        }

        return false;
    }

    private static bool TryGetAliasName(ScriptBlockAst script, CommandAst command, out string alias)
    {
        alias = string.Empty;
        return TryGetAliasNameExpression(command, out var expression) &&
               TryResolveAliasExpression(script, command, expression, out alias);
    }

    private static bool TryGetAliasNameExpression(CommandAst command, out Ast? expression)
        => TryGetCommandArgument(command, new[] { "Name" }, out expression);

    private static bool TryGetCommandArgument(CommandAst command, IReadOnlyCollection<string> parameterNames, out Ast? expression)
    {
        expression = null;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is not CommandParameterAst parameter ||
                !parameterNames.Contains(parameter.ParameterName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (parameter.Argument is not null)
            {
                expression = parameter.Argument;
                return true;
            }

            if (index + 1 < command.CommandElements.Count &&
                command.CommandElements[index + 1] is not CommandParameterAst)
            {
                expression = command.CommandElements[index + 1];
                return true;
            }

            return false;
        }

        var skipParameterValue = false;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is CommandParameterAst parameter)
            {
                skipParameterValue = parameter.Argument is null && !IsSwitchParameter(parameter.ParameterName);
                continue;
            }

            if (skipParameterValue)
            {
                skipParameterValue = false;
                continue;
            }

            expression = command.CommandElements[index];
            return true;
        }

        return false;
    }

    private static bool TryGetNamedCommandArgument(CommandAst command, string parameterName, out Ast? expression)
    {
        expression = null;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is not CommandParameterAst parameter ||
                !string.Equals(parameter.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (parameter.Argument is not null)
            {
                expression = parameter.Argument;
                return true;
            }

            if (index + 1 < command.CommandElements.Count &&
                command.CommandElements[index + 1] is not CommandParameterAst)
            {
                expression = command.CommandElements[index + 1];
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool IsSwitchParameter(string parameterName)
        => string.Equals(parameterName, "Force", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(parameterName, "PassThru", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(parameterName, "Recurse", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(parameterName, "Stream", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(parameterName, "UseTransaction", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(parameterName, "WhatIf", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(parameterName, "Confirm", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(parameterName, "Verbose", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(parameterName, "Debug", StringComparison.OrdinalIgnoreCase);

    private static ShouldProcessDisposition GetShouldProcessDisposition(CommandAst command)
    {
        foreach (var parameter in command.CommandElements.OfType<CommandParameterAst>())
        {
            if (string.Equals(parameter.ParameterName, "WhatIf", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryResolveSwitchArgument(parameter, out var enabled))
                    return ShouldProcessDisposition.Unknown;
                if (enabled)
                    return ShouldProcessDisposition.Skip;
            }

            if (string.Equals(parameter.ParameterName, "Confirm", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryResolveSwitchArgument(parameter, out var enabled) || enabled)
                    return ShouldProcessDisposition.Unknown;
            }
        }

        return ShouldProcessDisposition.Execute;
    }

    private static bool TryResolveSwitchArgument(CommandParameterAst parameter, out bool enabled)
    {
        enabled = true;
        if (parameter.Argument is null)
            return true;

        if (parameter.Argument is VariableExpressionAst variable)
        {
            if (string.Equals(variable.VariablePath.UserPath, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(variable.VariablePath.UserPath, "false", StringComparison.OrdinalIgnoreCase))
            {
                enabled = false;
                return true;
            }
        }

        if (parameter.Argument is ConstantExpressionAst { Value: bool value })
        {
            enabled = value;
            return true;
        }

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

    private static bool IsAliasLifecycleCommand(string? commandName)
        => IsAliasCreationCommand(commandName) ||
           IsAliasProviderCreationCommand(commandName) ||
           IsAliasRemovalCommand(commandName);

    private static bool IsPotentialNestedAliasLifecycleCommand(ScriptBlockAst script, CommandAst command)
    {
        var commandName = command.GetCommandName();
        if (IsAliasCreationCommand(commandName) || IsRemoveAliasCommand(commandName))
            return true;
        if (!IsAliasProviderCreationCommand(commandName) && !IsAliasRemovalCommand(commandName))
            return false;

        return TryGetCommandArgument(command, new[] { "Path", "LiteralPath" }, out var expression) &&
               ExpressionResemblesAliasProvider(script, command, expression);
    }

    private static bool IsDeferredFunctionCommand(CommandAst command)
    {
        for (Ast? current = command.Parent; current is not null; current = current.Parent)
        {
            if (current is FunctionDefinitionAst)
                return true;
        }

        return false;
    }

    private static bool IsAliasCreationCommand(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var leafName = commandName!.Substring(commandName.LastIndexOf('\\') + 1);
        return string.Equals(leafName, "Set-Alias", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "New-Alias", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "sal", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "nal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAliasProviderCreationCommand(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var leafName = commandName!.Substring(commandName.LastIndexOf('\\') + 1);
        return string.Equals(leafName, "Set-Item", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "New-Item", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "si", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "ni", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAliasRemovalCommand(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var leafName = commandName!.Substring(commandName.LastIndexOf('\\') + 1);
        return string.Equals(leafName, "Remove-Alias", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "Remove-Item", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "rm", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "ri", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "del", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "erase", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "rd", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leafName, "rmdir", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRemoveAliasCommand(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var leafName = commandName!.Substring(commandName.LastIndexOf('\\') + 1);
        return string.Equals(leafName, "Remove-Alias", StringComparison.OrdinalIgnoreCase);
    }

    private static AliasScopeDisposition GetAliasScopeDisposition(ScriptBlockAst script, CommandAst command)
    {
        if (!TryGetNamedCommandArgument(command, "Scope", out var expression))
            return AliasScopeDisposition.Module;

        string scope;
        if (expression is ConstantExpressionAst constant && constant.Value is not null)
        {
            scope = Convert.ToString(constant.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
        else if (!TryResolveAliasExpression(script, command, expression, out scope))
        {
            return AliasScopeDisposition.Unknown;
        }

        if (string.Equals(scope, "Global", StringComparison.OrdinalIgnoreCase))
            return AliasScopeDisposition.OutsideModule;
        if (string.Equals(scope, "Local", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scope, "Script", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scope, "0", StringComparison.OrdinalIgnoreCase))
        {
            return AliasScopeDisposition.Module;
        }

        return AliasScopeDisposition.Unknown;
    }

    private static bool TryGetDirectStringLiteral(StatementAst statement, out string value)
    {
        value = string.Empty;
        var commandExpression = statement as CommandExpressionAst;
        if (commandExpression is null &&
            statement is PipelineAst { PipelineElements.Count: 1 } pipeline)
        {
            commandExpression = pipeline.PipelineElements[0] as CommandExpressionAst;
        }

        if (commandExpression?.Expression is not StringConstantExpressionAst literal)
        {
            return false;
        }

        value = literal.Value;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsHashtableKeyAliasName(CommandAst command, string loopVariableName)
    {
        if (!TryGetAliasNameExpression(command, out var nameExpression))
            return false;

        var keyMember = nameExpression as MemberExpressionAst;
        if (keyMember is null ||
            !string.Equals(keyMember.Member.Extent.Text, "Key", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return keyMember.Expression is VariableExpressionAst loopVariable &&
               string.Equals(loopVariable.VariablePath.UserPath, loopVariableName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveEnclosingHashtable(
        ScriptBlockAst script,
        CommandAst command,
        out HashtableAst? hashtable,
        out string loopVariableName)
    {
        hashtable = null;
        loopVariableName = string.Empty;

        Ast? ancestor = command.Parent;
        while (ancestor is not null && ancestor is not ForEachStatementAst)
            ancestor = ancestor.Parent;
        if (ancestor is not ForEachStatementAst forEach ||
            !IsUnconditionalModuleScopeStatement(forEach) ||
            !IsDirectForeachBodyCommand(command, forEach))
        {
            return false;
        }

        loopVariableName = forEach.Variable.VariablePath.UserPath;

        var match = Regex.Match(
            forEach.Condition.Extent.Text.Trim(),
            @"^\$(?<name>[A-Za-z_][A-Za-z0-9_]*)\.GetEnumerator\(\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;
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
            return false;

        return TryGetDirectHashtableLiteral(assignments[0].Right, out hashtable);
    }

    private static bool TryGetDirectHashtableLiteral(StatementAst statement, out HashtableAst? hashtable)
    {
        hashtable = null;
        if (statement is not CommandExpressionAst commandExpression)
            return false;

        if (commandExpression.Expression is HashtableAst direct)
        {
            hashtable = direct;
            return true;
        }

        if (commandExpression.Expression is ConvertExpressionAst converted &&
            string.Equals(converted.Type.TypeName.Name, "ordered", StringComparison.OrdinalIgnoreCase) &&
            converted.Child is HashtableAst ordered)
        {
            hashtable = ordered;
            return true;
        }

        return false;
    }

    private static bool TryGetProviderCreatedAliasNames(
        ScriptBlockAst script,
        CommandAst command,
        out IReadOnlyList<string> aliases,
        out bool isRelevantCreation)
    {
        aliases = Array.Empty<string>();
        isRelevantCreation = false;
        if (!TryGetCommandArgument(command, new[] { "Path", "LiteralPath" }, out var expression))
            return true;
        if (!TryResolveStringValues(script, command, expression, out var values))
        {
            isRelevantCreation = ExpressionResemblesAliasProvider(script, command, expression);
            return false;
        }

        var resolved = new List<string>();
        foreach (var value in values)
        {
            if (!TryParseAliasProviderPath(value, out var alias, out var resemblesAliasPath))
            {
                if (resemblesAliasPath)
                {
                    isRelevantCreation = true;
                    return false;
                }

                continue;
            }

            isRelevantCreation = true;
            resolved.Add(alias);
        }

        aliases = resolved;
        return true;
    }

    private static bool TryGetRemovedAliasNames(
        ScriptBlockAst script,
        CommandAst command,
        out IReadOnlyList<string> aliases,
        out bool isRelevantRemoval,
        out bool isDeterministicLoopRemoval)
    {
        aliases = Array.Empty<string>();
        isRelevantRemoval = false;
        isDeterministicLoopRemoval = false;

        var commandName = command.GetCommandName();
        var leafName = commandName?.Substring(commandName.LastIndexOf('\\') + 1);
        var isRemoveAlias = string.Equals(leafName, "Remove-Alias", StringComparison.OrdinalIgnoreCase);
        isRelevantRemoval = isRemoveAlias;
        var parameterNames = isRemoveAlias
            ? new[] { "Name" }
            : new[] { "Path", "LiteralPath" };
        if (!TryGetCommandArgument(command, parameterNames, out var expression))
            return !isRemoveAlias;

        IReadOnlyList<string>? deterministicLoopValues = null;
        if (TryResolveEnclosingHashtable(script, command, out var loopHashtable, out var loopVariableName))
        {
            isDeterministicLoopRemoval = true;
            if (loopHashtable!.KeyValuePairs.Count == 0)
            {
                isRelevantRemoval = false;
                return true;
            }

            if (isRemoveAlias && IsHashtableKeyAliasName(command, loopVariableName))
            {
                var deterministicAliases = new List<string>();
                foreach (var pair in loopHashtable.KeyValuePairs)
                {
                    var alias = NormalizeHashtableKey(pair.Item1.Extent.Text);
                    if (string.IsNullOrWhiteSpace(alias))
                        return false;
                    deterministicAliases.Add(alias);
                }

                aliases = deterministicAliases;
                return true;
            }

            if (!isRemoveAlias && IsHashtableMemberExpression(expression, loopVariableName, "Value"))
            {
                var paths = new List<string>();
                foreach (var pair in loopHashtable.KeyValuePairs)
                {
                    if (!TryGetDirectStringLiteral(pair.Item2, out var path))
                    {
                        isRelevantRemoval = true;
                        return false;
                    }
                    paths.Add(path);
                }

                deterministicLoopValues = paths;
            }
        }

        IReadOnlyList<string> values;
        if (deterministicLoopValues is not null)
        {
            values = deterministicLoopValues;
        }
        else if (!TryResolveStringValues(script, command, expression, out values))
        {
            isRelevantRemoval = isRemoveAlias || ExpressionResemblesAliasProvider(script, command, expression);
            return false;
        }

        var resolved = new List<string>();
        foreach (var value in values)
        {
            if (isRemoveAlias)
            {
                isRelevantRemoval = true;
                if (string.IsNullOrWhiteSpace(value))
                    return false;
                resolved.Add(value);
                continue;
            }

            if (!TryParseAliasProviderPath(value, out var alias, out var resemblesAliasPath))
            {
                if (resemblesAliasPath)
                {
                    isRelevantRemoval = true;
                    return false;
                }

                continue;
            }

            isRelevantRemoval = true;
            resolved.Add(alias);
        }

        aliases = resolved;
        return true;
    }

    private static bool IsHashtableMemberExpression(Ast? expression, string loopVariableName, string memberName)
        => expression is MemberExpressionAst member &&
           member.Expression is VariableExpressionAst variable &&
           string.Equals(member.Member.Extent.Text, memberName, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(variable.VariablePath.UserPath, loopVariableName, StringComparison.OrdinalIgnoreCase);

    private static bool ExpressionResemblesAliasProvider(ScriptBlockAst script, CommandAst command, Ast? expression)
    {
        if (expression is null)
            return false;

        if (expression is ArrayLiteralAst array)
            return array.Elements.Any(element => ExpressionResemblesAliasProvider(script, command, element));

        if (expression is StringConstantExpressionAst literal)
            return literal.Value.Trim().StartsWith("Alias:", StringComparison.OrdinalIgnoreCase);

        if (expression is ExpandableStringExpressionAst expandable)
            return expandable.Value.Trim().StartsWith("Alias:", StringComparison.OrdinalIgnoreCase);

        if (expression is not VariableExpressionAst variable)
        {
            return expression.Find(
                node => node is StringConstantExpressionAst nestedLiteral &&
                            nestedLiteral.Value.Trim().StartsWith("Alias:", StringComparison.OrdinalIgnoreCase) ||
                        node is ExpandableStringExpressionAst nestedExpandable &&
                            nestedExpandable.Value.Trim().StartsWith("Alias:", StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: false) is not null;
        }

        var assignments = script
            .FindAll(node => node is AssignmentStatementAst, searchNestedScriptBlocks: false)
            .Cast<AssignmentStatementAst>()
            .Where(assignment =>
                assignment.Extent.EndOffset <= command.Extent.StartOffset &&
                assignment.Parent is NamedBlockAst &&
                assignment.Left is VariableExpressionAst assignedVariable &&
                string.Equals(assignedVariable.VariablePath.UserPath, variable.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (assignments.Length != 1 || assignments[0].Right is not CommandExpressionAst commandExpression)
            return false;

        return ExpressionResemblesAliasProvider(script, command, commandExpression.Expression);
    }

    private static bool TryResolveStringValues(
        ScriptBlockAst script,
        CommandAst command,
        Ast? expression,
        out IReadOnlyList<string> values)
    {
        values = Array.Empty<string>();
        if (expression is ArrayLiteralAst array)
        {
            var resolved = new List<string>();
            foreach (var element in array.Elements)
            {
                if (!TryResolveStringValues(script, command, element, out var elementValues))
                    return false;
                resolved.AddRange(elementValues);
            }

            values = resolved;
            return true;
        }

        if (expression is ExpandableStringExpressionAst expandable && expandable.NestedExpressions.Count == 0)
        {
            values = new[] { expandable.Value };
            return true;
        }

        if (TryResolveAliasExpression(script, command, expression, out var value))
        {
            values = new[] { value };
            return true;
        }

        return false;
    }

    private static bool TryParseAliasProviderPath(string path, out string alias, out bool resemblesAliasPath)
    {
        alias = string.Empty;
        var value = (path ?? string.Empty).Trim();
        resemblesAliasPath = value.StartsWith("Alias:", StringComparison.OrdinalIgnoreCase);
        if (!resemblesAliasPath)
            return false;

        value = value.Substring("Alias:".Length).TrimStart('\\', '/');
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '\\', '/' }) >= 0)
            return false;

        alias = value;
        return true;
    }

    private static bool ContainsWildcardCharacters(string value)
        => value.IndexOfAny(new[] { '*', '?', '[' }) >= 0;

    private enum AliasScopeDisposition
    {
        Module,
        OutsideModule,
        Unknown
    }

    private enum ShouldProcessDisposition
    {
        Execute,
        Skip,
        Unknown
    }

    private static bool IsUnconditionalModuleScopeStatement(StatementAst statement)
    {
        for (Ast? current = statement.Parent; current is not null; current = current.Parent)
        {
            if (current is NamedBlockAst)
                return true;
            if (current is StatementBlockAst or ScriptBlockAst or FunctionDefinitionAst)
                return false;
        }

        return false;
    }

    private static bool IsDirectForeachBodyCommand(CommandAst command, ForEachStatementAst forEach)
    {
        return command.Parent is PipelineAst pipeline &&
               ReferenceEquals(pipeline.Parent, forEach.Body);
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
