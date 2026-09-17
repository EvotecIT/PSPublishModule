using System;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class NestedFunctionVisibilityAnalyzer
{
    private bool DoesDeferredFunctionBodyEscape(
        FunctionDefinitionAst function,
        ScriptBlockAst declarationScope)
    {
        if (_deferredFunctionEscapeCache.TryGetValue(function, out var cached))
            return cached;

        var escaped = declarationScope.FindAll(
                ast => ast is VariableExpressionAst ||
                       ast is CommandAst,
                searchNestedScriptBlocks: true)
            .Any(ast => IsEscapingFunctionProviderReference(ast, function));
        _deferredFunctionEscapeCache[function] = escaped;
        return escaped;
    }

    private bool IsEscapingFunctionProviderReference(Ast ast, FunctionDefinitionAst function)
    {
        var normalizedFunctionName = NormalizeDeclaredFunctionName(function.Name);
        string? providerPath = ast is VariableExpressionAst variable
            ? variable.VariablePath.UserPath
            : null;
        if (ast is CommandAst command && IsFunctionLookupCommand(command, normalizedFunctionName))
        {
            var rawCommandName = command.GetCommandName();
            var commandName = NormalizeInvocationName(rawCommandName);
            return !IsPotentiallyShadowedByScriptFunction(command, rawCommandName, commandName) &&
                   command.Extent.StartOffset >= function.Extent.EndOffset &&
                   !IsDiscardedLookupResult(command);
        }

        if (!TryGetFunctionProviderName(providerPath, out var functionName) ||
            !string.Equals(
                NormalizeDeclaredFunctionName(functionName),
                normalizedFunctionName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ast.Extent.StartOffset < function.Extent.EndOffset)
            return false;
        if (ast is VariableExpressionAst && IsAssignedToNull(ast))
            return false;

        if (ast is VariableExpressionAst &&
            ast.Parent is CommandAst invocation &&
            IsInvocationTarget(ast, invocation) &&
            (invocation.InvocationOperator == TokenKind.Ampersand || invocation.InvocationOperator == TokenKind.Dot))
        {
            return false;
        }

        return true;
    }

    private static bool IsAssignedToNull(Ast ast)
    {
        for (var current = ast.Parent; current is not null; current = current.Parent)
        {
            if (current is AssignmentStatementAst assignment)
            {
                return assignment.Left is VariableExpressionAst variable &&
                       string.Equals(variable.VariablePath.UserPath, "null", StringComparison.OrdinalIgnoreCase);
            }

            if (current is FunctionDefinitionAst || current is ScriptBlockAst)
                return false;
        }

        return false;
    }

    private bool IsDiscardedLookupResult(CommandAst command)
    {
        if (command.Parent is not PipelineAst pipeline)
            return false;

        if (pipeline.Parent is AssignmentStatementAst assignment &&
            assignment.Left is VariableExpressionAst variable &&
            string.Equals(variable.VariablePath.UserPath, "null", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var commandIndex = pipeline.PipelineElements
            .Select((element, index) => new { element, index })
            .FirstOrDefault(item => ReferenceEquals(item.element, command))
            ?.index ?? -1;
        if (commandIndex < 0 ||
            commandIndex + 2 != pipeline.PipelineElements.Count ||
            pipeline.PipelineElements[commandIndex + 1] is not CommandAst consumer ||
            !string.Equals(consumer.GetCommandName(), "Out-Null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawConsumerName = consumer.GetCommandName();
        return !IsPotentiallyShadowedByScriptFunction(
            consumer,
            rawConsumerName,
            NormalizeInvocationName(rawConsumerName));
    }

    private static bool IsFunctionLookupCommand(CommandAst command, string functionName)
    {
        var commandName = NormalizeInvocationName(command.GetCommandName());
        if (string.Equals(commandName, "Get-Command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "gcm", StringComparison.OrdinalIgnoreCase))
        {
            return BoundLookupCanMatchFunction(command, functionName, providerPath: false, "Name");
        }

        if (!string.Equals(commandName, "Get-Item", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "gi", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "Get-Content", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "gc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return BoundLookupCanMatchFunction(command, functionName, providerPath: true, "Path", "LiteralPath");
    }

    private static bool BoundLookupCanMatchFunction(
        CommandAst command,
        string functionName,
        bool providerPath,
        params string[] parameterNames)
    {
        try
        {
            var binding = StaticParameterBinder.BindCommand(command);
            foreach (var parameterName in parameterNames)
            {
                if (binding.BoundParameters.TryGetValue(parameterName, out var result))
                    return LookupValueCanMatchFunction(result.Value, functionName, providerPath);
            }

            if (binding.BindingExceptions.Count == 0)
                return false;
        }
        catch
        {
            // Fall through to conservative AST matching when command metadata is unavailable.
        }

        var arguments = command.CommandElements.Skip(1).Where(element => element is not CommandParameterAst).ToArray();
        return arguments.Any(argument => LookupValueCanMatchFunction(argument, functionName, providerPath));
    }

    private static bool LookupValueCanMatchFunction(object? value, string functionName, bool providerPath)
    {
        if (value is StringConstantExpressionAst literal)
            return LookupTextCanMatchFunction(literal.Value, functionName, providerPath);
        if (value is ExpandableStringExpressionAst expandable && expandable.NestedExpressions.Count == 0)
            return LookupTextCanMatchFunction(expandable.Value, functionName, providerPath);
        if (value is ConstantExpressionAst constant && constant.Value is string text)
            return LookupTextCanMatchFunction(text, functionName, providerPath);
        if (value is ArrayLiteralAst array)
            return array.Elements.Any(element => LookupValueCanMatchFunction(element, functionName, providerPath));

        return value is not null;
    }

    private static bool LookupTextCanMatchFunction(string text, string functionName, bool providerPath)
    {
        if (!providerPath)
            return LookupPatternMatchesName(text, functionName);

        return TryGetFunctionProviderName(text, out var providerFunction) &&
               LookupPatternMatchesName(providerFunction, functionName);
    }

    private static bool LookupPatternMatchesName(string pattern, string functionName)
    {
        if (string.Equals(pattern, functionName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!System.Management.Automation.WildcardPattern.ContainsWildcardCharacters(pattern))
            return false;

        return new System.Management.Automation.WildcardPattern(
            pattern,
            System.Management.Automation.WildcardOptions.IgnoreCase).IsMatch(functionName);
    }

    private static bool TryGetFunctionProviderName(string? providerPath, out string functionName)
    {
        functionName = string.Empty;
        if (string.IsNullOrWhiteSpace(providerPath))
            return false;

        const string prefix = "function:";
        var value = providerPath!.Trim();
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        functionName = value.Substring(prefix.Length).TrimStart('\\');
        return functionName.Length > 0;
    }
}
