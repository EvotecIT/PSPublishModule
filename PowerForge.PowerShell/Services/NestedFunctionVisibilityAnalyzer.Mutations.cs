using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class NestedFunctionVisibilityAnalyzer
{
    private IReadOnlyDictionary<string, CommandAst[]> FindAliasRemovalCommands(ScriptBlockAst root)
    {
        return root.FindAll(ast => ast is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .SelectMany(command => TryGetRemovedAliasNames(command)
                .Select(name => new { Command = command, Name = name }))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Command).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> TryGetRemovedAliasNames(CommandAst command)
    {
        var rawCommandName = command.GetCommandName();
        var commandName = NormalizeInvocationName(rawCommandName);
        var aliasRemoval = string.Equals(commandName, "Remove-Alias", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(commandName, "ral", StringComparison.OrdinalIgnoreCase);
        var providerRemoval = IsProviderRemovalCommand(commandName);
        if (!aliasRemoval && !providerRemoval ||
            IsPotentiallyShadowedByScriptFunction(command, rawCommandName, commandName) ||
            TryGetBoundSwitchValue(command, "WhatIf") == true)
        {
            yield break;
        }

        if (!TryGetBoundRemovalValue(
                command,
                aliasRemoval ? new[] { "Name" } : new[] { "Path", "LiteralPath" },
                out var value))
        {
            yield break;
        }

        foreach (var text in EnumerateLiteralTexts(value))
        {
            if (aliasRemoval)
            {
                yield return text;
            }
            else if (TryGetAliasProviderName(text, out var aliasName))
            {
                yield return aliasName;
            }
        }
    }

    private IReadOnlyDictionary<string, CommandAst[]> FindFunctionRemovalCommands(ScriptBlockAst root)
    {
        return root.FindAll(ast => ast is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .SelectMany(command => TryGetRemovedFunctionNames(command)
                .Select(name => new { Command = command, Name = name }))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Command).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> TryGetRemovedFunctionNames(CommandAst command)
    {
        if (!TryGetFunctionRemovalValue(command, out var value, out var literalPath))
            yield break;

        foreach (var text in EnumerateLiteralTexts(value))
        {
            if (TryGetFunctionProviderName(text, out var functionName) &&
                (literalPath || !System.Management.Automation.WildcardPattern.ContainsWildcardCharacters(functionName)))
            {
                yield return NormalizeDeclaredFunctionName(functionName);
            }
        }
    }

    private IReadOnlyDictionary<string, CommandAst[]> FindWildcardFunctionRemovalCommands(ScriptBlockAst root)
    {
        return root.FindAll(ast => ast is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .SelectMany(command => TryGetWildcardRemovedFunctionNames(command)
                .Select(pattern => new { Command = command, Pattern = pattern }))
            .GroupBy(item => item.Pattern, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Command).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> TryGetWildcardRemovedFunctionNames(CommandAst command)
    {
        if (!TryGetFunctionRemovalValue(command, out var value, out var literalPath) || literalPath)
            yield break;

        foreach (var text in EnumerateLiteralTexts(value))
        {
            if (TryGetFunctionProviderName(text, out var functionName) &&
                System.Management.Automation.WildcardPattern.ContainsWildcardCharacters(functionName))
            {
                yield return NormalizeDeclaredFunctionName(functionName);
            }
        }
    }

    private CommandAst[] FindDynamicFunctionRemovalCommands(ScriptBlockAst root)
    {
        return root.FindAll(ast => ast is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .Where(command =>
                TryGetFunctionRemovalValue(command, out var value, out _) &&
                HasDynamicValue(value) &&
                DynamicValueCanTargetFunctionProvider(value))
            .ToArray();
    }

    private bool TryGetFunctionRemovalValue(
        CommandAst command,
        out object? value,
        out bool literalPath)
    {
        value = null;
        literalPath = false;
        var rawCommandName = command.GetCommandName();
        var commandName = NormalizeInvocationName(rawCommandName);
        if (!IsProviderRemovalCommand(commandName) ||
            IsPotentiallyShadowedByScriptFunction(command, rawCommandName, commandName) ||
            TryGetBoundSwitchValue(command, "WhatIf") == true ||
            !TryGetBoundRemovalValue(
                command,
                new[] { "Path", "LiteralPath" },
                out value,
                out var boundParameterName))
        {
            return false;
        }

        literalPath = string.Equals(boundParameterName, "LiteralPath", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static bool IsProviderRemovalCommand(string commandName)
    {
        return string.Equals(commandName, "Remove-Item", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(commandName, "Clear-Item", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(commandName, "ri", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(commandName, "rm", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(commandName, "del", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(commandName, "erase", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(commandName, "cli", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetBoundRemovalValue(
        CommandAst command,
        IReadOnlyList<string> parameterNames,
        out object? value)
        => TryGetBoundRemovalValue(command, parameterNames, out value, out _);

    private static bool TryGetBoundRemovalValue(
        CommandAst command,
        IReadOnlyList<string> parameterNames,
        out object? value,
        out string? boundParameterName)
    {
        value = null;
        boundParameterName = null;
        try
        {
            var binding = StaticParameterBinder.BindCommand(command);
            foreach (var parameterName in parameterNames)
            {
                if (binding.BoundParameters.TryGetValue(parameterName, out var result))
                {
                    value = result.Value;
                    boundParameterName = parameterName;
                    return true;
                }
            }
        }
        catch
        {
            // Fall back to the first positional expression when command metadata is unavailable.
        }

        value = command.CommandElements.Skip(1).FirstOrDefault(element => element is not CommandParameterAst);
        return value is not null;
    }

    private static bool DynamicValueCanTargetFunctionProvider(object? value)
    {
        if (value is ExpandableStringExpressionAst expandable && expandable.NestedExpressions.Count > 0)
        {
            return !TryGetStaticProviderPrefix(expandable.Extent.Text, out var provider) ||
                   string.Equals(provider, "function", StringComparison.OrdinalIgnoreCase);
        }

        if (value is ArrayLiteralAst array)
            return array.Elements.Any(DynamicValueCanTargetFunctionProvider);
        if (value is IEnumerable enumerable && value is not string)
            return enumerable.Cast<object?>().Any(DynamicValueCanTargetFunctionProvider);

        return HasDynamicValue(value);
    }

    private static bool TryGetStaticProviderPrefix(string text, out string provider)
    {
        provider = string.Empty;
        var candidate = text.Trim().Trim('"', '\'');
        var separator = candidate.IndexOf(':');
        if (separator <= 0)
            return false;

        var prefix = candidate.Substring(0, separator);
        if (prefix.Any(character =>
                !char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.'))
        {
            return false;
        }

        provider = prefix;
        return true;
    }

    private static IEnumerable<string> EnumerateLiteralTexts(object? value)
    {
        switch (value)
        {
            case string text:
                yield return text;
                yield break;
            case StringConstantExpressionAst literal:
                yield return literal.Value;
                yield break;
            case ExpandableStringExpressionAst expandable when expandable.NestedExpressions.Count == 0:
                yield return expandable.Value;
                yield break;
            case ConstantExpressionAst constant when constant.Value is string text:
                yield return text;
                yield break;
            case ArrayLiteralAst array:
                foreach (var element in array.Elements)
                foreach (var item in EnumerateLiteralTexts(element))
                    yield return item;
                yield break;
            case IEnumerable enumerable when value is not string:
                foreach (var element in enumerable)
                foreach (var item in EnumerateLiteralTexts(element))
                    yield return item;
                yield break;
        }
    }

    private static bool HasDynamicValue(object? value)
    {
        if (value is null)
            return false;
        if (value is string)
            return false;
        if (value is StringConstantExpressionAst)
            return false;
        if (value is ExpandableStringExpressionAst expandable)
            return expandable.NestedExpressions.Count > 0;
        if (value is ConstantExpressionAst constant)
            return constant.Value is not string;
        if (value is ArrayLiteralAst array)
            return array.Elements.Any(HasDynamicValue);
        if (value is IEnumerable enumerable && value is not string)
            return enumerable.Cast<object?>().Any(HasDynamicValue);

        return true;
    }

    private static bool IsPrivateAliasDeclaration(CommandAst command)
    {
        try
        {
            var binding = StaticParameterBinder.BindCommand(command);
            if (!binding.BoundParameters.TryGetValue("Option", out var result))
                return false;

            return EnumerateLiteralTexts(result.Value)
                .SelectMany(text => text.Split(','))
                .Any(option => string.Equals(option.Trim(), "Private", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
