using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkSummaryScriptTests
{
    [Fact]
    public void Comparison_ExposesManagedWarmMedianFromPostFirstIterations()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $rows = @(
                [pscustomobject]@{
                    Operation = 'Save'
                    Scenario = 'Warm'
                    Engine = 'Managed'
                    Iteration = 1
                    Status = 'Succeeded'
                    ElapsedMilliseconds = 900
                },
                [pscustomobject]@{
                    Operation = 'Save'
                    Scenario = 'Warm'
                    Engine = 'Managed'
                    Iteration = 2
                    Status = 'Succeeded'
                    ElapsedMilliseconds = 300
                },
                [pscustomobject]@{
                    Operation = 'Save'
                    Scenario = 'Warm'
                    Engine = 'Managed'
                    Iteration = 3
                    Status = 'Succeeded'
                    ElapsedMilliseconds = 500
                }
            )

            $summary = @(New-Summary -Rows $rows)
            [pscustomobject]@{
                Summary = $summary[0]
                Comparison = @(New-Comparison -SummaryRows $summary)[0]
            }
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var output = Assert.Single(results);
        var summary = (PSObject)output.Properties["Summary"].Value;
        var comparison = (PSObject)output.Properties["Comparison"].Value;
        Assert.Equal(500.0, NumericProperty(summary, "MedianMs"));
        Assert.Equal(2.0, NumericProperty(summary, "WarmRuns"));
        Assert.Equal(400.0, NumericProperty(summary, "WarmMedianMs"));
        Assert.Equal(300.0, NumericProperty(summary, "WarmMinMs"));
        Assert.Equal(500.0, NumericProperty(summary, "WarmMaxMs"));
        Assert.Equal(400.0, NumericProperty(comparison, "FastestWarmMedianMs"));
        Assert.Equal(2.0, NumericProperty(comparison, "ManagedWarmRuns"));
        Assert.Equal(400.0, NumericProperty(comparison, "ManagedWarmMedianMs"));
        Assert.Equal(300.0, NumericProperty(comparison, "ManagedWarmMinMs"));
        Assert.Equal(500.0, NumericProperty(comparison, "ManagedWarmMaxMs"));
    }

    [Fact]
    public void Comparison_ExposesManagedFirstAndLastCacheMetrics()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $rows = @(
                [pscustomobject]@{
                    Operation = 'Save'
                    Scenario = ''
                    Engine = 'Managed'
                    Iteration = 1
                    Status = 'Succeeded'
                    ElapsedMilliseconds = 4433.12
                    OutputFileCount = 482
                    OutputBytes = 1127000000
                    ManagedPackageCount = 78
                    ManagedDependencyCount = 77
                    ManagedUniquePackageCount = 40
                    ManagedUniqueDependencyCount = 39
                    ManagedInstalledPackageCount = 40
                    ManagedAlreadyInstalledPackageCount = 38
                    ManagedRootElapsedMilliseconds = 4200
                    ManagedHarnessOverheadMilliseconds = 233
                    ManagedRootDependencyMilliseconds = 3540
                    ManagedRootDependencyCriticalPathGapMilliseconds = 2760
                    ManagedDependencyBranchParallelismRatio = 1.5
                    ManagedTotalDownloadMilliseconds = 25000
                    ManagedTotalExtractionMilliseconds = 1200
                    ManagedTotalDependencyMilliseconds = 1600
                    ManagedTotalPromotionMilliseconds = 60
                    ManagedRepositoryRequestCount = 40
                    ManagedPackageRepositoryRequestCount = 40
                    ManagedPackageRepositoryRedirectCount = 0
                    ManagedDownloadBytes = 186506621
                    ManagedCacheHitCount = 0
                    ManagedExtractionCacheHitCount = 0
                    ManagedCoalescedWaitCount = 8
                    ManagedTotalCoalescedWaitMilliseconds = 900
                    ManagedSlowestCoalescedWaitName = 'Company.Wait'
                    ManagedSlowestCoalescedWaitMilliseconds = 250
                    ManagedInstallLockWaitCount = 4
                    ManagedTotalInstallLockWaitMilliseconds = 300
                    ManagedSlowestInstallLockWaitName = 'Company.Lock'
                    ManagedSlowestInstallLockWaitMilliseconds = 120
                    ManagedSlowestDependencyPackageName = 'Company.BranchCold'
                    ManagedSlowestDependencyPackageParent = 'Company.Root'
                    ManagedSlowestDependencyPackageMilliseconds = 700
                    ManagedSlowestMaterializedPackageName = 'Company.Big'
                    ManagedSlowestMaterializedPackageMilliseconds = 600
                    ManagedSlowestMaterializedPackageFileCount = 100
                    ManagedSlowestMaterializedPackageExtractedBytes = 104857600
                    ManagedSlowestMaterializedPackageMBPerSecond = 166.67
                    ManagedSlowestMaterializedPackageFilesPerSecond = 166.67
                    ManagedSlowestMaterializedPackageExtractionMilliseconds = 500
                    ManagedSlowestMaterializedPackagePromotionMilliseconds = 80
                    ManagedCriticalDependencyBranchName = 'Company.BranchCold'
                    ManagedCriticalDependencyBranchParent = 'Company.Root'
                    ManagedCriticalDependencyBranchMilliseconds = 780
                    ManagedCriticalDependencyBranchDominantPhase = 'Dependency'
                    ManagedCriticalDependencyBranchDominantPhaseMilliseconds = 700
                    ManagedCriticalRootBranchName = 'Company.Root'
                    ManagedCriticalRootBranchMilliseconds = 4200
                    ManagedCriticalRootBranchDominantPhase = 'Dependency'
                    ManagedCriticalRootBranchDominantPhaseMilliseconds = 3540
                    ManagedCriticalMaterializationBranchName = 'Company.Big'
                    ManagedCriticalMaterializationBranchMilliseconds = 560
                    ManagedCriticalMaterializationDominantPhase = 'Extraction'
                    ManagedCriticalMaterializationDominantPhaseMilliseconds = 500
                    ManagedAuthenticodeCheckedFileCount = 3
                    ManagedAuthenticodeCatalogFileCount = 1
                    ManagedMaintenanceActionCount = 0
                    ManagedMaintenanceFindingCount = 0
                },
                [pscustomobject]@{
                    Operation = 'Save'
                    Scenario = ''
                    Engine = 'Managed'
                    Iteration = 2
                    Status = 'Succeeded'
                    ElapsedMilliseconds = 1944.07
                    OutputFileCount = 482
                    OutputBytes = 1127000000
                    ManagedPackageCount = 78
                    ManagedDependencyCount = 77
                    ManagedUniquePackageCount = 40
                    ManagedUniqueDependencyCount = 39
                    ManagedInstalledPackageCount = 40
                    ManagedAlreadyInstalledPackageCount = 38
                    ManagedRootElapsedMilliseconds = 1773
                    ManagedHarnessOverheadMilliseconds = 171
                    ManagedRootDependencyMilliseconds = 1773
                    ManagedRootDependencyCriticalPathGapMilliseconds = 1253
                    ManagedDependencyBranchParallelismRatio = 2.0
                    ManagedTotalDownloadMilliseconds = 0
                    ManagedTotalExtractionMilliseconds = 1100
                    ManagedTotalDependencyMilliseconds = 900
                    ManagedTotalPromotionMilliseconds = 58
                    ManagedRepositoryRequestCount = 0
                    ManagedPackageRepositoryRequestCount = 0
                    ManagedPackageRepositoryRedirectCount = 0
                    ManagedDownloadBytes = 0
                    ManagedCacheHitCount = 40
                    ManagedExtractionCacheHitCount = 40
                    ManagedCoalescedWaitCount = 6
                    ManagedTotalCoalescedWaitMilliseconds = 700
                    ManagedSlowestCoalescedWaitName = 'Company.Shared'
                    ManagedSlowestCoalescedWaitMilliseconds = 180
                    ManagedInstallLockWaitCount = 2
                    ManagedTotalInstallLockWaitMilliseconds = 100
                    ManagedSlowestInstallLockWaitName = 'Company.LockWarm'
                    ManagedSlowestInstallLockWaitMilliseconds = 60
                    ManagedSlowestDependencyPackageName = 'Company.BranchWarm'
                    ManagedSlowestDependencyPackageParent = 'Company.Shared'
                    ManagedSlowestDependencyPackageMilliseconds = 500
                    ManagedSlowestMaterializedPackageName = 'Company.Files'
                    ManagedSlowestMaterializedPackageMilliseconds = 450
                    ManagedSlowestMaterializedPackageFileCount = 80
                    ManagedSlowestMaterializedPackageExtractedBytes = 52428800
                    ManagedSlowestMaterializedPackageMBPerSecond = 111.11
                    ManagedSlowestMaterializedPackageFilesPerSecond = 177.78
                    ManagedSlowestMaterializedPackageExtractionMilliseconds = 320
                    ManagedSlowestMaterializedPackagePromotionMilliseconds = 70
                    ManagedCriticalDependencyBranchName = 'Company.BranchWarm'
                    ManagedCriticalDependencyBranchParent = 'Company.Shared'
                    ManagedCriticalDependencyBranchMilliseconds = 520
                    ManagedCriticalDependencyBranchDominantPhase = 'Dependency'
                    ManagedCriticalDependencyBranchDominantPhaseMilliseconds = 500
                    ManagedCriticalRootBranchName = 'Company.Root'
                    ManagedCriticalRootBranchMilliseconds = 1773
                    ManagedCriticalRootBranchDominantPhase = 'Dependency'
                    ManagedCriticalRootBranchDominantPhaseMilliseconds = 1773
                    ManagedCriticalMaterializationBranchName = 'Company.Files'
                    ManagedCriticalMaterializationBranchMilliseconds = 390
                    ManagedCriticalMaterializationDominantPhase = 'Extraction'
                    ManagedCriticalMaterializationDominantPhaseMilliseconds = 320
                    ManagedAuthenticodeCheckedFileCount = 5
                    ManagedAuthenticodeCatalogFileCount = 1
                    ManagedMaintenanceActionCount = 0
                    ManagedMaintenanceFindingCount = 0
                }
            )

            $summary = @(New-Summary -Rows $rows)
            New-Comparison -SummaryRows $summary
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(results);
        Assert.Equal(1.0, NumericProperty(row, "ManagedFirstIteration"));
        Assert.Equal(2.0, NumericProperty(row, "ManagedLastIteration"));
        Assert.Equal(4433.12, NumericProperty(row, "ManagedFirstMs"));
        Assert.Equal(1944.07, NumericProperty(row, "ManagedLastMs"));
        Assert.Equal(40.0, NumericProperty(row, "ManagedFirstRepositoryRequests"));
        Assert.Equal(0.0, NumericProperty(row, "ManagedLastRepositoryRequests"));
        Assert.Equal(40.0, NumericProperty(row, "ManagedFirstPackageRepositoryRequests"));
        Assert.Equal(0.0, NumericProperty(row, "ManagedLastPackageRepositoryRequests"));
        Assert.Equal(3540.0, NumericProperty(row, "ManagedFirstRootDependencyMs"));
        Assert.Equal(1773.0, NumericProperty(row, "ManagedLastRootDependencyMs"));
        Assert.Equal(2760.0, NumericProperty(row, "ManagedFirstRootDependencyCriticalPathGapMs"));
        Assert.Equal(1253.0, NumericProperty(row, "ManagedLastRootDependencyCriticalPathGapMs"));
        Assert.Equal(1.5, NumericProperty(row, "ManagedFirstDependencyBranchParallelismRatio"));
        Assert.Equal(2.0, NumericProperty(row, "ManagedLastDependencyBranchParallelismRatio"));
        Assert.Equal(25000.0, NumericProperty(row, "ManagedFirstDownloadMs"));
        Assert.Equal(0.0, NumericProperty(row, "ManagedLastDownloadMs"));
        Assert.Equal(1200.0, NumericProperty(row, "ManagedFirstExtractionMs"));
        Assert.Equal(1100.0, NumericProperty(row, "ManagedLastExtractionMs"));
        Assert.Equal(1250.0, NumericProperty(row, "ManagedDependencyMs"));
        Assert.Equal(1600.0, NumericProperty(row, "ManagedFirstDependencyMs"));
        Assert.Equal(900.0, NumericProperty(row, "ManagedLastDependencyMs"));
        Assert.Equal(60.0, NumericProperty(row, "ManagedFirstPromotionMs"));
        Assert.Equal(58.0, NumericProperty(row, "ManagedLastPromotionMs"));
        Assert.Equal(186506621.0, NumericProperty(row, "ManagedFirstDownloadBytes"));
        Assert.Equal(0.0, NumericProperty(row, "ManagedLastDownloadBytes"));
        Assert.Equal(0.0, NumericProperty(row, "ManagedFirstCacheHits"));
        Assert.Equal(40.0, NumericProperty(row, "ManagedLastCacheHits"));
        Assert.Equal(800.0, NumericProperty(row, "ManagedCoalescedWaitMs"));
        Assert.Equal(900.0, NumericProperty(row, "ManagedFirstCoalescedWaitMs"));
        Assert.Equal(700.0, NumericProperty(row, "ManagedLastCoalescedWaitMs"));
        Assert.Equal("Company.Shared", Property(row, "ManagedLastSlowestCoalescedWaitName"));
        Assert.Equal(180.0, NumericProperty(row, "ManagedLastSlowestCoalescedWaitMs"));
        Assert.Equal(200.0, NumericProperty(row, "ManagedInstallLockWaitMs"));
        Assert.Equal(300.0, NumericProperty(row, "ManagedFirstInstallLockWaitMs"));
        Assert.Equal(100.0, NumericProperty(row, "ManagedLastInstallLockWaitMs"));
        Assert.Equal("Company.LockWarm", Property(row, "ManagedLastSlowestInstallLockWaitName"));
        Assert.Equal(60.0, NumericProperty(row, "ManagedLastSlowestInstallLockWaitMs"));
        Assert.Equal("Company.BranchWarm", Property(row, "ManagedLastSlowestDependencyPackageName"));
        Assert.Equal("Company.Shared", Property(row, "ManagedLastSlowestDependencyPackageParent"));
        Assert.Equal(500.0, NumericProperty(row, "ManagedLastSlowestDependencyPackageMs"));
        Assert.Equal("Company.Files", Property(row, "ManagedLastSlowestMaterializedPackageName"));
        Assert.Equal(450.0, NumericProperty(row, "ManagedLastSlowestMaterializedPackageMs"));
        Assert.Equal(90.0, NumericProperty(row, "ManagedSlowestMaterializedPackageFileCount"));
        Assert.Equal(78643200.0, NumericProperty(row, "ManagedSlowestMaterializedPackageExtractedBytes"));
        Assert.Equal(138.89, NumericProperty(row, "ManagedSlowestMaterializedPackageMBPerSecond"));
        Assert.Equal(172.22, NumericProperty(row, "ManagedSlowestMaterializedPackageFilesPerSecond"));
        Assert.Equal(80.0, NumericProperty(row, "ManagedLastSlowestMaterializedPackageFileCount"));
        Assert.Equal(52428800.0, NumericProperty(row, "ManagedLastSlowestMaterializedPackageExtractedBytes"));
        Assert.Equal(111.11, NumericProperty(row, "ManagedLastSlowestMaterializedPackageMBPerSecond"));
        Assert.Equal(177.78, NumericProperty(row, "ManagedLastSlowestMaterializedPackageFilesPerSecond"));
        Assert.Equal(320.0, NumericProperty(row, "ManagedLastSlowestMaterializedPackageExtractionMs"));
        Assert.Equal(70.0, NumericProperty(row, "ManagedLastSlowestMaterializedPackagePromotionMs"));
        Assert.Equal(650.0, NumericProperty(row, "ManagedCriticalDependencyBranchMs"));
        Assert.Equal(2986.5, NumericProperty(row, "ManagedCriticalRootBranchMs"));
        Assert.Equal(475.0, NumericProperty(row, "ManagedCriticalMaterializationBranchMs"));
        Assert.Equal(2006.5, NumericProperty(row, "ManagedRootDependencyCriticalPathGapMs"));
        Assert.Equal(1.75, NumericProperty(row, "ManagedDependencyBranchParallelismRatio"));
        Assert.Equal("Company.BranchWarm", Property(row, "ManagedLastCriticalDependencyBranchName"));
        Assert.Equal("Company.Shared", Property(row, "ManagedLastCriticalDependencyBranchParent"));
        Assert.Equal(520.0, NumericProperty(row, "ManagedLastCriticalDependencyBranchMs"));
        Assert.Equal("Dependency", Property(row, "ManagedLastCriticalDependencyBranchDominantPhase"));
        Assert.Equal(500.0, NumericProperty(row, "ManagedLastCriticalDependencyBranchDominantPhaseMs"));
        Assert.Equal("Company.Root", Property(row, "ManagedLastCriticalRootBranchName"));
        Assert.Equal(1773.0, NumericProperty(row, "ManagedLastCriticalRootBranchMs"));
        Assert.Equal("Dependency", Property(row, "ManagedLastCriticalRootBranchDominantPhase"));
        Assert.Equal(1773.0, NumericProperty(row, "ManagedLastCriticalRootBranchDominantPhaseMs"));
        Assert.Equal("Company.Files", Property(row, "ManagedLastCriticalMaterializationBranchName"));
        Assert.Equal(390.0, NumericProperty(row, "ManagedLastCriticalMaterializationBranchMs"));
        Assert.Equal("Extraction", Property(row, "ManagedLastCriticalMaterializationDominantPhase"));
        Assert.Equal(320.0, NumericProperty(row, "ManagedLastCriticalMaterializationDominantPhaseMs"));
        Assert.Equal(4.0, NumericProperty(row, "ManagedAuthenticodeCheckedFiles"));
        Assert.Equal(1.0, NumericProperty(row, "ManagedAuthenticodeCatalogFiles"));
    }

    private static PowerShell CreateBenchmarkPowerShell(string script)
    {
        var root = RepoRootLocator.Find();
        var summaryScript = Path.Combine(root, "Benchmarks", "ManagedModules", "ManagedModuleBenchmark.Summary.ps1");
        var ps = PowerShell.Create();
        ps.AddScript(File.ReadAllText(summaryScript) + Environment.NewLine + script);
        return ps;
    }

    private static double NumericProperty(PSObject value, string name)
        => Convert.ToDouble(value.Properties[name].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static string Property(PSObject value, string name)
        => (string)value.Properties[name].Value;

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(static error => error.ToString()));
        Assert.Fail(message);
    }
}
