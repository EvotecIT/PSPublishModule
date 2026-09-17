using System;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class NestedFunctionVisibilityAnalyzer
{
    private bool IsDeclarationQualifierVisibleAtCommand(
        FunctionDefinitionAst declaration,
        CommandAst command)
    {
        if (!declaration.Name.StartsWith("private:", StringComparison.OrdinalIgnoreCase))
            return true;

        return TryGetPotentialDeclarationContext(declaration, out var declarationScope, out _) &&
               ReferenceEquals(declarationScope, FindContainingScriptBlock(command));
    }

    private bool IsRemovedBeforeCommand(FunctionDefinitionAst declaration, CommandAst command)
    {
        if (!TryGetPotentialDeclarationContext(declaration, out var declarationScope, out _))
            return false;

        var name = NormalizeDeclaredFunctionName(declaration.Name);
        if (!_functionRemovalsByName.TryGetValue(name, out var removals))
            return false;

        return removals.Any(removal =>
            removal.Extent.StartOffset >= declaration.Extent.EndOffset &&
            removal.Extent.EndOffset <= command.Extent.StartOffset &&
            ReferenceEquals(FindEffectiveCommandScope(removal), declarationScope) &&
            RemovalDominatesCommand(removal, command, declarationScope));
    }

    private bool IsRemovedBeforeExecution(
        FunctionDefinitionAst declaration,
        int executionOffset,
        ScriptBlockAst declarationScope,
        CommandAst? executionCommand = null)
    {
        var name = NormalizeDeclaredFunctionName(declaration.Name);
        if (!_functionRemovalsByName.TryGetValue(name, out var removals))
            return false;

        return removals.Any(removal =>
            removal.Extent.StartOffset >= declaration.Extent.EndOffset &&
            removal.Extent.EndOffset <= executionOffset &&
            ReferenceEquals(FindEffectiveCommandScope(removal), declarationScope) &&
            (IsGuaranteedCommandInScope(removal, declarationScope) ||
             executionCommand is not null &&
             RemovalDominatesCommand(removal, executionCommand, declarationScope)));
    }

    private bool RemovalDominatesCommand(
        CommandAst removal,
        CommandAst command,
        ScriptBlockAst declarationScope)
    {
        return CommandDominatesCommandInEffectiveScope(removal, command, declarationScope);
    }

    private static bool IsDirectScopeCommand(CommandAst command, ScriptBlockAst scope)
    {
        return command.Parent is PipelineAst pipeline &&
               pipeline.Parent is NamedBlockAst block &&
               ReferenceEquals(block.Parent, scope);
    }
}
