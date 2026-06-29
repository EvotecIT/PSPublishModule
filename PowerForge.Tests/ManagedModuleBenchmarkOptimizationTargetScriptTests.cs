using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkOptimizationTargetScriptTests
{
    [Fact]
    public void OptimizationTarget_IdentifiesDownloadBottleneck()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $rows = @(
                [pscustomobject]@{
                    Suite = 'SaveGate'
                    Scenario = 'Graph.Authentication.Save'
                    ModuleName = 'Microsoft.Graph.Authentication'
                    Host = 'PowerShell7'
                    Operation = 'Save'
                    ManagedMs = '1000'
                    ManagedRank = '1'
                    ManagedVsFastest = '1x'
                    ManagedOutputBytes = '10485760'
                    ManagedOutputFileCount = '20'
                    ManagedRootElapsedMs = '900'
                    ManagedHarnessOverheadMs = '100'
                    ManagedRootDependencyMs = '0'
                    ManagedDependencyMs = '300'
                    ManagedDownloadMs = '600'
                    ManagedExtractionMs = '150'
                    ManagedPromotionMs = '25'
                    ManagedRepositoryRequests = '10'
                    ManagedPackageRepositoryRequests = '8'
                    ManagedPackageRepositoryRedirects = '4'
                    ManagedDownloadBytes = '2097152'
                    ManagedPackageCount = '3'
                    ManagedUniquePackageCount = '3'
                    ManagedCacheHits = '1'
                    ManagedExtractionCacheHits = '1'
                    ManagedFirstMs = '1500'
                    ManagedLastMs = '500'
                    ManagedFirstRepositoryRequests = '10'
                    ManagedLastRepositoryRequests = '0'
                    ManagedFirstPackageRepositoryRequests = '8'
                    ManagedLastPackageRepositoryRequests = '0'
                    ManagedFirstDownloadBytes = '2097152'
                    ManagedLastDownloadBytes = '0'
                    ManagedFirstCacheHits = '0'
                    ManagedLastCacheHits = '3'
                    ManagedFirstExtractionCacheHits = '0'
                    ManagedLastExtractionCacheHits = '3'
                    ManagedCoalescedWaitMs = '450'
                    ManagedLastCoalescedWaitMs = '125'
                    ManagedLastSlowestCoalescedWaitName = 'Company.Shared'
                    ManagedLastSlowestCoalescedWaitMs = '125'
                    ManagedInstallLockWaitMs = '80'
                    ManagedLastInstallLockWaitMs = '35'
                    ManagedLastSlowestInstallLockWaitName = 'Company.Lock'
                    ManagedLastSlowestInstallLockWaitMs = '35'
                    ManagedSlowestDependencyPackageMs = '300'
                    ManagedLastSlowestDependencyPackageName = 'Company.Branch'
                    ManagedLastSlowestDependencyPackageParent = 'Company.Root'
                    ManagedLastSlowestDependencyPackageMs = '200'
                    ManagedSlowestMaterializedPackageMs = '650'
                    ManagedLastSlowestMaterializedPackageName = 'Company.Files'
                    ManagedLastSlowestMaterializedPackageMs = '400'
                    ManagedLastSlowestMaterializedPackageExtractionMs = '300'
                    ManagedLastSlowestMaterializedPackagePromotionMs = '50'
                    ManagedLastCriticalDependencyBranchName = 'Company.Branch'
                    ManagedLastCriticalDependencyBranchParent = 'Company.Root'
                    ManagedLastCriticalDependencyBranchMs = '220'
                    ManagedLastCriticalDependencyBranchDominantPhase = 'Dependency'
                    ManagedLastCriticalDependencyBranchDominantPhaseMs = '200'
                    ManagedLastCriticalRootBranchName = 'Company.Root'
                    ManagedLastCriticalRootBranchMs = '120'
                    ManagedLastCriticalRootBranchDominantPhase = 'Dependency'
                    ManagedLastCriticalRootBranchDominantPhaseMs = '120'
                    ManagedLastCriticalMaterializationBranchName = 'Company.Files'
                    ManagedLastCriticalMaterializationBranchMs = '350'
                    ManagedLastCriticalMaterializationDominantPhase = 'Extraction'
                    ManagedLastCriticalMaterializationDominantPhaseMs = '300'
                    ManagedLastRootDependencyMs = '120'
                    ManagedLastDependencyMs = '200'
                    ManagedLastDownloadMs = '0'
                    ManagedLastExtractionMs = '300'
                    ManagedLastPromotionMs = '40'
                    ManagedLastPromotionMoveMs = '25'
                }
            )

            New-ManagedOptimizationTarget -Rows $rows
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(results);
        Assert.Equal("Download", Property(row, "Bottleneck"));
        Assert.Equal("60%", Property(row, "BottleneckShare"));
        Assert.Equal(60.0, NumericProperty(row, "BottleneckShareRaw"));
        Assert.Equal(string.Empty, Property(row, "TimingNote"));
        Assert.Equal(2.0, NumericProperty(row, "DownloadMB"));
        Assert.Equal(2.0, NumericProperty(row, "FirstDownloadMB"));
        Assert.Equal(0.0, NumericProperty(row, "LastDownloadMB"));
        Assert.Equal(1500.0, NumericProperty(row, "FirstMs"));
        Assert.Equal(500.0, NumericProperty(row, "LastMs"));
        Assert.Equal(10.0, NumericProperty(row, "RepositoryRequests"));
        Assert.Equal(10.0, NumericProperty(row, "FirstRepositoryRequests"));
        Assert.Equal(0.0, NumericProperty(row, "LastRepositoryRequests"));
        Assert.Equal(8.0, NumericProperty(row, "FirstPackageRepositoryRequests"));
        Assert.Equal(0.0, NumericProperty(row, "LastPackageRepositoryRequests"));
        Assert.Equal(4.0, NumericProperty(row, "PackageRepositoryRedirects"));
        Assert.Equal(1.0, NumericProperty(row, "ExtractionCacheHits"));
        Assert.Equal(0.0, NumericProperty(row, "FirstCacheHits"));
        Assert.Equal(3.0, NumericProperty(row, "LastCacheHits"));
        Assert.Equal(0.0, NumericProperty(row, "FirstExtractionCacheHits"));
        Assert.Equal(3.0, NumericProperty(row, "LastExtractionCacheHits"));
        Assert.Equal(450.0, NumericProperty(row, "CoalescedWaitMs"));
        Assert.Equal(125.0, NumericProperty(row, "LastCoalescedWaitMs"));
        Assert.Equal("Company.Shared", Property(row, "LastSlowestCoalescedWait"));
        Assert.Equal(125.0, NumericProperty(row, "LastSlowestCoalescedWaitMs"));
        Assert.Equal(80.0, NumericProperty(row, "InstallLockWaitMs"));
        Assert.Equal(35.0, NumericProperty(row, "LastInstallLockWaitMs"));
        Assert.Equal("Company.Lock", Property(row, "LastSlowestInstallLockWait"));
        Assert.Equal(35.0, NumericProperty(row, "LastSlowestInstallLockWaitMs"));
        Assert.Equal(300.0, NumericProperty(row, "DependencyMs"));
        Assert.Equal(200.0, NumericProperty(row, "LastDependencyMs"));
        Assert.Equal(300.0, NumericProperty(row, "SlowestDependencyPackageMs"));
        Assert.Equal("Company.Branch", Property(row, "LastSlowestDependencyPackage"));
        Assert.Equal("Company.Root", Property(row, "LastSlowestDependencyPackageParent"));
        Assert.Equal(200.0, NumericProperty(row, "LastSlowestDependencyPackageMs"));
        Assert.Equal(650.0, NumericProperty(row, "SlowestMaterializedPackageMs"));
        Assert.Equal("Company.Files", Property(row, "LastSlowestMaterializedPackage"));
        Assert.Equal(400.0, NumericProperty(row, "LastSlowestMaterializedPackageMs"));
        Assert.Equal(300.0, NumericProperty(row, "LastSlowestMaterializedPackageExtractionMs"));
        Assert.Equal(50.0, NumericProperty(row, "LastSlowestMaterializedPackagePromotionMs"));
        Assert.Equal("Company.Branch", Property(row, "LastCriticalDependencyBranch"));
        Assert.Equal("Company.Root", Property(row, "LastCriticalDependencyBranchParent"));
        Assert.Equal(220.0, NumericProperty(row, "LastCriticalDependencyBranchMs"));
        Assert.Equal("Dependency", Property(row, "LastCriticalDependencyBranchDominantPhase"));
        Assert.Equal(200.0, NumericProperty(row, "LastCriticalDependencyBranchDominantPhaseMs"));
        Assert.Equal("Company.Root", Property(row, "LastCriticalRootBranch"));
        Assert.Equal(120.0, NumericProperty(row, "LastCriticalRootBranchMs"));
        Assert.Equal("Dependency", Property(row, "LastCriticalRootBranchDominantPhase"));
        Assert.Equal(120.0, NumericProperty(row, "LastCriticalRootBranchDominantPhaseMs"));
        Assert.Equal("Company.Files", Property(row, "LastCriticalMaterializationBranch"));
        Assert.Equal(350.0, NumericProperty(row, "LastCriticalMaterializationBranchMs"));
        Assert.Equal("Extraction", Property(row, "LastCriticalMaterializationDominantPhase"));
        Assert.Equal(300.0, NumericProperty(row, "LastCriticalMaterializationDominantPhaseMs"));
        Assert.Equal("MaterializationBranch", Property(row, "LastCriticalOptimizationLane"));
        Assert.Equal(350.0, NumericProperty(row, "LastCriticalOptimizationLaneMs"));
        Assert.Contains("materialization", Property(row, "LastCriticalOptimizationQuestion"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(120.0, NumericProperty(row, "LastRootDependencyMs"));
        Assert.Equal(0.0, NumericProperty(row, "LastDownloadMs"));
        Assert.Equal(300.0, NumericProperty(row, "LastExtractionMs"));
        Assert.Equal(40.0, NumericProperty(row, "LastPromotionMs"));
        Assert.Equal(25.0, NumericProperty(row, "LastPromotionMoveMs"));
        Assert.Equal(325.0, NumericProperty(row, "LastMaterializationMs"));
        Assert.Equal(30.77, NumericProperty(row, "LastMaterializationMBPerSecond"));
        Assert.Equal(61.54, NumericProperty(row, "LastMaterializationFilesPerSecond"));
        Assert.Equal("Extraction", Property(row, "LastMaterializationDominantPhase"));
        Assert.Equal("Materialization", Property(row, "LastWarmOptimizationLane"));
        Assert.Equal(325.0, NumericProperty(row, "LastWarmOptimizationLaneMs"));
        Assert.Contains("materialization", Property(row, "LastWarmOptimizationQuestion"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Extraction", Property(row, "LastBottleneck"));
        Assert.Equal(300.0, NumericProperty(row, "LastBottleneckMs"));
        Assert.Equal("60%", Property(row, "LastBottleneckShare"));
        Assert.Contains("download", Property(row, "NextQuestion"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("archive extraction", Property(row, "LastNextQuestion"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptimizationTarget_IdentifiesDependencyBottleneck()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $rows = @(
                [pscustomobject]@{
                    Suite = 'HeavySaveCacheGate'
                    Scenario = 'Az.Full.Save.ManagedWarmCache'
                    ModuleName = 'Az'
                    Host = 'PowerShell7'
                    Operation = 'Save'
                    ManagedMs = '4000'
                    ManagedRank = '1'
                    ManagedVsFastest = '1x'
                    ManagedOutputBytes = '623902720'
                    ManagedOutputFileCount = '2800'
                    ManagedRootElapsedMs = '3900'
                    ManagedHarnessOverheadMs = '100'
                    ManagedRootDependencyMs = '3500'
                    ManagedDependencyBranchElapsedMs = '9800'
                    ManagedDependencyBranchOverheadMs = '5600'
                    ManagedDependencyMs = '4600'
                    ManagedDownloadMs = '0'
                    ManagedExtractionMs = '700'
                    ManagedPromotionMs = '120'
                    ManagedRepositoryRequests = '0'
                    ManagedPackageRepositoryRequests = '0'
                    ManagedPackageRepositoryRedirects = '0'
                    ManagedDownloadBytes = '0'
                    ManagedPackageCount = '102'
                    ManagedUniquePackageCount = '102'
                    ManagedCacheHits = '102'
                    ManagedLastMs = '3800'
                    ManagedLastRootDependencyMs = '3300'
                    ManagedLastRootDependencyCriticalPathGapMs = '2400'
                    ManagedLastDependencyBranchParallelismRatio = '7.1'
                    ManagedFirstDependencyBranchElapsedMs = '11200'
                    ManagedLastDependencyBranchElapsedMs = '9200'
                    ManagedLastDependencyBranchOverheadMs = '5000'
                    ManagedLastDependencyMs = '4200'
                    ManagedLastDownloadMs = '0'
                    ManagedLastExtractionMs = '600'
                    ManagedLastPromotionMs = '100'
                    ManagedLastPromotionMoveMs = '75'
                    ManagedSlowestDependencyPackageMs = '900'
                    ManagedLastSlowestDependencyPackageName = 'Az.Advisor'
                    ManagedLastSlowestDependencyPackageParent = 'Az'
                    ManagedLastSlowestDependencyPackageMs = '800'
                    ManagedLastCriticalDependencyBranchName = 'Az.Advisor'
                    ManagedLastCriticalDependencyBranchParent = 'Az'
                    ManagedLastCriticalDependencyBranchMs = '900'
                    ManagedLastCriticalDependencyBranchDominantPhase = 'Dependency'
                    ManagedLastCriticalDependencyBranchDominantPhaseMs = '800'
                    ManagedLastCriticalRootBranchName = 'Az'
                    ManagedLastCriticalRootBranchMs = '3300'
                    ManagedLastCriticalRootBranchDominantPhase = 'Dependency'
                    ManagedLastCriticalRootBranchDominantPhaseMs = '3300'
                    ManagedLastCriticalMaterializationBranchName = 'Az.Files'
                    ManagedLastCriticalMaterializationBranchMs = '675'
                    ManagedLastCriticalMaterializationDominantPhase = 'Extraction'
                    ManagedLastCriticalMaterializationDominantPhaseMs = '600'
                }
            )

            New-ManagedOptimizationTarget -Rows $rows
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(results);
        Assert.Equal("Dependency", Property(row, "Bottleneck"));
        Assert.Equal("Dependency", Property(row, "LastBottleneck"));
        Assert.Equal(4600.0, NumericProperty(row, "DependencyMs"));
        Assert.Equal(4200.0, NumericProperty(row, "LastDependencyMs"));
        Assert.Equal(675.0, NumericProperty(row, "LastMaterializationMs"));
        Assert.Equal("Extraction", Property(row, "LastMaterializationDominantPhase"));
        Assert.Equal("Dependency", Property(row, "LastWarmOptimizationLane"));
        Assert.Equal(4200.0, NumericProperty(row, "LastWarmOptimizationLaneMs"));
        Assert.Contains("dependency", Property(row, "LastWarmOptimizationQuestion"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("RootBranch", Property(row, "LastCriticalOptimizationLane"));
        Assert.Equal(3300.0, NumericProperty(row, "LastCriticalOptimizationLaneMs"));
        Assert.Contains("root branch", Property(row, "LastCriticalOptimizationQuestion"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Az", Property(row, "LastCriticalRootBranch"));
        Assert.Equal(3300.0, NumericProperty(row, "LastCriticalRootBranchMs"));
        Assert.Equal(2400.0, NumericProperty(row, "LastRootDependencyCriticalPathGapMs"));
        Assert.Equal(7.1, NumericProperty(row, "LastDependencyBranchParallelismRatio"));
        Assert.Equal(9800.0, NumericProperty(row, "DependencyBranchElapsedMs"));
        Assert.Equal(5600.0, NumericProperty(row, "DependencyBranchOverheadMs"));
        Assert.Equal(11200.0, NumericProperty(row, "FirstDependencyBranchElapsedMs"));
        Assert.Equal(9200.0, NumericProperty(row, "LastDependencyBranchElapsedMs"));
        Assert.Equal(5000.0, NumericProperty(row, "LastDependencyBranchOverheadMs"));
        Assert.Equal("Az.Advisor", Property(row, "LastCriticalDependencyBranch"));
        Assert.Equal(900.0, NumericProperty(row, "LastCriticalDependencyBranchMs"));
        Assert.Equal("Az.Files", Property(row, "LastCriticalMaterializationBranch"));
        Assert.Equal(675.0, NumericProperty(row, "LastCriticalMaterializationBranchMs"));
        Assert.Equal("Az.Advisor", Property(row, "LastSlowestDependencyPackage"));
        Assert.Equal("Az", Property(row, "LastSlowestDependencyPackageParent"));
        Assert.Equal(800.0, NumericProperty(row, "LastSlowestDependencyPackageMs"));
        Assert.Contains("dependency graph", Property(row, "NextQuestion"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptimizationTarget_MarksOverlappedPhaseTiming()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $rows = @(
                [pscustomobject]@{
                    Suite = 'SpeedGate'
                    Scenario = 'Graph.Full.ProviderMatrix'
                    ModuleName = 'Microsoft.Graph'
                    Host = 'PowerShell7'
                    Operation = 'Install'
                    ManagedMs = '7213.3'
                    ManagedRank = '1'
                    ManagedVsFastest = '1x'
                    ManagedRootElapsedMs = '6320.63'
                    ManagedHarnessOverheadMs = '893'
                    ManagedRootDependencyMs = '5445.89'
                    ManagedDownloadMs = '71365.43'
                    ManagedExtractionMs = '1196.52'
                    ManagedPromotionMs = '379'
                    ManagedRepositoryRequests = '80'
                    ManagedPackageRepositoryRequests = '80'
                    ManagedPackageRepositoryRedirects = '40'
                    ManagedDownloadBytes = '186506621'
                    ManagedPackageCount = '78'
                    ManagedUniquePackageCount = '40'
                    ManagedCacheHits = '0'
                }
            )

            New-ManagedOptimizationTarget -Rows $rows
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(results);
        Assert.Equal(">100%", Property(row, "BottleneckShare"));
        Assert.Equal(989.4, NumericProperty(row, "BottleneckShareRaw"));
        Assert.Contains("overlap", Property(row, "TimingNote"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptimizationTarget_ReportsUninstrumentedRows()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $rows = @(
                [pscustomobject]@{
                    Suite = 'Smoke'
                    Scenario = 'ThreadJob'
                    ModuleName = 'ThreadJob'
                    Host = 'WindowsPowerShell'
                    Operation = 'Find'
                    ManagedMs = '500'
                    ManagedRank = '1'
                    ManagedVsFastest = '1x'
                    ManagedRootElapsedMs = '0'
                    ManagedHarnessOverheadMs = '0'
                    ManagedRootDependencyMs = '0'
                    ManagedDownloadMs = '0'
                    ManagedExtractionMs = '0'
                    ManagedPromotionMs = '0'
                    ManagedRepositoryRequests = '0'
                    ManagedPackageRepositoryRequests = '0'
                    ManagedPackageRepositoryRedirects = '0'
                    ManagedDownloadBytes = '0'
                    ManagedPackageCount = '0'
                    ManagedUniquePackageCount = '0'
                    ManagedCacheHits = '0'
                }
            )

            New-ManagedOptimizationTarget -Rows $rows
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(results);
        Assert.Equal("Uninstrumented", Property(row, "Bottleneck"));
        Assert.Equal("Uninstrumented", Property(row, "LastBottleneck"));
        Assert.Equal(string.Empty, Property(row, "BottleneckShare"));
        Assert.Contains("instrumentation", Property(row, "NextQuestion"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptimizationTarget_OrdersRowsByInputAndFiltersRowsWithoutManagedTiming()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $rows = @(
                [pscustomobject]@{
                    Suite = 'SpeedGate'
                    Scenario = 'Graph.Full.SameSource'
                    ModuleName = 'Microsoft.Graph'
                    Host = 'PowerShell7'
                    Operation = 'Install'
                    ManagedMs = '0'
                },
                [pscustomobject]@{
                    Suite = 'LifecycleGate'
                    Scenario = 'ThreadJob.InstallSave.NoOpForce'
                    ModuleName = 'ThreadJob'
                    Host = 'PowerShell7'
                    Operation = 'InstallForce'
                    ManagedMs = '800'
                    ManagedRank = '1'
                    ManagedVsFastest = '1x'
                    ManagedRootElapsedMs = '760'
                    ManagedHarnessOverheadMs = '40'
                    ManagedRootDependencyMs = '700'
                    ManagedDownloadMs = '10'
                    ManagedExtractionMs = '5'
                    ManagedPromotionMs = '3'
                    ManagedRepositoryRequests = '2'
                    ManagedPackageRepositoryRequests = '1'
                    ManagedPackageRepositoryRedirects = '0'
                    ManagedDownloadBytes = '100'
                    ManagedPackageCount = '1'
                    ManagedUniquePackageCount = '1'
                    ManagedCacheHits = '0'
                }
            )

            New-ManagedOptimizationTarget -Rows $rows
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(results);
        Assert.Equal("LifecycleGate", Property(row, "Suite"));
        Assert.Equal("RootDependency", Property(row, "Bottleneck"));
    }

    private static PowerShell CreateBenchmarkPowerShell(string script)
    {
        var root = RepoRootLocator.Find();
        var hostComparisonScript = Path.Combine(root, "Benchmarks", "ManagedModules", "ManagedModuleBenchmark.HostComparison.ps1");
        var optimizationScript = Path.Combine(root, "Benchmarks", "ManagedModules", "ManagedModuleBenchmark.OptimizationTargets.ps1");
        var ps = PowerShell.Create();
        ps.AddScript(File.ReadAllText(hostComparisonScript) + Environment.NewLine + File.ReadAllText(optimizationScript) + Environment.NewLine + script);
        return ps;
    }

    private static string Property(PSObject value, string name)
        => (string)value.Properties[name].Value;

    private static double NumericProperty(PSObject value, string name)
        => Convert.ToDouble(value.Properties[name].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString()));
        Assert.Fail(message);
    }
}
