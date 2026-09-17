using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Evaluates whether a nested PowerShell function declaration is visible at a command call site.
/// </summary>
internal static class NestedFunctionVisibilityAnalyzer
{
    internal static bool IsDeclaredInVisibleScope(
        CommandAst command,
        string commandName,
        IReadOnlyDictionary<string, FunctionDefinitionAst[]> functionDeclarationsByName)
    {
        if (!functionDeclarationsByName.TryGetValue(commandName, out var declarations))
            return false;

        foreach (var declaration in declarations)
        {
            if (!TryGetDeclarationContext(declaration, out var declarationScope, out var declarationBlock) ||
                !IsDeclarationBlockCompatibleWithCommand(declarationBlock, command, declarationScope))
            {
                continue;
            }

            if (!TryGetDeferredEntryFunction(command, declaration, declarationScope, out var deferredEntryFunction))
                continue;

            if (ReferenceEquals(deferredEntryFunction, declaration))
                return true;

            if (deferredEntryFunction is not null)
            {
                if (!IsInvokedBeforeDeclaration(deferredEntryFunction, declaration, declarationScope))
                    return true;

                continue;
            }

            if (declaration.Extent.EndOffset <= command.Extent.StartOffset)
                return true;
        }

        return false;
    }

    private static bool TryGetDeclarationContext(
        FunctionDefinitionAst declaration,
        out ScriptBlockAst declarationScope,
        out NamedBlockAst declarationBlock)
    {
        declarationScope = null!;
        declarationBlock = null!;

        var syntacticScope = FindContainingScriptBlock(declaration);
        if (syntacticScope is null ||
            declaration.Parent is not NamedBlockAst syntacticBlock ||
            !ReferenceEquals(syntacticBlock.Parent, syntacticScope))
        {
            return false;
        }

        declarationScope = syntacticScope;
        declarationBlock = syntacticBlock;

        while (declarationScope.Parent is ScriptBlockExpressionAst expression &&
               expression.Parent is CommandAst invocation &&
               invocation.InvocationOperator == TokenKind.Dot)
        {
            var parentScope = FindContainingScriptBlock(invocation);
            var parentBlock = parentScope is null ? null : FindNamedBlockInScope(invocation, parentScope);
            if (parentScope is null ||
                parentBlock is null ||
                invocation.Parent is not PipelineAst pipeline ||
                !ReferenceEquals(pipeline.Parent, parentBlock))
            {
                break;
            }

            declarationScope = parentScope;
            declarationBlock = parentBlock;
        }

        return true;
    }

    private static bool IsDeclarationBlockCompatibleWithCommand(
        NamedBlockAst declarationBlock,
        CommandAst command,
        ScriptBlockAst declarationScope)
    {
        var commandBlock = FindNamedBlockInScope(command, declarationScope);
        if (commandBlock is null)
            return false;

        if (ReferenceEquals(commandBlock, declarationBlock))
            return true;

        return declarationBlock.BlockKind == TokenKind.Begin &&
               (commandBlock.BlockKind == TokenKind.Process || commandBlock.BlockKind == TokenKind.End);
    }

    private static NamedBlockAst? FindNamedBlockInScope(Ast ast, ScriptBlockAst scope)
    {
        for (Ast? current = ast.Parent; current is not null && !ReferenceEquals(current, scope); current = current.Parent)
        {
            if (current is NamedBlockAst namedBlock && ReferenceEquals(namedBlock.Parent, scope))
                return namedBlock;
        }

        return null;
    }

    private static bool TryGetDeferredEntryFunction(
        CommandAst command,
        FunctionDefinitionAst declaration,
        ScriptBlockAst declarationScope,
        out FunctionDefinitionAst? deferredEntryFunction)
    {
        deferredEntryFunction = null;

        for (Ast? current = command; current is not null; current = current.Parent)
        {
            if (current is ScriptBlockExpressionAst scriptBlockExpression &&
                IsEscapingOrIsolatedScriptBlock(scriptBlockExpression))
            {
                return false;
            }

            if (current is FunctionDefinitionAst function)
            {
                if (ReferenceEquals(function, declaration))
                {
                    deferredEntryFunction = declaration;
                    return true;
                }

                if (ReferenceEquals(FindContainingScriptBlock(function), declarationScope))
                    deferredEntryFunction = function;
            }

            if (ReferenceEquals(current, declarationScope))
                return true;
        }

        return false;
    }

    private static bool IsInvokedBeforeDeclaration(
        FunctionDefinitionAst deferredEntryFunction,
        FunctionDefinitionAst declaration,
        ScriptBlockAst declarationScope)
    {
        var functionsByName = declarationScope.FindAll(
                ast => ast is FunctionDefinitionAst,
                searchNestedScriptBlocks: true)
            .Cast<FunctionDefinitionAst>()
            .Select(function => new
            {
                Function = function,
                HasContext = TryGetDeclarationContext(function, out var scope, out _),
                Scope = scope
            })
            .Where(item => item.HasContext && ReferenceEquals(item.Scope, declarationScope))
            .GroupBy(item => item.Function.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Function).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var queue = new Queue<(FunctionDefinitionAst Function, int InvocationOffset)>();
        var processedInvocationOffsets = new Dictionary<FunctionDefinitionAst, int>();

        var rootInvocations = declarationScope.FindAll(
                ast => ast is CommandAst,
                searchNestedScriptBlocks: true)
            .Cast<CommandAst>();

        foreach (var invocation in rootInvocations)
        {
            if (invocation.Extent.StartOffset >= declaration.Extent.EndOffset ||
                !ExecutesWhileScopeInitializes(invocation, declarationScope))
            {
                continue;
            }

            EnqueueAvailableFunctions(invocation, invocation.Extent.StartOffset, functionsByName, declarationScope, queue);
        }

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            if (processedInvocationOffsets.TryGetValue(item.Function, out var processedOffset) &&
                processedOffset >= item.InvocationOffset)
            {
                continue;
            }

            processedInvocationOffsets[item.Function] = item.InvocationOffset;

            if (ReferenceEquals(item.Function, deferredEntryFunction))
                return true;

            var nestedInvocations = item.Function.Body.FindAll(
                    ast => ast is CommandAst,
                    searchNestedScriptBlocks: true)
                .Cast<CommandAst>();

            foreach (var invocation in nestedInvocations)
            {
                if (!ExecutesWhileScopeInitializes(invocation, item.Function.Body))
                    continue;

                EnqueueAvailableFunctions(invocation, item.InvocationOffset, functionsByName, declarationScope, queue);
            }
        }

        return false;
    }

    private static void EnqueueAvailableFunctions(
        CommandAst invocation,
        int invocationOffset,
        IReadOnlyDictionary<string, FunctionDefinitionAst[]> functionsByName,
        ScriptBlockAst declarationScope,
        Queue<(FunctionDefinitionAst Function, int InvocationOffset)> queue)
    {
        var invocationName = invocation.GetCommandName();
        if (string.IsNullOrWhiteSpace(invocationName) ||
            !functionsByName.TryGetValue(invocationName, out var candidates))
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            if (candidate.Extent.EndOffset > invocationOffset ||
                !TryGetDeclarationContext(candidate, out var candidateScope, out var candidateBlock) ||
                !ReferenceEquals(candidateScope, declarationScope) ||
                !IsDeclarationBlockCompatibleWithCommand(candidateBlock, invocation, declarationScope))
            {
                continue;
            }

            queue.Enqueue((candidate, invocationOffset));
        }
    }

    private static bool ExecutesWhileScopeInitializes(CommandAst command, ScriptBlockAst declarationScope)
    {
        for (Ast? current = command.Parent; current is not null; current = current.Parent)
        {
            if (current is FunctionDefinitionAst)
                return false;

            if (current is ScriptBlockExpressionAst scriptBlockExpression &&
                IsEscapingOrIsolatedScriptBlock(scriptBlockExpression))
            {
                return false;
            }

            if (ReferenceEquals(current, declarationScope))
                return true;
        }

        return false;
    }

    private static bool IsEscapingOrIsolatedScriptBlock(ScriptBlockExpressionAst scriptBlockExpression)
    {
        if (scriptBlockExpression.Parent is not CommandAst invocation)
            return true;

        var invocationName = NormalizeInvocationName(invocation.GetCommandName());
        if (invocationName.Length == 0)
            return false;

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
            if (remoteBinding.HasValue)
                return remoteBinding.Value;

            return HasAnyParameter(
                       invocation,
                       "ComputerName",
                       "ConnectionUri",
                       "Session",
                       "HostName",
                       "SSHConnection",
                       "VMId",
                       "VMName",
                       "ContainerId") ||
                   HasPositionalRemoteTarget(invocation);
        }

        if (!string.Equals(invocationName, "ForEach-Object", StringComparison.OrdinalIgnoreCase))
            return false;

        var parallelBinding = TryHasAnyBoundParameter(invocation, "Parallel");
        return parallelBinding ?? HasAnyParameter(invocation, "Parallel");
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

    private static ScriptBlockAst? FindContainingScriptBlock(Ast ast)
    {
        for (Ast? current = ast.Parent; current is not null; current = current.Parent)
        {
            if (current is ScriptBlockAst scriptBlock)
                return scriptBlock;
        }

        return null;
    }
}
