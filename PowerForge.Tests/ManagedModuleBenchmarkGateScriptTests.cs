using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkGateScriptTests
{
    [Fact]
    public void GateForSuite_DoesNotApplyScenarioThresholdsUnlessRequested()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $row = [pscustomobject]@{
                Suite = 'SaveGate'
                Scenario = 'Graph.Authentication.Save'
                Host = 'PowerShell7'
                Operation = 'Save'
                FastestEngine = 'PSResourceGet'
                FastestMs = 1000
                ManagedMs = 1100
                ManagedRank = 2
                ManagedVsFastest = '1.10x'
                GateManagedMaxRank = 1
                GateManagedMaxVsFastest = 1.05
            }

            Get-ManagedPerformanceGateViolationForSuite -Rows @($row) -MaxRank 0 -MaxVsFastest 0
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        Assert.Empty(results);
    }

    [Fact]
    public void GateForSuite_AppliesScenarioOwnedThresholdsWhenRequested()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $ratioRow = [pscustomobject]@{
                Suite = 'SaveGate'
                Scenario = 'Graph.Authentication.Save'
                Host = 'PowerShell7'
                Operation = 'Save'
                FastestEngine = 'PSResourceGet'
                FastestMs = 1000
                ManagedMs = 1100
                ManagedRank = 2
                ManagedVsFastest = '1.10x'
                GateManagedMaxRank = 0
                GateManagedMaxVsFastest = 1.05
            }
            $rankRow = [pscustomobject]@{
                Suite = 'SpeedGate'
                Scenario = 'Graph.Full.SameSource'
                Host = 'PowerShell7'
                Operation = 'Install'
                FastestEngine = 'ModuleFast'
                FastestMs = 1000
                ManagedMs = 1010
                ManagedRank = 2
                ManagedVsFastest = '1.01x'
                GateManagedMaxRank = 1
                GateManagedMaxVsFastest = 0
            }

            Get-ManagedPerformanceGateViolationForSuite -Rows @($ratioRow, $rankRow) -MaxRank 0 -MaxVsFastest 0 -UseScenarioGates
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, result => Property(result, "Reason").Contains("ratio", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, result => Property(result, "Reason").Contains("rank", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GateForSuite_ExplicitThresholdsOverrideScenarioOwnedThresholds()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $row = [pscustomobject]@{
                Suite = 'SaveGate'
                Scenario = 'Graph.Authentication.Save'
                Host = 'PowerShell7'
                Operation = 'Save'
                FastestEngine = 'PSResourceGet'
                FastestMs = 1000
                ManagedMs = 1100
                ManagedRank = 2
                ManagedVsFastest = '1.10x'
                GateManagedMaxRank = 1
                GateManagedMaxVsFastest = 1.05
            }

            Get-ManagedPerformanceGateViolationForSuite -Rows @($row) -MaxRank 0 -MaxVsFastest 1.25 -UseScenarioGates
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        Assert.Empty(results);
    }

    [Fact]
    public void GateForSuite_ExplicitRankGateCanBeTighterThanScenarioThresholds()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $row = [pscustomobject]@{
                Suite = 'SpeedGate'
                Scenario = 'Graph.Full.SameSource'
                Host = 'PowerShell7'
                Operation = 'Install'
                FastestEngine = 'ModuleFast'
                FastestMs = 1000
                ManagedMs = 1010
                ManagedRank = 2
                ManagedVsFastest = '1.01x'
                GateManagedMaxRank = 3
                GateManagedMaxVsFastest = 1.25
            }

            Get-ManagedPerformanceGateViolationForSuite -Rows @($row) -MaxRank 1 -MaxVsFastest 0 -UseScenarioGates
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var violation = Assert.Single(results);
        Assert.Contains("rank", Property(violation, "Reason"), StringComparison.OrdinalIgnoreCase);
    }

    private static PowerShell CreateBenchmarkPowerShell(string script)
    {
        var gateScript = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "ManagedModuleBenchmark.PerformanceGate.ps1");
        var ps = PowerShell.Create();
        ps.AddScript(File.ReadAllText(gateScript) + Environment.NewLine + script);
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
