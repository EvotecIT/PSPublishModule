using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkEngineSummaryScriptTests
{
    [Fact]
    public void EngineRows_PreserveWarmSteadyStateMetrics()
    {
        var runPath = Path.Combine(Path.GetTempPath(), "pf-engine-summary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runPath);

        try
        {
            File.WriteAllText(
                Path.Combine(runPath, "managed-module-summary.csv"),
                string.Join(
                    Environment.NewLine,
                    [
                        "Operation,Engine,Runs,Succeeded,Failed,Skipped,MedianMs,WarmRuns,WarmMedianMs,WarmMinMs,WarmMaxMs,FirstIteration,LastIteration,FirstMs,LastMs,MinMs,MaxMs,MedianOutputFileCount,MedianOutputBytes",
                        "Save,Managed,3,3,0,0,2229.87,2,2033.57,1837.27,2229.87,1,3,4098.36,2229.87,1837.27,4098.36,2789,623021826"
                    ]));

            var escapedRunPath = runPath.Replace("'", "''", StringComparison.Ordinal);
            using var ps = CreateBenchmarkPowerShell(string.Join(
                Environment.NewLine,
                [
                    "function Get-ScenarioEngines { param($Scenario) $Scenario.Engines }",
                    "$rows = [Collections.Generic.List[object]]::new()",
                    "$scenario = [pscustomobject]@{",
                    "    Suite = 'HeavySaveCacheGate'",
                    "    Name = 'Az.Full.Save.ManagedWarmCache'",
                    "    BenchmarkRole = 'Diagnostic'",
                    "    ComparisonScope = 'ManagedOnlySaveCache'",
                    "    BenchmarkInterpretation = 'Managed warm cache'",
                    "    ModuleName = 'Az'",
                    "    Engines = @('Managed')",
                    "}",
                    $"Add-ManagedBenchmarkEngineRows -Rows $rows -Scenario $scenario -HostLabel 'PowerShell7' -RunPath '{escapedRunPath}'",
                    "$rows[0]"
                ]));

            var results = ps.Invoke();

            AssertNoErrors(ps);
            var row = Assert.Single(results);
            Assert.Equal(2.0, NumericProperty(row, "WarmRuns"));
            Assert.Equal(2033.57, NumericProperty(row, "WarmMedianMs"));
            Assert.Equal(1837.27, NumericProperty(row, "WarmMinMs"));
            Assert.Equal(2229.87, NumericProperty(row, "WarmMaxMs"));
        }
        finally
        {
            Directory.Delete(runPath, recursive: true);
        }
    }

    private static PowerShell CreateBenchmarkPowerShell(string script)
    {
        var root = RepoRootLocator.Find();
        var hostComparisonScript = Path.Combine(root, "Benchmarks", "ManagedModules", "ManagedModuleBenchmark.HostComparison.ps1");
        var engineSummaryScript = Path.Combine(root, "Benchmarks", "ManagedModules", "ManagedModuleBenchmark.EngineSummary.ps1");
        var ps = PowerShell.Create();
        ps.AddScript(
            File.ReadAllText(hostComparisonScript)
            + Environment.NewLine
            + File.ReadAllText(engineSummaryScript)
            + Environment.NewLine
            + script);
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
