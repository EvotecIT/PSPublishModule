using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class NestedFunctionVisibilityAnalyzer
{
    private static bool IsWithinExecutionScope(CommandAst command, ScriptBlockAst scope)
    {
        for (Ast? current = command.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, scope))
                return true;
            if (current is FunctionDefinitionAst)
                return false;
        }

        return false;
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
                .Where(command => IsWithinExecutionScope(command, scope))
                .Select(command => new InvocationSite(command, FindContainingTrap(command, scope)))
                .ToArray();

            foreach (var function in functions)
            {
                _functionInvocations[function] = function.Body.FindAll(
                    ast => ast is CommandAst,
                    searchNestedScriptBlocks: true)
                    .Cast<CommandAst>()
                    .Where(command => IsWithinExecutionScope(command, function.Body))
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
