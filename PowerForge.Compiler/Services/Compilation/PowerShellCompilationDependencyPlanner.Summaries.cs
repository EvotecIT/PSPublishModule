namespace PowerForge;

public sealed partial class PowerShellCompilationDependencyPlanner
{
    /// <summary>Aggregates a detailed dependency inventory for census and dashboard output.</summary>
    public static PowerShellCompilationDependencySummary[] Summarize(IEnumerable<PowerShellCompilationDependency> dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        return dependencies
            .GroupBy(static dependency => new { dependency.Kind, dependency.Disposition })
            .Select(static group => new PowerShellCompilationDependencySummary(
                group.Key.Kind,
                group.Key.Disposition,
                group.Count(),
                group.Count(static dependency => dependency.Disposition == PowerShellCompilationDependencyDisposition.Missing),
                group.Sum(static dependency => dependency.SizeBytes)))
            .OrderBy(static summary => summary.Kind)
            .ThenBy(static summary => summary.Disposition)
            .ToArray();
    }

    /// <summary>Summarizes selected and unselected local resource payload.</summary>
    public static PowerShellCompilationResourceSummary SummarizeResources(IEnumerable<PowerShellCompilationDependency> dependencies)
        => PowerShellCompilationResourceSummary.Create(dependencies);
}
