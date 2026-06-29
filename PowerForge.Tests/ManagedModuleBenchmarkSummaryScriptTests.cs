using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkSummaryScriptTests
{
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
                    ManagedTotalDownloadMilliseconds = 25000
                    ManagedTotalExtractionMilliseconds = 1200
                    ManagedTotalPromotionMilliseconds = 60
                    ManagedRepositoryRequestCount = 40
                    ManagedPackageRepositoryRequestCount = 40
                    ManagedPackageRepositoryRedirectCount = 0
                    ManagedDownloadBytes = 186506621
                    ManagedCacheHitCount = 0
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
                    ManagedTotalDownloadMilliseconds = 0
                    ManagedTotalExtractionMilliseconds = 1100
                    ManagedTotalPromotionMilliseconds = 58
                    ManagedRepositoryRequestCount = 0
                    ManagedPackageRepositoryRequestCount = 0
                    ManagedPackageRepositoryRedirectCount = 0
                    ManagedDownloadBytes = 0
                    ManagedCacheHitCount = 40
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
        Assert.Equal(186506621.0, NumericProperty(row, "ManagedFirstDownloadBytes"));
        Assert.Equal(0.0, NumericProperty(row, "ManagedLastDownloadBytes"));
        Assert.Equal(0.0, NumericProperty(row, "ManagedFirstCacheHits"));
        Assert.Equal(40.0, NumericProperty(row, "ManagedLastCacheHits"));
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

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(static error => error.ToString()));
        Assert.Fail(message);
    }
}
