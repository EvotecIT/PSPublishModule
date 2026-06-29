namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkSuiteAggregationScriptTests
{
    [Fact]
    public void SuiteRunner_PreservesFirstAndLastManagedPhaseMetrics()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoRootLocator.Find(),
            "Benchmarks",
            "ManagedModules",
            "Invoke-ManagedModuleBenchmarkSuite.ps1"));

        foreach (var field in new[]
                 {
                     "ManagedFirstRootDependencyMs",
                     "ManagedLastRootDependencyMs",
                     "ManagedFirstDownloadMs",
                     "ManagedLastDownloadMs",
                     "ManagedFirstExtractionMs",
                     "ManagedLastExtractionMs",
                     "ManagedFirstPromotionMs",
                     "ManagedLastPromotionMs",
                     "ManagedFirstCoalescedWaitMs",
                     "ManagedLastCoalescedWaitMs",
                     "ManagedLastSlowestMaterializedPackageMs"
                 })
        {
            Assert.Contains(field + " = $row." + field, script, StringComparison.Ordinal);
        }
    }
}
