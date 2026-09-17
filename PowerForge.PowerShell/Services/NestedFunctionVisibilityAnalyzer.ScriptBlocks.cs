using System;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class NestedFunctionVisibilityAnalyzer
{
    private bool IsEscapingOrIsolatedScriptBlock(ScriptBlockExpressionAst scriptBlockExpression)
    {
        if (scriptBlockExpression.Parent is not CommandAst invocation)
            return true;

        if (invocation.InvocationOperator == TokenKind.Ampersand ||
            invocation.InvocationOperator == TokenKind.Dot)
        {
            if (IsInvocationTarget(scriptBlockExpression, invocation))
            {
                return false;
            }
        }

        var rawInvocationName = invocation.GetCommandName();
        var invocationName = NormalizeInvocationName(rawInvocationName);
        if (invocationName.Length == 0)
            return true;

        if (!HasTrustedModuleQualification(rawInvocationName, invocationName))
            return true;

        if (IsPotentiallyShadowedByScriptFunction(invocation, rawInvocationName, invocationName))
            return true;

        if (string.Equals(invocationName, "Start-Job", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Start-ThreadJob", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Start-RSJob", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-ObjectEvent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-EngineEvent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-CimIndicationEvent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-WmiEvent", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(invocationName, "Invoke-Command", StringComparison.OrdinalIgnoreCase))
        {
            if (HasUnresolvedSplat(invocation))
                return true;

            var remoteBinding = TryHasAnyBoundParameter(
                invocation,
                "ComputerName",
                "ConnectionUri",
                "Session",
                "HostName",
                "SSHConnection",
                "VMId",
                "VMName",
                "ContainerId");
            var isRemote = remoteBinding ??
                           (HasAnyParameter(
                                invocation,
                                "ComputerName",
                                "ConnectionUri",
                                "Session",
                                "HostName",
                                "SSHConnection",
                                "VMId",
                                "VMName",
                                "ContainerId") ||
                            HasPositionalRemoteTarget(invocation));
            return isRemote ||
                   !IsScriptBlockBoundToAnyParameter(invocation, scriptBlockExpression, "ScriptBlock");
        }

        if (string.Equals(invocationName, "ForEach-Object", StringComparison.OrdinalIgnoreCase))
        {
            if (HasUnresolvedSplat(invocation))
                return true;

            var parallelBinding = TryHasAnyBoundParameter(invocation, "Parallel");
            var isParallel = parallelBinding ?? HasAnyParameter(invocation, "Parallel");
            return isParallel ||
                   !IsScriptBlockBoundToAnyParameter(
                       invocation,
                       scriptBlockExpression,
                       "Begin",
                       "Process",
                       "End",
                       "RemainingScripts");
        }

        return !IsKnownSynchronousScriptBlockConsumer(
            invocationName,
            invocation,
            scriptBlockExpression);
    }

    private bool IsPotentiallyShadowedByScriptFunction(
        CommandAst invocation,
        string? rawInvocationName,
        string normalizedInvocationName)
    {
        if (!string.IsNullOrWhiteSpace(rawInvocationName) && rawInvocationName!.IndexOf('\\') >= 0)
            return false;

        var authoredAliasName = GetExactInvocationName(rawInvocationName, normalizedInvocationName);
        if (_aliasDeclarationsByName.TryGetValue(authoredAliasName, out var aliases) &&
            aliases.Any(alias => AliasCanShadowInvocation(alias, invocation)))
        {
            return true;
        }
        if (_dynamicAliasDeclarations.Any(alias => AliasCanShadowInvocation(alias, invocation)))
            return true;

        if (!_functionDeclarationsByName.ContainsKey(normalizedInvocationName))
            return false;

        if (!_shadowResolutionInProgress.Add(invocation))
            return true;

        try
        {
            if (IsDeclaredInVisibleScope(invocation, normalizedInvocationName))
                return true;

            foreach (var declaration in _functionDeclarationsByName[normalizedInvocationName])
            {
                if (declaration.Extent.EndOffset > invocation.Extent.StartOffset ||
                    !TryGetPotentialDeclarationContext(
                        declaration,
                        out var declarationScope,
                        out var declarationBlock))
                {
                    continue;
                }

                if (IsDeclarationBlockCompatibleWithCommand(
                        declaration,
                        declarationBlock,
                        invocation,
                        declarationScope))
                    return true;
            }

            return false;
        }
        finally
        {
            _shadowResolutionInProgress.Remove(invocation);
        }
    }

    private static bool IsKnownSynchronousScriptBlockConsumer(
        string invocationName,
        CommandAst invocation,
        ScriptBlockExpressionAst scriptBlockExpression)
    {
        if (string.Equals(invocationName, "Where-Object", StringComparison.OrdinalIgnoreCase))
        {
            return IsScriptBlockBoundToAnyParameter(
                invocation,
                scriptBlockExpression,
                "FilterScript");
        }

        if (string.Equals(invocationName, "Sort-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Group-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Measure-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Select-Object", StringComparison.OrdinalIgnoreCase))
        {
            return IsScriptBlockBoundToAnyParameter(
                invocation,
                scriptBlockExpression,
                "Property");
        }

        if (string.Equals(invocationName, "Measure-Command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Trace-Command", StringComparison.OrdinalIgnoreCase))
        {
            return IsScriptBlockBoundToAnyParameter(
                invocation,
                scriptBlockExpression,
                "Expression");
        }

        return false;
    }

    private static bool IsScriptBlockBoundToAnyParameter(
        CommandAst invocation,
        ScriptBlockExpressionAst scriptBlockExpression,
        params string[] parameterNames)
    {
        try
        {
            var binding = StaticParameterBinder.BindCommand(invocation);
            foreach (var parameterName in parameterNames)
            {
                if (binding.BoundParameters.TryGetValue(parameterName, out var result) &&
                    ContainsAst(result.Value, scriptBlockExpression))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Unresolved binding is treated as escaping rather than assuming same-runspace execution.
        }

        return false;
    }

    private static bool ContainsAst(object? value, Ast target)
    {
        if (ReferenceEquals(value, target))
            return true;
        if (value is not Ast valueAst)
            return false;

        return valueAst.FindAll(ast => ReferenceEquals(ast, target), searchNestedScriptBlocks: true).Any();
    }

    private bool IsScopePromotingInvocation(
        ScriptBlockExpressionAst expression,
        CommandAst invocation)
    {
        if (invocation.InvocationOperator == TokenKind.Dot && IsInvocationTarget(expression, invocation))
            return true;

        var rawName = invocation.GetCommandName();
        var invocationName = NormalizeInvocationName(rawName);
        if (!HasTrustedModuleQualification(rawName, invocationName) ||
            IsPotentiallyShadowedByScriptFunction(invocation, rawName, invocationName) ||
            HasUnresolvedSplat(invocation))
        {
            return false;
        }

        if (string.Equals(invocationName, "ForEach-Object", StringComparison.OrdinalIgnoreCase))
        {
            return TryHasAnyBoundParameter(invocation, "Parallel") == false &&
                   IsScriptBlockBoundToAnyParameter(invocation, expression, "Begin", "End");
        }

        if (!string.Equals(invocationName, "Invoke-Command", StringComparison.OrdinalIgnoreCase) ||
            !IsScriptBlockBoundToAnyParameter(invocation, expression, "ScriptBlock"))
        {
            return false;
        }

        var noNewScope = TryGetBoundSwitchValue(invocation, "NoNewScope");
        return noNewScope == true;
    }

    private static IReadOnlyDictionary<string, CommandAst[]> FindAuthoredAliasDeclarations(ScriptBlockAst root)
    {
        return root.FindAll(ast => ast is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .Select(command => new
            {
                Command = command,
                AliasName = TryGetAuthoredAliasName(command)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.AliasName))
            .GroupBy(
                item => item.AliasName!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Command).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static CommandAst[] FindDynamicAliasDeclarations(ScriptBlockAst root)
    {
        return root.FindAll(ast => ast is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .Where(IsPotentialDynamicAliasDeclaration)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, CommandAst[]> FindFunctionRemovalCommands(ScriptBlockAst root)
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

    private static IEnumerable<string> TryGetRemovedFunctionNames(CommandAst command)
    {
        var commandName = NormalizeInvocationName(command.GetCommandName());
        if (!string.Equals(commandName, "Remove-Item", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "Clear-Item", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "ri", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "rm", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "del", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "erase", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "cli", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        if (TryGetBoundSwitchValue(command, "WhatIf") == true)
            yield break;

        foreach (var argument in command.CommandElements.Skip(1).OfType<StringConstantExpressionAst>())
        {
            if (TryGetFunctionProviderName(argument.Value, out var functionName))
                yield return NormalizeDeclaredFunctionName(functionName);
        }
    }

    private static string? TryGetAuthoredAliasName(CommandAst command)
    {
        var commandName = NormalizeInvocationName(command.GetCommandName());
        var aliasCommand = string.Equals(commandName, "Set-Alias", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(commandName, "New-Alias", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(commandName, "sal", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(commandName, "nal", StringComparison.OrdinalIgnoreCase);
        var providerCommand = string.Equals(commandName, "Set-Item", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(commandName, "New-Item", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(commandName, "si", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(commandName, "ni", StringComparison.OrdinalIgnoreCase);
        if (!aliasCommand && !providerCommand)
            return null;

        try
        {
            var binding = StaticParameterBinder.BindCommand(command);
            if (aliasCommand && binding.BoundParameters.TryGetValue("Name", out var result))
            {
                return TryGetLiteralText(result.Value);
            }

            if (providerCommand &&
                (binding.BoundParameters.TryGetValue("Path", out result) ||
                 binding.BoundParameters.TryGetValue("LiteralPath", out result)) &&
                TryGetAliasProviderName(TryGetLiteralText(result.Value), out var aliasName))
            {
                return aliasName;
            }
        }
        catch
        {
            // Dynamic alias names cannot safely establish a concrete shadow.
        }

        return null;
    }

    private static bool IsPotentialDynamicAliasDeclaration(CommandAst command)
    {
        var commandName = NormalizeInvocationName(command.GetCommandName());
        var aliasCommand = string.Equals(commandName, "Set-Alias", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(commandName, "New-Alias", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(commandName, "sal", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(commandName, "nal", StringComparison.OrdinalIgnoreCase);
        if (aliasCommand)
            return string.IsNullOrWhiteSpace(TryGetAuthoredAliasName(command));

        var providerCommand = string.Equals(commandName, "Set-Item", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(commandName, "New-Item", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(commandName, "si", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(commandName, "ni", StringComparison.OrdinalIgnoreCase);
        if (!providerCommand || !string.IsNullOrWhiteSpace(TryGetAuthoredAliasName(command)))
            return false;

        try
        {
            var binding = StaticParameterBinder.BindCommand(command);
            if (binding.BoundParameters.TryGetValue("Path", out var result) ||
                binding.BoundParameters.TryGetValue("LiteralPath", out result))
            {
                var literalPath = TryGetLiteralText(result.Value);
                return string.IsNullOrWhiteSpace(literalPath);
            }
        }
        catch
        {
            return true;
        }

        return true;
    }

    private static string GetExactInvocationName(string? rawInvocationName, string normalizedInvocationName)
    {
        if (string.IsNullOrWhiteSpace(rawInvocationName))
            return normalizedInvocationName;

        var value = rawInvocationName!.Trim();
        var separator = value.LastIndexOf('\\');
        return separator >= 0 && separator + 1 < value.Length
            ? value.Substring(separator + 1)
            : value;
    }

    private static string? TryGetLiteralText(object? value)
    {
        return value switch
        {
            StringConstantExpressionAst literal => literal.Value,
            ExpandableStringExpressionAst expandable when expandable.NestedExpressions.Count == 0 => expandable.Value,
            _ => null
        };
    }

    private static bool TryGetAliasProviderName(string? providerPath, out string aliasName)
    {
        aliasName = string.Empty;
        if (string.IsNullOrWhiteSpace(providerPath) ||
            !providerPath!.StartsWith("alias:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        aliasName = providerPath.Substring("alias:".Length).TrimStart('\\');
        return aliasName.Length > 0;
    }

    private static string? TryGetAuthoredAliasTarget(CommandAst command)
    {
        try
        {
            var binding = StaticParameterBinder.BindCommand(command);
            if (binding.BoundParameters.TryGetValue("Value", out var result))
                return TryGetLiteralText(result.Value);
        }
        catch
        {
            // Dynamic alias targets cannot safely establish a concrete call edge.
        }

        return null;
    }

    private static bool AliasCanShadowInvocation(
        CommandAst aliasDeclaration,
        CommandAst invocation,
        int? executionOffset = null)
    {
        if (aliasDeclaration.Extent.EndOffset > (executionOffset ?? invocation.Extent.StartOffset))
            return false;

        var aliasScope = FindContainingScriptBlock(aliasDeclaration);
        if (aliasScope is null)
            return false;

        for (Ast? current = invocation; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, aliasScope))
                return true;
        }

        return false;
    }

    private static bool HasTrustedModuleQualification(string? rawInvocationName, string normalizedInvocationName)
    {
        if (string.IsNullOrWhiteSpace(rawInvocationName))
            return true;

        var separator = rawInvocationName!.LastIndexOf('\\');
        if (separator < 0)
            return true;

        var moduleName = rawInvocationName.Substring(0, separator);
        if (string.Equals(normalizedInvocationName, "ForEach-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Where-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Invoke-Command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Start-Job", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(moduleName, "Microsoft.PowerShell.Core", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(normalizedInvocationName, "Sort-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Group-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Measure-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Select-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Measure-Command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Trace-Command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Register-ObjectEvent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Register-EngineEvent", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(moduleName, "Microsoft.PowerShell.Utility", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(normalizedInvocationName, "Register-WmiEvent", StringComparison.OrdinalIgnoreCase))
            return string.Equals(moduleName, "Microsoft.PowerShell.Management", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(normalizedInvocationName, "Register-CimIndicationEvent", StringComparison.OrdinalIgnoreCase))
            return string.Equals(moduleName, "CimCmdlets", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(normalizedInvocationName, "Start-ThreadJob", StringComparison.OrdinalIgnoreCase))
            return string.Equals(moduleName, "ThreadJob", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(normalizedInvocationName, "Start-RSJob", StringComparison.OrdinalIgnoreCase))
            return string.Equals(moduleName, "PoshRSJob", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static bool HasUnresolvedSplat(CommandAst invocation)
    {
        return invocation.CommandElements
            .OfType<VariableExpressionAst>()
            .Any(variable => variable.Splatted);
    }

    private static string NormalizeInvocationName(string? invocationName)
    {
        if (string.IsNullOrWhiteSpace(invocationName))
            return string.Empty;

        var normalized = invocationName!.Trim();
        var moduleSeparator = normalized.LastIndexOf('\\');
        if (moduleSeparator >= 0 && moduleSeparator + 1 < normalized.Length)
            normalized = normalized.Substring(moduleSeparator + 1);

        if (string.Equals(normalized, "sajb", StringComparison.OrdinalIgnoreCase))
            return "Start-Job";
        if (string.Equals(normalized, "icm", StringComparison.OrdinalIgnoreCase))
            return "Invoke-Command";
        if (string.Equals(normalized, "%", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "foreach", StringComparison.OrdinalIgnoreCase))
        {
            return "ForEach-Object";
        }
        if (string.Equals(normalized, "?", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "where", StringComparison.OrdinalIgnoreCase))
        {
            return "Where-Object";
        }
        if (string.Equals(normalized, "sort", StringComparison.OrdinalIgnoreCase))
            return "Sort-Object";
        if (string.Equals(normalized, "group", StringComparison.OrdinalIgnoreCase))
            return "Group-Object";
        if (string.Equals(normalized, "measure", StringComparison.OrdinalIgnoreCase))
            return "Measure-Object";
        if (string.Equals(normalized, "select", StringComparison.OrdinalIgnoreCase))
            return "Select-Object";
        if (string.Equals(normalized, "sal", StringComparison.OrdinalIgnoreCase))
            return "Set-Alias";
        if (string.Equals(normalized, "nal", StringComparison.OrdinalIgnoreCase))
            return "New-Alias";

        return normalized;
    }

    private static bool HasAnyParameter(CommandAst invocation, params string[] parameterNames)
    {
        foreach (var parameter in invocation.CommandElements.OfType<CommandParameterAst>())
        {
            var actualName = parameter.ParameterName;
            if (string.IsNullOrWhiteSpace(actualName))
                continue;

            if (parameterNames.Any(name => name.StartsWith(actualName, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static bool? TryHasAnyBoundParameter(CommandAst invocation, params string[] parameterNames)
    {
        try
        {
            var binding = StaticParameterBinder.BindCommand(invocation);
            if (binding.BoundParameters.Keys.Any(
                    key => parameterNames.Any(name => string.Equals(name, key, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }

            if (binding.BindingExceptions.Count == 0)
                return false;
        }
        catch
        {
            // Fall back to AST-only matching when command metadata is unavailable.
        }

        return null;
    }

    private static bool? TryGetBoundSwitchValue(CommandAst invocation, string parameterName)
    {
        foreach (var parameter in invocation.CommandElements.OfType<CommandParameterAst>())
        {
            if (!parameterName.StartsWith(parameter.ParameterName, StringComparison.OrdinalIgnoreCase))
                continue;

            return parameter.Argument switch
            {
                null => true,
                VariableExpressionAst variable when
                    string.Equals(variable.VariablePath.UserPath, "false", StringComparison.OrdinalIgnoreCase) => false,
                VariableExpressionAst variable when
                    string.Equals(variable.VariablePath.UserPath, "true", StringComparison.OrdinalIgnoreCase) => true,
                ConstantExpressionAst constant when constant.Value is bool value => value,
                _ => null
            };
        }

        return false;
    }

    private static bool HasPositionalRemoteTarget(CommandAst invocation)
    {
        foreach (var element in invocation.CommandElements.Skip(1))
        {
            if (element is CommandParameterAst)
                continue;

            if (element is ScriptBlockExpressionAst)
                return false;

            return element is StringConstantExpressionAst ||
                   element is ExpandableStringExpressionAst ||
                   element is ArrayLiteralAst ||
                   element is ArrayExpressionAst;
        }

        return false;
    }
}
