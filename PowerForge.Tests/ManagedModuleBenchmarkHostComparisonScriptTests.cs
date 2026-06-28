using System.Globalization;
using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkHostComparisonScriptTests
{
    [Fact]
    public void HostComparison_ComparesManagedTimingAcrossPowerShellHosts()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $rows = @(
                [pscustomobject]@{
                    Suite = 'SaveGate'
                    Scenario = 'Graph.Authentication.Save'
                    ModuleName = 'Microsoft.Graph.Authentication'
                    Operation = 'Save'
                    Host = 'PowerShell7'
                    ManagedMs = '1000.25'
                    RunPath = 'ps7-run'
                },
                [pscustomobject]@{
                    Suite = 'SaveGate'
                    Scenario = 'Graph.Authentication.Save'
                    ModuleName = 'Microsoft.Graph.Authentication'
                    Operation = 'Save'
                    Host = 'WindowsPowerShell'
                    ManagedMs = '1500.50'
                    RunPath = 'ps51-run'
                }
            )

            New-ManagedHostComparison -Rows $rows
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(results);
        Assert.Equal("Compared", Property(row, "Status"));
        Assert.Equal("PowerShell7", Property(row, "FasterHost"));
        Assert.Equal("1.5x", Property(row, "ComparisonVsBaseline"));
        Assert.Equal("ps7-run", Property(row, "BaselineRunPath"));
        Assert.Equal("ps51-run", Property(row, "ComparisonRunPath"));
    }

    [Fact]
    public void HostComparison_ReportsMissingComparisonHost()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $rows = @(
                [pscustomobject]@{
                    Suite = 'SpeedGate'
                    Scenario = 'Graph.Full.SameSource'
                    ModuleName = 'Microsoft.Graph'
                    Operation = 'Install'
                    Host = 'PowerShell7'
                    ManagedMs = '3500'
                }
            )

            New-ManagedHostComparison -Rows $rows
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(results);
        Assert.Equal("MissingComparison", Property(row, "Status"));
        Assert.Equal(string.Empty, Property(row, "ComparisonVsBaseline"));
    }

    [Fact]
    public void HostComparison_ParsesCurrentCultureDecimalValues()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
        try
        {
            using var ps = CreateBenchmarkPowerShell("""
                $rows = @(
                    [pscustomobject]@{
                        Suite = 'LifecycleGate'
                        Scenario = 'ThreadJob.InstallSave.NoOpForce'
                        ModuleName = 'ThreadJob'
                        Operation = 'InstallNoOp'
                        Host = 'PowerShell7'
                        ManagedMs = '500,5'
                    },
                    [pscustomobject]@{
                        Suite = 'LifecycleGate'
                        Scenario = 'ThreadJob.InstallSave.NoOpForce'
                        ModuleName = 'ThreadJob'
                        Operation = 'InstallNoOp'
                        Host = 'WindowsPowerShell'
                        ManagedMs = '751,0'
                    }
                )

                New-ManagedHostComparison -Rows $rows
                """);

            var results = ps.Invoke();

            AssertNoErrors(ps);
            var row = Assert.Single(results);
            Assert.Equal("Compared", Property(row, "Status"));
            Assert.Equal("1.5x", Property(row, "ComparisonVsBaseline"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static PowerShell CreateBenchmarkPowerShell(string script)
    {
        var hostComparisonScript = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "ManagedModuleBenchmark.HostComparison.ps1");
        var ps = PowerShell.Create();
        ps.AddScript(File.ReadAllText(hostComparisonScript) + Environment.NewLine + script);
        return ps;
    }

    private static string Property(PSObject value, string name)
        => (string)value.Properties[name].Value;

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString()));
        Assert.Fail(message);
    }
}
