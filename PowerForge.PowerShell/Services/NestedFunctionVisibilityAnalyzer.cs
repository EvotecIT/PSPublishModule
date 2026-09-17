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
    private readonly ScriptBlockAst _root;
    private readonly IReadOnlyDictionary<string, FunctionDefinitionAst[]> _functionDeclarationsByName;
    private readonly Dictionary<ScriptBlockAst, ScopeExecutionGraph> _executionGraphs = new();
    private readonly Dictionary<FunctionDefinitionAst, Dictionary<FunctionDefinitionAst, bool>>
        _invokedBeforeDeclarationCache = new();
    private readonly Dictionary<FunctionDefinitionAst, bool> _deferredFunctionEscapeCache = new();
    private readonly HashSet<CommandAst> _shadowResolutionInProgress = new();
    private readonly IReadOnlyDictionary<string, CommandAst[]> _aliasDeclarationsByName =
        new Dictionary<string, CommandAst[]>(StringComparer.OrdinalIgnoreCase);
    private readonly CommandAst[] _dynamicAliasDeclarations = Array.Empty<CommandAst>();
    private readonly IReadOnlyDictionary<string, CommandAst[]> _aliasRemovalsByName =
        new Dictionary<string, CommandAst[]>(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, CommandAst[]> _functionRemovalsByName =
        new Dictionary<string, CommandAst[]>(StringComparer.OrdinalIgnoreCase);

    internal NestedFunctionVisibilityAnalyzer(
        ScriptBlockAst root,
        IReadOnlyDictionary<string, FunctionDefinitionAst[]> functionDeclarationsByName)
    {
        _root = root;
        _functionDeclarationsByName = functionDeclarationsByName;
        _aliasDeclarationsByName = FindAuthoredAliasDeclarations(root);
        _dynamicAliasDeclarations = FindDynamicAliasDeclarations(root);
        _aliasRemovalsByName = FindAliasRemovalCommands(root);
        _functionRemovalsByName = FindFunctionRemovalCommands(root);
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
            if (!IsDeclarationQualifierVisibleAtCommand(declaration, command))
                continue;
            if (IsRemovedBeforeCommand(declaration, command))
                continue;

            if (IsDominatingDeclarationInContainingStatementBlock(declaration, command) ||
                IsPromotedDeclarationDominatingCommand(declaration, command) ||
                DeclarationDominatesFinallyCall(declaration, command) ||
                DeclarationDominatesFollowingTryCall(declaration, command) ||
                DeclarationDominatesFollowingDoLoopCall(declaration, command) ||
                IsQualifiedDeclarationInstalledBeforeCommand(declaration, command))
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

            var directExecutionOffset = command.Extent.StartOffset;
            var trap = FindContainingTrap(command, declarationScope);
            if (trap is not null &&
                TryGetTrapExecutionOffset(trap, declaration, declarationScope) is int trapExecutionOffset)
            {
                directExecutionOffset = trapExecutionOffset;
            }

            if (declaration.Extent.EndOffset <= directExecutionOffset &&
                !IsRemovedBeforeExecution(declaration, directExecutionOffset, declarationScope, command))
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

    private bool TryGetDeclarationContext(
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
               IsScopePromotingInvocation(expression, invocation) &&
               DeclarationDominatesPromotedBlockExit(declaration))
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

    private bool TryGetPotentialDeclarationContext(
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
               IsScopePromotingInvocation(expression, invocation) &&
               DeclarationDominatesPromotedBlockExit(declaration))
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

    private bool IsPromotedDeclarationDominatingCommand(
        FunctionDefinitionAst declaration,
        CommandAst command)
    {
        var declarationScope = FindContainingScriptBlock(declaration);
        if (declarationScope?.Parent is not ScriptBlockExpressionAst expression ||
            expression.Parent is not CommandAst invocation ||
            !IsScopePromotingInvocation(expression, invocation) ||
            !DeclarationDominatesPromotedBlockExit(declaration) ||
            invocation.Parent is not PipelineAst pipeline ||
            pipeline.Parent is not StatementBlockAst statementBlock ||
            invocation.Extent.EndOffset > command.Extent.StartOffset)
        {
            return false;
        }

        var parentScope = FindContainingScriptBlock(invocation);
        if (parentScope is null || !ReferenceEquals(parentScope, FindContainingScriptBlock(command)))
            return false;

        for (Ast? current = command.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, statementBlock))
                return true;
            if (current is FunctionDefinitionAst || current is ScriptBlockExpressionAst)
                return false;
            if (ReferenceEquals(current, parentScope))
                return false;
        }

        return false;
    }

    private static bool DeclarationDominatesPromotedBlockExit(FunctionDefinitionAst declaration)
    {
        if (declaration.Parent is not NamedBlockAst declarationBlock)
            return false;

        return declarationBlock.Statements
            .Where(statement => statement.Extent.EndOffset <= declaration.Extent.StartOffset)
            .All(statement => statement is FunctionDefinitionAst || statement is TrapStatementAst);
    }

    private static bool DeclarationDominatesFinallyCall(
        FunctionDefinitionAst declaration,
        CommandAst command)
    {
        if (declaration.Parent is not StatementBlockAst tryBody ||
            tryBody.Parent is not TryStatementAst tryStatement ||
            !ReferenceEquals(tryStatement.Body, tryBody) ||
            tryStatement.Finally is null ||
            !ExecutesDirectlyInStatementBlock(command, tryStatement.Finally))
        {
            return false;
        }

        return tryBody.Statements
            .Where(statement => statement.Extent.EndOffset <= declaration.Extent.StartOffset)
            .All(statement => statement is FunctionDefinitionAst || statement is TrapStatementAst);
    }

    private static bool ExecutesDirectlyInStatementBlock(CommandAst command, StatementBlockAst statementBlock)
    {
        for (Ast? current = command.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, statementBlock))
                return true;
            if (current is FunctionDefinitionAst || current is ScriptBlockExpressionAst)
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

                var functionScope = FindContainingScriptBlock(function);
                if (ReferenceEquals(functionScope, declarationScope))
                {
                    deferredEntryFunction = function;
                }
                else if (functionScope is not null && DoesDeferredFunctionBodyEscape(function, functionScope))
                {
                    return false;
                }
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

        var queue = new Queue<(FunctionDefinitionAst Function, int InvocationOffset, CommandAst InvocationCommand)>();
        var processedInvocations = new HashSet<(FunctionDefinitionAst Function, int InvocationOffset, CommandAst InvocationCommand)>();

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
            else if (TryGetCleanExecutionOffset(
                         invocation.Command,
                         declaration,
                         declarationScope) is int cleanExecutionOffset)
            {
                invocationOffset = cleanExecutionOffset;
            }
            else if (invocationOffset >= declaration.Extent.EndOffset &&
                     !IsRemovedBeforeExecution(
                         declaration,
                         invocationOffset,
                         declarationScope,
                         invocation.Command))
            {
                continue;
            }

            EnqueueAvailableFunctions(invocation, invocationOffset, graph.FunctionsByName, declarationScope, queue);
        }

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            if (!processedInvocations.Add(item))
                continue;

            if (ReferenceEquals(item.Function, deferredEntryFunction))
                return true;

            foreach (var invocation in graph.GetFunctionInvocations(item.Function))
            {
                if (!ExecutesWhileScopeInitializes(invocation.Command, item.Function.Body))
                    continue;
                if (IsInsideBoundParameterDefault(
                        invocation.Command,
                        item.Function,
                        item.InvocationCommand))
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

    private void EnqueueAvailableFunctions(
        InvocationSite invocation,
        int invocationOffset,
        IReadOnlyDictionary<string, FunctionDefinitionAst[]> functionsByName,
        ScriptBlockAst declarationScope,
        Queue<(FunctionDefinitionAst Function, int InvocationOffset, CommandAst InvocationCommand)> queue)
    {
        if (invocation.IsDynamic ||
            HasDynamicAliasDeclarationBefore(invocation, invocationOffset) ||
            HasDynamicAliasTargetBefore(invocation, invocationOffset))
        {
            foreach (var candidates in functionsByName.Values)
                EnqueueCandidates(invocation.Command, invocationOffset, candidates, declarationScope, queue);
            return;
        }

        foreach (var invocationName in ResolveInvocationNames(invocation, invocationOffset))
        {
            if (string.IsNullOrWhiteSpace(invocationName) ||
                !functionsByName.TryGetValue(invocationName, out var namedCandidates))
            {
                continue;
            }

            EnqueueCandidates(invocation.Command, invocationOffset, namedCandidates, declarationScope, queue);
        }
    }

    private bool HasDynamicAliasDeclarationBefore(InvocationSite invocation, int executionOffset)
    {
        return _dynamicAliasDeclarations.Any(alias =>
            AliasCanShadowInvocation(alias, invocation.Command, executionOffset));
    }

    private bool HasDynamicAliasTargetBefore(InvocationSite invocation, int executionOffset)
    {
        var invocationName = invocation.Name;
        if (string.IsNullOrWhiteSpace(invocationName))
            return false;

        var aliasName = GetExactInvocationName(invocationName, NormalizeInvocationName(invocationName));
        if (!_aliasDeclarationsByName.TryGetValue(aliasName, out var declarations))
            return false;

        var eligible = declarations
            .Where(alias => AliasCanShadowInvocation(alias, invocation.Command, executionOffset))
            .OrderBy(alias => alias.Extent.EndOffset)
            .ToArray();
        var latestUnconditional = eligible
            .Where(IsUnconditionalAliasDeclaration)
            .OrderByDescending(alias => alias.Extent.EndOffset)
            .FirstOrDefault();
        var possibleDeclarations = latestUnconditional is null
            ? eligible
            : eligible.Where(alias => alias.Extent.EndOffset >= latestUnconditional.Extent.EndOffset);

        return possibleDeclarations.Any(alias =>
            string.IsNullOrWhiteSpace(TryGetAuthoredAliasTarget(alias)));
    }

    private IReadOnlyCollection<string> ResolveInvocationNames(InvocationSite invocation, int executionOffset)
    {
        var invocationName = invocation.Name;
        if (string.IsNullOrWhiteSpace(invocationName))
            return Array.Empty<string>();

        var aliasName = GetExactInvocationName(invocationName, NormalizeInvocationName(invocationName));
        if (!_aliasDeclarationsByName.TryGetValue(aliasName, out var declarations))
            return new[] { invocationName! };

        var eligible = declarations
            .Where(alias => AliasCanShadowInvocation(alias, invocation.Command, executionOffset))
            .OrderBy(alias => alias.Extent.EndOffset)
            .ToArray();
        var latestUnconditional = eligible
            .Where(IsUnconditionalAliasDeclaration)
            .OrderByDescending(alias => alias.Extent.EndOffset)
            .FirstOrDefault();
        var possibleDeclarations = latestUnconditional is null
            ? eligible
            : eligible.Where(alias => alias.Extent.EndOffset >= latestUnconditional.Extent.EndOffset);
        var names = possibleDeclarations
            .Select(TryGetAuthoredAliasTarget)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Select(target => NormalizeDeclaredFunctionName(target!))
            .ToList();
        if (latestUnconditional is null)
            names.Add(invocationName!);

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsUnconditionalAliasDeclaration(CommandAst declaration)
    {
        var declarationScope = FindContainingScriptBlock(declaration);
        return declarationScope is not null &&
               declaration.Parent is PipelineAst pipeline &&
               pipeline.Parent is NamedBlockAst block &&
               ReferenceEquals(block.Parent, declarationScope);
    }

    private void EnqueueCandidates(
        CommandAst invocation,
        int invocationOffset,
        IEnumerable<FunctionDefinitionAst> candidates,
        ScriptBlockAst declarationScope,
        Queue<(FunctionDefinitionAst Function, int InvocationOffset, CommandAst InvocationCommand)> queue)
    {
        var eligible = candidates
            .Where(candidate =>
                candidate.Extent.EndOffset <= invocationOffset &&
                TryGetPotentialDeclarationContext(candidate, out var candidateScope, out var candidateBlock) &&
                ReferenceEquals(candidateScope, declarationScope) &&
                IsDeclarationBlockCompatibleWithCommand(
                    candidate,
                    candidateBlock,
                    invocation,
                    declarationScope))
            .ToArray();
        var latestUnconditional = eligible
            .Where(candidate => candidate.Parent is NamedBlockAst)
            .OrderByDescending(candidate => candidate.Extent.EndOffset)
            .FirstOrDefault();

        foreach (var candidate in eligible)
        {
            if (candidate.Parent is NamedBlockAst &&
                latestUnconditional is not null &&
                !ReferenceEquals(candidate, latestUnconditional))
                continue;

            queue.Enqueue((candidate, invocationOffset, invocation));
        }
    }

    private static bool IsInsideBoundParameterDefault(
        CommandAst command,
        FunctionDefinitionAst function,
        CommandAst invocation)
    {
        var parameters = function.Parameters ?? function.Body.ParamBlock?.Parameters;
        if (parameters is null)
            return false;

        for (var parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
        {
            var parameter = parameters[parameterIndex];
            if (parameter.DefaultValue is null ||
                !ContainsAst(parameter.DefaultValue, command))
            {
                continue;
            }

            var parameterName = parameter.Name.VariablePath.UserPath;
            if (IsParameterStaticallyBound(invocation, parameterName, parameterIndex))
                return true;
        }

        return false;
    }

    private static bool IsParameterStaticallyBound(
        CommandAst invocation,
        string parameterName,
        int parameterIndex)
    {
        try
        {
            var binding = StaticParameterBinder.BindCommand(invocation);
            if (binding.BoundParameters.Keys.Any(key =>
                    string.Equals(key, parameterName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, parameterIndex.ToString(), StringComparison.Ordinal)))
            {
                return true;
            }
        }
        catch
        {
            // Fall back to named AST matching when static binding is incomplete.
        }

        return HasAnyParameter(invocation, parameterName);
    }

    private ScopeExecutionGraph GetExecutionGraph(ScriptBlockAst declarationScope)
    {
        if (_executionGraphs.TryGetValue(declarationScope, out var graph))
            return graph;

        graph = new ScopeExecutionGraph(this, declarationScope);
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

    private int? TryGetTrapExecutionOffset(
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

    private int? TryGetCleanExecutionOffset(
        CommandAst command,
        FunctionDefinitionAst declaration,
        ScriptBlockAst declarationScope)
    {
        var commandBlock = FindNamedBlockInScope(command, declarationScope);
        if (commandBlock is null ||
            !string.Equals(commandBlock.BlockKind.ToString(), "Clean", StringComparison.OrdinalIgnoreCase) ||
            !TryGetDeclarationContext(declaration, out var declarationEffectiveScope, out var declarationBlock) ||
            !ReferenceEquals(declarationEffectiveScope, declarationScope) ||
            declarationBlock.BlockKind != TokenKind.Begin)
        {
            return null;
        }

        var transferStatement = declarationBlock.Statements
            .Where(statement => statement.Extent.EndOffset <= declaration.Extent.StartOffset)
            .Where(statement => statement is not FunctionDefinitionAst && statement is not TrapStatementAst)
            .OrderBy(statement => statement.Extent.StartOffset)
            .FirstOrDefault();
        return transferStatement?.Extent.StartOffset;
    }

    private sealed class ScopeExecutionGraph
    {
        private readonly Dictionary<FunctionDefinitionAst, InvocationSite[]> _functionInvocations = new();

        internal ScopeExecutionGraph(NestedFunctionVisibilityAnalyzer analyzer, ScriptBlockAst scope)
        {
            var functions = scope.FindAll(
                    ast => ast is FunctionDefinitionAst,
                    searchNestedScriptBlocks: true)
                .Cast<FunctionDefinitionAst>()
                .Select(function => new
                {
                    Function = function,
                    HasContext = analyzer.TryGetPotentialDeclarationContext(function, out var declarationScope, out _),
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
            var commandName = command.GetCommandName();
            if (string.Equals(NormalizeInvocationName(commandName), "Invoke-Expression", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(commandName, "iex", StringComparison.OrdinalIgnoreCase))
            {
                Name = TryGetInvokeExpressionTarget(command);
                IsDynamic = string.IsNullOrWhiteSpace(Name);
            }
            else
            {
                Name = commandName;
                IsDynamic = string.IsNullOrWhiteSpace(Name) &&
                            command.CommandElements.Count > 0 &&
                            command.CommandElements[0] is not ScriptBlockExpressionAst;
            }
        }

        private static string? TryGetInvokeExpressionTarget(CommandAst command)
        {
            var arguments = command.CommandElements.Skip(1).ToArray();
            if (arguments.Length != 1 || arguments[0] is not StringConstantExpressionAst literal)
                return null;

            var parsed = Parser.ParseInput(literal.Value, out _, out var errors);
            if (errors.Length > 0)
                return null;

            var commands = parsed.FindAll(ast => ast is CommandAst, searchNestedScriptBlocks: true)
                .Cast<CommandAst>()
                .ToArray();
            return commands.Length == 1 ? commands[0].GetCommandName() : null;
        }

        internal CommandAst Command { get; }
        internal string? Name { get; }
        internal bool IsDynamic { get; }
        internal TrapStatementAst? Trap { get; }
    }
}
