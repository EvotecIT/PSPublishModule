namespace PowerForge;

/// <summary>Shares literal-body reachability between code generation and evidence collectors.</summary>
internal static class PowerShellLoweredScriptBlockClosure
{
    internal static IEnumerable<PowerShellLoweredNativeScriptBlockExpression> DirectBlocks(PowerShellLoweredFunction function)
        => PowerShellLoweredTreeEnumerator.EnumerateExpressions(function.Statements)
            .OfType<PowerShellLoweredNativeScriptBlockExpression>()
            .GroupBy(static block => block.Target.StableKey, StringComparer.Ordinal).Select(static group => group.First())
            .OrderBy(static block => block.Span.StartOffset);

    internal static IEnumerable<PowerShellLoweredFunction> Enumerate(PowerShellLoweredFunction root,
        IReadOnlyDictionary<string, PowerShellLoweredFunction> functions)
    {
        var pending = new Stack<PowerShellLoweredFunction>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(root);
        while (pending.Count > 0)
        {
            var function = pending.Pop();
            if (!visited.Add(function.Symbol.StableKey)) continue;
            yield return function;
            foreach (var block in DirectBlocks(function).Reverse()) pending.Push(functions[block.Target.StableKey]);
        }
    }
}
