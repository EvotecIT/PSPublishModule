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
}
