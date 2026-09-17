using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class NestedFunctionVisibilityAnalyzer
{
    private static bool DeclarationDominatesFollowingTryCall(
        FunctionDefinitionAst declaration,
        CommandAst command)
    {
        if (declaration.Parent is not StatementBlockAst tryBody ||
            tryBody.Parent is not TryStatementAst tryStatement ||
            !ReferenceEquals(tryStatement.Body, tryBody) ||
            !DeclarationDominatesBlockExit(declaration, tryBody))
        {
            return false;
        }

        return StatementDominatesFollowingCommand(tryStatement, command);
    }

    private static bool DeclarationDominatesFollowingDoLoopCall(
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

        return StatementDominatesFollowingCommand(loop, command);
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
            !DeclarationDominatesPromotedBlockExit(declaration))
        {
            return false;
        }

        var graph = GetExecutionGraph(_root);
        var rootInvocations = graph.RootInvocations
            .Where(invocation => IsDirectRootInvocation(invocation.Command))
            .OrderBy(invocation => invocation.Command.Extent.StartOffset)
            .ToArray();
        var containingFunction = FindContainingFunction(command);
        var consumerOffsets = containingFunction is null
            ? new[] { command.Extent.StartOffset }
            : rootInvocations
                .Where(invocation => InvocationCanReachFunction(invocation, containingFunction, graph))
                .Select(invocation => invocation.Command.Extent.StartOffset)
                .ToArray();

        foreach (var consumerOffset in consumerOffsets)
        {
            if (rootInvocations.Any(invocation =>
                    invocation.Command.Extent.EndOffset <= consumerOffset &&
                    InvocationCanReachFunction(invocation, installer, graph)))
            {
                return true;
            }
        }

        return false;
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
        ScopeExecutionGraph graph)
    {
        var executionOffset = entry.Command.Extent.StartOffset;
        var queue = new Queue<(FunctionDefinitionAst Function, int InvocationOffset)>();
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
                if (!ExecutesWhileScopeInitializes(invocation.Command, item.Function.Body))
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
}
