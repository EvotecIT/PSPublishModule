namespace PowerForge;

internal sealed partial class PowerShellSemanticAnalyzer
{
    private static bool IsRecursive(PowerShellSymbolId start, IReadOnlyList<PowerShellCallGraphEdge> edges)
        => ReachesStartThroughCallGraph(start, edges, includeDirectSelfEdge: true);

    private static bool IsMutuallyRecursive(PowerShellSymbolId start, IReadOnlyList<PowerShellCallGraphEdge> edges)
        => ReachesStartThroughCallGraph(start, edges, includeDirectSelfEdge: false);

    private static bool ReachesStartThroughCallGraph(
        PowerShellSymbolId start,
        IReadOnlyList<PowerShellCallGraphEdge> edges,
        bool includeDirectSelfEdge)
    {
        var targets = edges.GroupBy(static edge => edge.Caller.StableKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static edge => edge.Callee.StableKey).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (targets.TryGetValue(start.StableKey, out var direct))
        {
            foreach (var target in direct.Where(target => includeDirectSelfEdge || !target.Equals(start.StableKey, StringComparison.Ordinal)))
                pending.Push(target);
        }
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current.Equals(start.StableKey, StringComparison.Ordinal)) return true;
            if (!visited.Add(current) || !targets.TryGetValue(current, out var nested)) continue;
            foreach (var target in nested) pending.Push(target);
        }
        return false;
    }
}
