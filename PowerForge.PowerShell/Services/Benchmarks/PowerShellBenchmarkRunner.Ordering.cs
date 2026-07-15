namespace PowerForge;

public sealed partial class PowerShellBenchmarkRunner
{
    private static IEnumerable<PowerShellBenchmarkWorkItem> OrderWorkItems(
        IReadOnlyList<PowerShellBenchmarkWorkItem> items,
        int iteration,
        PowerShellBenchmarkRunOrder order)
        => order switch
        {
            PowerShellBenchmarkRunOrder.Sequential => items,
            PowerShellBenchmarkRunOrder.Randomized => Randomize(items, iteration),
            _ => RotateComparisonGroups(items, iteration)
        };

    private static IEnumerable<PowerShellBenchmarkWorkItem> RotateComparisonGroups(
        IReadOnlyList<PowerShellBenchmarkWorkItem> items,
        int iteration)
    {
        if (items.Count == 0)
            yield break;

        var groups = GroupComparisonWorkItems(items);
        var groupOffset = iteration % groups.Length;

        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var group = groups[(groupOffset + groupIndex) % groups.Length];
            foreach (var item in Rotate(group, iteration))
                yield return item;
        }
    }

    private static PowerShellBenchmarkWorkItem[][] GroupComparisonWorkItems(
        IReadOnlyList<PowerShellBenchmarkWorkItem> items)
        => items
            .GroupBy(item => ComparisonGroupKey(string.Empty, item), StringComparer.Ordinal)
            .Select(group => group.ToArray())
            .ToArray();

    private static IEnumerable<PowerShellBenchmarkWorkItem> Rotate(
        IReadOnlyList<PowerShellBenchmarkWorkItem> items,
        int iteration)
    {
        if (items.Count == 0)
            yield break;

        var itemOffset = iteration % items.Count;
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            yield return items[(itemOffset + itemIndex) % items.Count];
    }

    private static IEnumerable<PowerShellBenchmarkWorkItem> Randomize(
        IReadOnlyList<PowerShellBenchmarkWorkItem> items,
        int iteration)
    {
        var ordered = items.ToArray();
        var random = new Random(unchecked((iteration + 1) * 397) ^ ordered.Length);
        for (var i = ordered.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
        }

        return ordered;
    }
}
