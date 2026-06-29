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
                     "ManagedFirstDependencyMs",
                     "ManagedLastDependencyMs",
                     "ManagedFirstPromotionMs",
                     "ManagedLastPromotionMs",
                     "ManagedFirstCoalescedWaitMs",
                     "ManagedLastCoalescedWaitMs",
                     "ManagedFirstInstallLockWaitMs",
                     "ManagedLastInstallLockWaitMs",
                     "ManagedLastSlowestInstallLockWaitName",
                     "ManagedLastSlowestDependencyPackageName",
                     "ManagedLastSlowestDependencyPackageParent",
                     "ManagedLastSlowestDependencyPackageMs",
                     "ManagedLastSlowestMaterializedPackageMs"
                 })
        {
            Assert.Contains(field + " = $row." + field, script, StringComparison.Ordinal);
        }
    }
}
