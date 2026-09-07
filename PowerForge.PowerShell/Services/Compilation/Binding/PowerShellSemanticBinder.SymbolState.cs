using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private static Dictionary<string, PowerShellSemanticSymbolBinding> CloneSymbols(
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols)
        => symbols.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);

    private static void MergeSymbolValueStates(
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> destination,
        params IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding>[] paths)
    {
        foreach (var pair in destination)
        {
            var bindings = paths
                .Where(path => path.ContainsKey(pair.Key))
                .Select(path => path[pair.Key])
                .ToArray();
            pair.Value.MergeFlowState(bindings);
        }
    }

    private static void ForgetTryMutationsOnCatchEntry(
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        System.Management.Automation.Language.StatementBlockAst tryBody)
    {
        foreach (var assignment in tryBody
                     .FindAll(static node => node is System.Management.Automation.Language.AssignmentStatementAst, searchNestedScriptBlocks: false)
                     .Cast<System.Management.Automation.Language.AssignmentStatementAst>())
        {
            var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left);
            if (variable is not null && symbols.TryGetValue(variable.VariablePath.UserPath, out var binding))
                binding.ForgetValueState();
        }
    }

    private static void PrepareLoopFlowState(
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        PowerShellCompilationCapability capabilities,
        params Ast?[] regions)
    {
        // The body is emitted once and reused for every iteration. Entry facts
        // about a written variable do not establish facts at the next backedge.
        // Keep immutable CLR representation/type constraints, but discard these
        // value facts until a statement in the body proves them again.
        foreach (var region in regions)
        {
            if (region is null) continue;
            foreach (var write in region.FindAll(
                         static node => node is AssignmentStatementAst or ForEachStatementAst ||
                                        node is UnaryExpressionAst { TokenKind: TokenKind.PlusPlus or TokenKind.PostfixPlusPlus or TokenKind.MinusMinus or TokenKind.PostfixMinusMinus },
                         searchNestedScriptBlocks: false))
            {
                var variable = write switch
                {
                    AssignmentStatementAst assignment => PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left),
                    ForEachStatementAst loop => loop.Variable,
                    UnaryExpressionAst unary => PowerShellAssignmentTargetPolicy.FindDirectVariable(unary.Child),
                    _ => null
                };
                if (variable is not null && symbols.TryGetValue(variable.VariablePath.UserPath, out var binding))
                    binding.ForgetValueState();
            }
        }
        PowerShellModuleStateOriginPolicy.PropagateLoopCarriedOrigins(symbols, functions, capabilities, regions);
    }
}
