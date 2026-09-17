using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Evaluates whether a nested PowerShell function declaration is visible at a command call site.
/// </summary>
internal sealed partial class NestedFunctionVisibilityAnalyzer
{
    private readonly IReadOnlyDictionary<string, FunctionDefinitionAst[]> _functionDeclarationsByName;
    private readonly Dictionary<ScriptBlockAst, ScopeExecutionGraph> _executionGraphs = new();
    private readonly Dictionary<FunctionDefinitionAst, Dictionary<FunctionDefinitionAst, bool>>
        _invokedBeforeDeclarationCache = new();
    private readonly Dictionary<FunctionDefinitionAst, bool> _deferredFunctionEscapeCache = new();
    private readonly HashSet<CommandAst> _shadowResolutionInProgress = new();
    private readonly IReadOnlyDictionary<string, CommandAst[]> _aliasDeclarationsByName;

    internal NestedFunctionVisibilityAnalyzer(
        ScriptBlockAst root,
        IReadOnlyDictionary<string, FunctionDefinitionAst[]> functionDeclarationsByName)
    {
        _functionDeclarationsByName = functionDeclarationsByName;
        _aliasDeclarationsByName = FindAuthoredAliasDeclarations(root);
    }

    internal static string NormalizeDeclaredFunctionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var separator = name.IndexOf(':');
        if (separator <= 0 || separator + 1 >= name.Length)
            return name;

        var qualifier = name.Substring(0, separator);
        return string.Equals(qualifier, "local", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(qualifier, "script", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(qualifier, "global", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(qualifier, "private", StringComparison.OrdinalIgnoreCase)
            ? name.Substring(separator + 1)
            : name;
    }

    internal bool IsDeclaredInVisibleScope(CommandAst command, string commandName)
    {
        if (!_functionDeclarationsByName.TryGetValue(commandName, out var declarations))
            return false;

        foreach (var declaration in declarations)
        {
            if (IsDominatingDeclarationInContainingStatementBlock(declaration, command))
                return true;

            if (!TryGetDeclarationContext(declaration, out var declarationScope, out var declarationBlock) ||
                !IsDeclarationBlockCompatibleWithCommand(declaration, declarationBlock, command, declarationScope))
            {
                continue;
            }

            if (!TryGetDeferredEntryFunction(command, declaration, declarationScope, out var deferredEntryFunction))
                continue;

            if (ReferenceEquals(deferredEntryFunction, declaration))
                return true;

            if (deferredEntryFunction is not null)
            {
                if (DoesDeferredFunctionBodyEscape(deferredEntryFunction, declarationScope))
                    continue;

                if (!IsInvokedBeforeDeclarationCached(deferredEntryFunction, declaration, declarationScope))
                    return true;

                continue;
            }

            if (declaration.Extent.EndOffset <= command.Extent.StartOffset)
                return true;
        }

        return false;
    }

    private bool IsInvokedBeforeDeclarationCached(
        FunctionDefinitionAst deferredEntryFunction,
        FunctionDefinitionAst declaration,
        ScriptBlockAst declarationScope)
    {
        if (!_invokedBeforeDeclarationCache.TryGetValue(declaration, out var results))
        {
            results = new Dictionary<FunctionDefinitionAst, bool>();
            _invokedBeforeDeclarationCache[declaration] = results;
        }

        if (results.TryGetValue(deferredEntryFunction, out var cached))
            return cached;

        var result = IsInvokedBeforeDeclaration(deferredEntryFunction, declaration, declarationScope);
        results[deferredEntryFunction] = result;
        return result;
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
               IsScopePromotingInvocation(expression, invocation))
        {
            if (declarationBlock.BlockKind == TokenKind.Process)
                break;

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

    private static bool TryGetPotentialDeclarationContext(
        FunctionDefinitionAst declaration,
        out ScriptBlockAst declarationScope,
        out NamedBlockAst declarationBlock)
    {
        declarationScope = null!;
        declarationBlock = null!;

        var syntacticScope = FindContainingScriptBlock(declaration);
        var syntacticBlock = syntacticScope is null ? null : FindNamedBlockInScope(declaration, syntacticScope);
        if (syntacticScope is null || syntacticBlock is null)
            return false;

        declarationScope = syntacticScope;
        declarationBlock = syntacticBlock;

        while (declarationScope.Parent is ScriptBlockExpressionAst expression &&
               expression.Parent is CommandAst invocation &&
               IsScopePromotingInvocation(expression, invocation))
        {
            var parentScope = FindContainingScriptBlock(invocation);
            var parentBlock = parentScope is null ? null : FindNamedBlockInScope(invocation, parentScope);
            if (parentScope is null || parentBlock is null)
                break;

            declarationScope = parentScope;
            declarationBlock = parentBlock;
        }

        return true;
    }

    private static bool IsDeclarationBlockCompatibleWithCommand(
        FunctionDefinitionAst declaration,
        NamedBlockAst declarationBlock,
        CommandAst command,
        ScriptBlockAst declarationScope)
    {
        var commandBlock = FindNamedBlockInScope(command, declarationScope);
        if (commandBlock is null)
            return false;

        if (ReferenceEquals(commandBlock, declarationBlock))
            return true;

        if (declarationBlock.BlockKind != TokenKind.Begin)
            return false;

        if (commandBlock.BlockKind == TokenKind.Process || commandBlock.BlockKind == TokenKind.End)
            return true;

        return string.Equals(commandBlock.BlockKind.ToString(), "Clean", StringComparison.OrdinalIgnoreCase) &&
               DeclarationDominatesCleanTransfer(declaration, declarationBlock);
    }

    private static bool DeclarationDominatesCleanTransfer(
        FunctionDefinitionAst declaration,
        NamedBlockAst declarationBlock)
    {
        return declarationBlock.Statements
            .Where(statement => statement.Extent.EndOffset <= declaration.Extent.StartOffset)
            .All(statement => statement is FunctionDefinitionAst || statement is TrapStatementAst);
    }

    private static bool IsDominatingDeclarationInContainingStatementBlock(
        FunctionDefinitionAst declaration,
        CommandAst command)
    {
        if (declaration.Parent is not StatementBlockAst statementBlock ||
            declaration.Extent.EndOffset > command.Extent.StartOffset ||
            !ReferenceEquals(FindContainingScriptBlock(declaration), FindContainingScriptBlock(command)))
        {
            return false;
        }

        for (Ast? current = command.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, statementBlock))
                return true;
            if (current is ScriptBlockAst)
                return false;
        }

        return false;
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

    private bool TryGetDeferredEntryFunction(
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

    private bool IsInvokedBeforeDeclaration(
        FunctionDefinitionAst deferredEntryFunction,
        FunctionDefinitionAst declaration,
        ScriptBlockAst declarationScope)
    {
        var graph = GetExecutionGraph(declarationScope);

        var queue = new Queue<(FunctionDefinitionAst Function, int InvocationOffset)>();
        var processedInvocationOffsets = new Dictionary<FunctionDefinitionAst, int>();

        foreach (var invocation in graph.RootInvocations)
        {
            if (!ExecutesWhileScopeInitializes(invocation.Command, declarationScope))
                continue;

            var invocationOffset = invocation.Command.Extent.StartOffset;
            if (invocation.Trap is not null)
            {
                var trapOffset = TryGetTrapExecutionOffset(invocation.Trap, declaration, declarationScope);
                if (!trapOffset.HasValue)
                    continue;

                invocationOffset = trapOffset.Value;
            }
            else if (invocationOffset >= declaration.Extent.EndOffset)
            {
                continue;
            }

            EnqueueAvailableFunctions(invocation, invocationOffset, graph.FunctionsByName, declarationScope, queue);
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

            foreach (var invocation in graph.GetFunctionInvocations(item.Function))
            {
                if (!ExecutesWhileScopeInitializes(invocation.Command, item.Function.Body))
                    continue;

                EnqueueAvailableFunctions(
                    invocation,
                    item.InvocationOffset,
                    graph.FunctionsByName,
                    declarationScope,
                    queue);
            }
        }

        return false;
    }

    private static void EnqueueAvailableFunctions(
        InvocationSite invocation,
        int invocationOffset,
        IReadOnlyDictionary<string, FunctionDefinitionAst[]> functionsByName,
        ScriptBlockAst declarationScope,
        Queue<(FunctionDefinitionAst Function, int InvocationOffset)> queue)
    {
        if (invocation.IsDynamic)
        {
            foreach (var candidates in functionsByName.Values)
                EnqueueCandidates(invocation.Command, invocationOffset, candidates, declarationScope, queue);
            return;
        }

        var invocationName = invocation.Name;
        if (string.IsNullOrWhiteSpace(invocationName) ||
            !functionsByName.TryGetValue(invocationName!, out var namedCandidates))
        {
            return;
        }

        EnqueueCandidates(invocation.Command, invocationOffset, namedCandidates, declarationScope, queue);
    }

    private static void EnqueueCandidates(
        CommandAst invocation,
        int invocationOffset,
        IEnumerable<FunctionDefinitionAst> candidates,
        ScriptBlockAst declarationScope,
        Queue<(FunctionDefinitionAst Function, int InvocationOffset)> queue)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Extent.EndOffset > invocationOffset ||
                !TryGetPotentialDeclarationContext(candidate, out var candidateScope, out var candidateBlock) ||
                !ReferenceEquals(candidateScope, declarationScope) ||
                !IsDeclarationBlockCompatibleWithCommand(
                    candidate,
                    candidateBlock,
                    invocation,
                    declarationScope))
            {
                continue;
            }

            queue.Enqueue((candidate, invocationOffset));
        }
    }

    private ScopeExecutionGraph GetExecutionGraph(ScriptBlockAst declarationScope)
    {
        if (_executionGraphs.TryGetValue(declarationScope, out var graph))
            return graph;

        graph = new ScopeExecutionGraph(declarationScope);
        _executionGraphs[declarationScope] = graph;
        return graph;
    }

    private bool ExecutesWhileScopeInitializes(CommandAst command, ScriptBlockAst declarationScope)
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

    private static bool IsEscapingFunctionProviderReference(Ast ast, FunctionDefinitionAst function)
    {
        string? providerPath = ast is VariableExpressionAst variable
            ? variable.VariablePath.UserPath
            : null;
        if (ast is CommandAst command && IsFunctionLookupCommand(command, function.Name))
            return true;

        if (!TryGetFunctionProviderName(providerPath, out var functionName) ||
            !string.Equals(functionName, function.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ast is VariableExpressionAst &&
            ast.Parent is CommandAst invocation &&
            IsInvocationTarget(ast, invocation) &&
            (invocation.InvocationOperator == TokenKind.Ampersand || invocation.InvocationOperator == TokenKind.Dot))
        {
            return false;
        }

        return true;
    }

    private static bool IsFunctionLookupCommand(CommandAst command, string functionName)
    {
        var commandName = command.GetCommandName();
        if (string.Equals(commandName, "Get-Command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "gcm", StringComparison.OrdinalIgnoreCase))
        {
            return command.CommandElements
                .Skip(1)
                .OfType<StringConstantExpressionAst>()
                .Any(argument => string.Equals(argument.Value, functionName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(commandName, "Get-Item", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "gi", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "Get-Content", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "gc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return command.CommandElements
            .Skip(1)
            .OfType<StringConstantExpressionAst>()
            .Select(argument => argument.Value)
            .Any(path => TryGetFunctionProviderName(path, out var providerFunction) &&
                         string.Equals(providerFunction, functionName, StringComparison.OrdinalIgnoreCase));
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

    private static bool IsInvocationTarget(Ast target, CommandAst invocation)
    {
        return invocation.CommandElements.Count > 0 &&
               ReferenceEquals(invocation.CommandElements[0], target);
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

    private static TrapStatementAst? FindContainingTrap(CommandAst command, ScriptBlockAst declarationScope)
    {
        for (Ast? current = command.Parent; current is not null && !ReferenceEquals(current, declarationScope); current = current.Parent)
        {
            if (current is TrapStatementAst trap)
                return trap;
        }

        return null;
    }

    private static int? TryGetTrapExecutionOffset(
        TrapStatementAst trap,
        FunctionDefinitionAst declaration,
        ScriptBlockAst declarationScope)
    {
        if (!TryGetDeclarationContext(declaration, out var effectiveScope, out var declarationBlock) ||
            !ReferenceEquals(effectiveScope, declarationScope))
        {
            return null;
        }

        var trapBlock = FindNamedBlockInScope(trap, declarationScope);
        if (!ReferenceEquals(trapBlock, declarationBlock))
            return null;

        var triggeringOffsets = declarationBlock.Statements
            .Where(statement => statement.Extent.EndOffset <= declaration.Extent.StartOffset)
            .Where(statement => statement is not FunctionDefinitionAst && statement is not TrapStatementAst)
            .Select(statement => statement.Extent.StartOffset)
            .ToList();

        var syntacticScope = FindContainingScriptBlock(declaration);
        var syntacticBlock = syntacticScope is null ? null : FindNamedBlockInScope(declaration, syntacticScope);
        if (syntacticBlock is not null && !ReferenceEquals(syntacticBlock, declarationBlock))
        {
            triggeringOffsets.AddRange(syntacticBlock.Statements
                .Where(statement => statement.Extent.EndOffset <= declaration.Extent.StartOffset)
                .Where(statement => statement is not FunctionDefinitionAst && statement is not TrapStatementAst)
                .Select(statement => statement.Extent.StartOffset));
        }

        return triggeringOffsets.Count == 0 ? null : triggeringOffsets.Min();
    }

    private sealed class ScopeExecutionGraph
    {
        private readonly Dictionary<FunctionDefinitionAst, InvocationSite[]> _functionInvocations = new();

        internal ScopeExecutionGraph(ScriptBlockAst scope)
        {
            var functions = scope.FindAll(
                    ast => ast is FunctionDefinitionAst,
                    searchNestedScriptBlocks: true)
                .Cast<FunctionDefinitionAst>()
                .Select(function => new
                {
                    Function = function,
                    HasContext = TryGetPotentialDeclarationContext(function, out var declarationScope, out _),
                    Scope = declarationScope
                })
                .Where(item => item.HasContext && ReferenceEquals(item.Scope, scope))
                .Select(item => item.Function)
                .ToArray();

            FunctionsByName = functions
                .GroupBy(
                    function => NormalizeDeclaredFunctionName(function.Name),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            RootInvocations = scope.FindAll(
                    ast => ast is CommandAst,
                    searchNestedScriptBlocks: true)
                .Cast<CommandAst>()
                .Select(command => new InvocationSite(command, FindContainingTrap(command, scope)))
                .ToArray();

            foreach (var function in functions)
            {
                _functionInvocations[function] = function.Body.FindAll(
                    ast => ast is CommandAst,
                    searchNestedScriptBlocks: true)
                    .Cast<CommandAst>()
                    .Select(command => new InvocationSite(command, trap: null))
                    .ToArray();
            }
        }

        internal IReadOnlyDictionary<string, FunctionDefinitionAst[]> FunctionsByName { get; }
        internal InvocationSite[] RootInvocations { get; }

        internal InvocationSite[] GetFunctionInvocations(FunctionDefinitionAst function)
        {
            return _functionInvocations.TryGetValue(function, out var invocations)
                ? invocations
                : Array.Empty<InvocationSite>();
        }
    }

    private sealed class InvocationSite
    {
        internal InvocationSite(CommandAst command, TrapStatementAst? trap)
        {
            Command = command;
            Trap = trap;
            Name = command.GetCommandName();
            IsDynamic = string.IsNullOrWhiteSpace(Name) &&
                        command.CommandElements.Count > 0 &&
                        command.CommandElements[0] is not ScriptBlockExpressionAst;
        }

        internal CommandAst Command { get; }
        internal string? Name { get; }
        internal bool IsDynamic { get; }
        internal TrapStatementAst? Trap { get; }
    }
}
