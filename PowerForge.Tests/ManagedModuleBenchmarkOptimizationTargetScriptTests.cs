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
                    ManagedRootElapsedMs = '900'
                    ManagedHarnessOverheadMs = '100'
                    ManagedRootDependencyMs = '0'
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
        Assert.Equal(10.0, NumericProperty(row, "RepositoryRequests"));
        Assert.Equal(4.0, NumericProperty(row, "PackageRepositoryRedirects"));
        Assert.Contains("download", Property(row, "NextQuestion"), StringComparison.OrdinalIgnoreCase);
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
