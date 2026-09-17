using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class NestedFunctionVisibilityAnalyzer
{
    private bool DeclarationDominatesFollowingTryCall(
        FunctionDefinitionAst declaration,
        CommandAst command)
    {
        if (declaration.Parent is not StatementBlockAst declarationBlock ||
            declarationBlock.Parent is not TryStatementAst tryStatement ||
            (!ReferenceEquals(tryStatement.Body, declarationBlock) &&
             !ReferenceEquals(tryStatement.Finally, declarationBlock)) ||
            !DeclarationDominatesBlockExit(declaration, declarationBlock))
        {
            return false;
        }

        if (TryStatementCanRemoveFunction(tryStatement, declaration))
            return false;

        return StatementDominatesFollowingCommand(tryStatement, command);
    }

    private bool DeclarationDominatesFollowingDoLoopCall(
        FunctionDefinitionAst declaration,
        CommandAst command)
    {
        if (declaration.Parent is not StatementBlockAst loopBody ||
            loopBody.Parent is not LoopStatementAst loop ||
            loop is not DoWhileStatementAst && loop is not DoUntilStatementAst ||
            !DeclarationDominatesBlockExit(declaration, loopBody))
        {
            return false;
        }

        if (BlockCanRemoveFunctionAfterDeclaration(loopBody, declaration))
            return false;

        return StatementDominatesFollowingCommand(loop, command);
    }

    private bool TryStatementCanRemoveFunction(
        TryStatementAst tryStatement,
        FunctionDefinitionAst declaration)
    {
        return TryBodyAlwaysThrowsAfterDeclaration(tryStatement.Body, declaration) &&
               tryStatement.CatchClauses.Any(catchClause =>
                   BlockCanRemoveFunctionAfterDeclaration(catchClause.Body, declaration)) ||
               tryStatement.Finally is not null &&
               BlockCanRemoveFunctionAfterDeclaration(tryStatement.Finally, declaration);
    }

    private static bool TryBodyAlwaysThrowsAfterDeclaration(
        StatementBlockAst block,
        FunctionDefinitionAst declaration)
    {
        return block.Statements
            .Where(statement => statement.Extent.StartOffset >= declaration.Extent.EndOffset)
            .FirstOrDefault(statement => statement is not FunctionDefinitionAst && statement is not TrapStatementAst)
            is ThrowStatementAst;
    }

    private bool BlockCanRemoveFunctionAfterDeclaration(
        StatementBlockAst block,
        FunctionDefinitionAst declaration)
    {
        var functionName = NormalizeDeclaredFunctionName(declaration.Name);
        if (!_functionRemovalsByName.TryGetValue(functionName, out var removals))
            return false;

        return removals.Any(removal =>
            removal.Extent.StartOffset >= declaration.Extent.EndOffset &&
            removal.Parent is PipelineAst pipeline &&
            ReferenceEquals(pipeline.Parent, block) &&
            block.Statements
                .Where(statement => statement.Extent.EndOffset <= removal.Extent.StartOffset)
                .All(statement => statement is FunctionDefinitionAst || statement is TrapStatementAst));
    }

    private static bool DeclarationDominatesBlockExit(
        FunctionDefinitionAst declaration,
        StatementBlockAst statementBlock)
    {
        return statementBlock.Statements
            .Where(statement => statement.Extent.EndOffset <= declaration.Extent.StartOffset)
            .All(statement => statement is FunctionDefinitionAst || statement is TrapStatementAst);
    }

    private static bool StatementDominatesFollowingCommand(StatementAst statement, CommandAst command)
    {
        if (statement.Parent is not NamedBlockAst containingBlock ||
            statement.Extent.EndOffset > command.Extent.StartOffset)
        {
            return false;
        }

        for (Ast? current = command.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, containingBlock))
                return true;
            if (current is FunctionDefinitionAst || current is ScriptBlockExpressionAst)
                return false;
        }

        return false;
    }

    private bool IsQualifiedDeclarationInstalledBeforeCommand(
        FunctionDefinitionAst declaration,
        CommandAst command)
    {
        if (!HasEffectiveRootQualifier(declaration) ||
            FindContainingFunction(declaration) is not { } installer ||
            !DeclarationDominatesInstallerExit(declaration, installer))
        {
            return false;
        }

        var graph = GetExecutionGraph(_root);
        var guaranteedRootInvocations = graph.RootInvocations
            .Where(invocation => IsDirectRootInvocation(invocation.Command))
            .OrderBy(invocation => invocation.Command.Extent.StartOffset)
            .ToArray();
        var possibleRootInvocations = graph.RootInvocations
            .Where(invocation => ExecutesWhileScopeInitializes(invocation.Command, _root))
            .OrderBy(invocation => invocation.Command.Extent.StartOffset)
            .ToArray();
        var containingFunction = FindContainingFunction(command);
        var consumerOffsets = containingFunction is null
            ? new[] { command.Extent.StartOffset }
            : possibleRootInvocations
                .Where(invocation => InvocationCanReachFunction(
                    invocation,
                    containingFunction,
                    graph,
                    requireGuaranteedPath: false))
                .Select(invocation => invocation.Command.Extent.StartOffset)
                .ToArray();

        return consumerOffsets.Length > 0 && consumerOffsets.All(consumerOffset =>
            IsQualifiedDeclarationInstalledForConsumer(
                declaration,
                installer,
                consumerOffset,
                guaranteedRootInvocations,
                graph));
    }

    private static bool DeclarationDominatesInstallerExit(
        FunctionDefinitionAst declaration,
        FunctionDefinitionAst installer)
    {
        if (DeclarationDominatesPromotedBlockExit(declaration))
            return true;
        if (declaration.Parent is not StatementBlockAst finallyBlock ||
            finallyBlock.Parent is not TryStatementAst tryStatement ||
            !ReferenceEquals(tryStatement.Finally, finallyBlock) ||
            !DeclarationDominatesBlockExit(declaration, finallyBlock) ||
            tryStatement.Parent is not NamedBlockAst installerBlock ||
            !ReferenceEquals(installerBlock.Parent, installer.Body))
        {
            return false;
        }

        return installerBlock.Statements
            .Where(statement => statement.Extent.EndOffset <= tryStatement.Extent.StartOffset)
            .All(statement => statement is FunctionDefinitionAst || statement is TrapStatementAst);
    }

    private bool IsQualifiedDeclarationInstalledForConsumer(
        FunctionDefinitionAst declaration,
        FunctionDefinitionAst installer,
        int consumerOffset,
        IEnumerable<InvocationSite> guaranteedRootInvocations,
        ScopeExecutionGraph graph)
    {
        var latestRemovalOffset = -1;
        var functionName = NormalizeDeclaredFunctionName(declaration.Name);
        if (_functionRemovalsByName.TryGetValue(functionName, out var removals))
        {
            latestRemovalOffset = removals
                .Where(removal =>
                    removal.Extent.EndOffset <= consumerOffset &&
                    ReferenceEquals(FindEffectiveCommandScope(removal), _root) &&
                    IsGuaranteedCommandInScope(removal, _root))
                .Select(removal => removal.Extent.EndOffset)
                .DefaultIfEmpty(-1)
                .Max();
        }

        return guaranteedRootInvocations.Any(invocation =>
            invocation.Command.Extent.StartOffset >= latestRemovalOffset &&
            invocation.Command.Extent.EndOffset <= consumerOffset &&
            InvocationCanReachFunction(
                invocation,
                installer,
                graph,
                requireGuaranteedPath: true));
    }

    private static bool HasEffectiveRootQualifier(FunctionDefinitionAst declaration)
    {
        var separator = declaration.Name.IndexOf(':');
        if (separator <= 0)
            return false;

        var qualifier = declaration.Name.Substring(0, separator);
        return string.Equals(qualifier, "global", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(qualifier, "script", StringComparison.OrdinalIgnoreCase);
    }

    private static FunctionDefinitionAst? FindContainingFunction(Ast ast)
    {
        for (var current = ast.Parent; current is not null; current = current.Parent)
        {
            if (current is FunctionDefinitionAst function)
                return function;
        }

        return null;
    }

    private bool IsDirectRootInvocation(CommandAst command)
    {
        return command.Parent is PipelineAst pipeline &&
               pipeline.Parent is NamedBlockAst block &&
               ReferenceEquals(block.Parent, _root);
    }

    private bool InvocationCanReachFunction(
        InvocationSite entry,
        FunctionDefinitionAst target,
        ScopeExecutionGraph graph,
        bool requireGuaranteedPath)
    {
        var executionOffset = entry.Command.Extent.StartOffset;
        if (requireGuaranteedPath && !HasDeterministicInvocationTarget(entry, executionOffset))
            return false;

        var queue = new Queue<(FunctionDefinitionAst Function, int InvocationOffset, CommandAst InvocationCommand)>();
        var visited = new HashSet<FunctionDefinitionAst>();
        EnqueueAvailableFunctions(entry, executionOffset, graph.FunctionsByName, _root, queue);

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            if (!visited.Add(item.Function))
                continue;
            if (ReferenceEquals(item.Function, target))
                return true;

            foreach (var invocation in graph.GetFunctionInvocations(item.Function))
            {
                if (requireGuaranteedPath
                        ? !IsGuaranteedFunctionBodyInvocation(invocation.Command, item.Function)
                        : !ExecutesWhileScopeInitializes(invocation.Command, item.Function.Body))
                    continue;
                if (requireGuaranteedPath &&
                    !HasDeterministicInvocationTarget(invocation, item.InvocationOffset))
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
                    _root,
                    queue);
            }
        }

        return false;
    }

    private static bool IsGuaranteedFunctionBodyInvocation(
        CommandAst command,
        FunctionDefinitionAst function)
    {
        if (command.Parent is not PipelineAst pipeline ||
            pipeline.Parent is not NamedBlockAst block ||
            !ReferenceEquals(block.Parent, function.Body))
        {
            return false;
        }

        return block.Statements
            .Where(statement => statement.Extent.EndOffset <= pipeline.Extent.StartOffset)
            .All(statement => statement is FunctionDefinitionAst || statement is TrapStatementAst);
    }

    private bool HasDeterministicInvocationTarget(InvocationSite invocation, int executionOffset)
    {
        if (invocation.IsDynamic ||
            HasDynamicAliasDeclarationBefore(invocation, executionOffset) ||
            HasDynamicAliasTargetBefore(invocation, executionOffset))
        {
            return false;
        }

        return ResolveInvocationNames(invocation, executionOffset).Count == 1;
    }
}
