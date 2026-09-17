using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Evaluates whether a nested PowerShell function declaration is visible at a command call site.
/// </summary>
internal sealed class NestedFunctionVisibilityAnalyzer
{
    private readonly IReadOnlyDictionary<string, FunctionDefinitionAst[]> _functionDeclarationsByName;
    private readonly Dictionary<ScriptBlockAst, ScopeExecutionGraph> _executionGraphs = new();
    private readonly Dictionary<FunctionDefinitionAst, Dictionary<FunctionDefinitionAst, bool>>
        _invokedBeforeDeclarationCache = new();

    internal NestedFunctionVisibilityAnalyzer(
        IReadOnlyDictionary<string, FunctionDefinitionAst[]> functionDeclarationsByName)
    {
        _functionDeclarationsByName = functionDeclarationsByName;
    }

    internal bool IsDeclaredInVisibleScope(CommandAst command, string commandName)
    {
        if (!_functionDeclarationsByName.TryGetValue(commandName, out var declarations))
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
                !TryGetDeclarationContext(candidate, out var candidateScope, out var candidateBlock) ||
                !ReferenceEquals(candidateScope, declarationScope) ||
                !IsDeclarationBlockCompatibleWithCommand(candidateBlock, invocation, declarationScope))
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

        if (invocation.InvocationOperator == TokenKind.Ampersand ||
            invocation.InvocationOperator == TokenKind.Dot)
        {
            return false;
        }

        var invocationName = NormalizeInvocationName(invocation.GetCommandName());
        if (invocationName.Length == 0)
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

        if (string.Equals(invocationName, "ForEach-Object", StringComparison.OrdinalIgnoreCase))
        {
            if (HasUnresolvedSplat(invocation))
                return true;

            var parallelBinding = TryHasAnyBoundParameter(invocation, "Parallel");
            return parallelBinding ?? HasAnyParameter(invocation, "Parallel");
        }

        return !IsKnownSynchronousScriptBlockConsumer(invocationName);
    }

    private static bool IsKnownSynchronousScriptBlockConsumer(string invocationName)
    {
        return string.Equals(invocationName, "Where-Object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Sort-Object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Group-Object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Measure-Object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Select-Object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Measure-Command", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Trace-Command", StringComparison.OrdinalIgnoreCase);
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
        if (declaration.Parent is not NamedBlockAst declarationBlock ||
            !ReferenceEquals(declarationBlock.Parent, declarationScope))
        {
            return null;
        }

        var trapBlock = FindNamedBlockInScope(trap, declarationScope);
        if (!ReferenceEquals(trapBlock, declarationBlock))
            return null;

        var triggeringStatement = declarationBlock.Statements
            .Where(statement => statement.Extent.StartOffset < declaration.Extent.StartOffset)
            .Where(statement => statement is not FunctionDefinitionAst && statement is not TrapStatementAst)
            .OrderBy(statement => statement.Extent.StartOffset)
            .FirstOrDefault();

        return triggeringStatement?.Extent.StartOffset;
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
                    HasContext = TryGetDeclarationContext(function, out var declarationScope, out _),
                    Scope = declarationScope
                })
                .Where(item => item.HasContext && ReferenceEquals(item.Scope, scope))
                .Select(item => item.Function)
                .ToArray();

            FunctionsByName = functions
                .GroupBy(function => function.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            RootInvocations = scope.FindAll(
                    ast => ast is CommandAst,
                    searchNestedScriptBlocks: true)
                .Cast<CommandAst>()
                .Where(command => ExecutesWhileScopeInitializes(command, scope))
                .Select(command => new InvocationSite(command, FindContainingTrap(command, scope)))
                .ToArray();

            foreach (var function in functions)
            {
                _functionInvocations[function] = function.Body.FindAll(
                        ast => ast is CommandAst,
                        searchNestedScriptBlocks: true)
                    .Cast<CommandAst>()
                    .Where(command => ExecutesWhileScopeInitializes(command, function.Body))
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
