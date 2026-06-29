using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkScoreboardScriptTests
{
    [Fact]
    public void Scoreboard_CreatesWideProviderRowsWithRatios()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $engineRows = @(
                [pscustomobject]@{
                    BenchmarkRole = 'Scoreboard'
                    Suite = 'SpeedGate'
                    Scenario = 'Graph.Full.ProviderMatrix'
                    ComparisonScope = 'InstallProviderMatrix'
                    BenchmarkInterpretation = 'Install scoreboard'
                    ModuleName = 'Microsoft.Graph'
                    Host = 'PowerShell7'
                    Operation = 'Install'
                    Engine = 'Managed'
                    MedianMs = '5080.40'
                    Succeeded = '1'
                    Failed = '0'
                    Skipped = '0'
                },
                [pscustomobject]@{
                    BenchmarkRole = 'Scoreboard'
                    Suite = 'SpeedGate'
                    Scenario = 'Graph.Full.ProviderMatrix'
                    ComparisonScope = 'InstallProviderMatrix'
                    BenchmarkInterpretation = 'Install scoreboard'
                    ModuleName = 'Microsoft.Graph'
                    Host = 'PowerShell7'
                    Operation = 'Install'
                    Engine = 'ModuleFast'
                    MedianMs = '7133.02'
                    Succeeded = '1'
                    Failed = '0'
                    Skipped = '0'
                },
                [pscustomobject]@{
                    BenchmarkRole = 'Scoreboard'
                    Suite = 'SpeedGate'
                    Scenario = 'Graph.Full.ProviderMatrix'
                    ComparisonScope = 'InstallProviderMatrix'
                    BenchmarkInterpretation = 'Install scoreboard'
                    ModuleName = 'Microsoft.Graph'
                    Host = 'PowerShell7'
                    Operation = 'Install'
                    Engine = 'PSResourceGet'
                    MedianMs = '53378.88'
                    Succeeded = '1'
                    Failed = '0'
                    Skipped = '0'
                },
                [pscustomobject]@{
                    BenchmarkRole = 'Scoreboard'
                    Suite = 'SpeedGate'
                    Scenario = 'Graph.Full.ProviderMatrix'
                    ComparisonScope = 'InstallProviderMatrix'
                    BenchmarkInterpretation = 'Install scoreboard'
                    ModuleName = 'Microsoft.Graph'
                    Host = 'PowerShell7'
                    Operation = 'Install'
                    Engine = 'PowerShellGet'
                    MedianMs = '67081.83'
                    Succeeded = '1'
                    Failed = '0'
                    Skipped = '0'
                }
            )

            New-ManagedBenchmarkScoreboard -EngineRows $engineRows
            """);

        var result = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(result);
        Assert.Equal("Managed", row.Property("FastestEngine"));
        Assert.Equal("1", row.Property("ManagedRank"));
        Assert.Equal("1x", row.Property("ManagedVsFastest"));
        Assert.Equal("5080.40 ms (1x)", row.Property("Managed"));
        Assert.Equal("7133.02 ms (1.40x)", row.Property("ModuleFast"));
        Assert.Equal("53378.88 ms (10.51x)", row.Property("PSResourceGet"));
        Assert.Equal("67081.83 ms (13.20x)", row.Property("PowerShellGet"));
        Assert.Equal("Managed,ModuleFast,PSResourceGet,PowerShellGet", row.Property("SuccessfulEngines"));
    }

    [Fact]
    public void Scoreboard_PreservesSkippedAndFailedProviderState()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $engineRows = @(
                [pscustomobject]@{
                    BenchmarkRole = 'Scoreboard'
                    Suite = 'HeavySaveGate'
                    Scenario = 'Az.Full.Save'
                    ComparisonScope = 'SaveCapableProviders'
                    BenchmarkInterpretation = 'Save scoreboard'
                    ModuleName = 'Az'
                    Host = 'WindowsPowerShell'
                    Operation = 'Save'
                    Engine = 'Managed'
                    MedianMs = '8561.29'
                    Succeeded = '1'
                    Failed = '0'
                    Skipped = '0'
                },
                [pscustomobject]@{
                    BenchmarkRole = 'Scoreboard'
                    Suite = 'HeavySaveGate'
                    Scenario = 'Az.Full.Save'
                    ComparisonScope = 'SaveCapableProviders'
                    BenchmarkInterpretation = 'Save scoreboard'
                    ModuleName = 'Az'
                    Host = 'WindowsPowerShell'
                    Operation = 'Save'
                    Engine = 'ModuleFast'
                    MedianMs = '0'
                    Succeeded = '0'
                    Failed = '0'
                    Skipped = '1'
                },
                [pscustomobject]@{
                    BenchmarkRole = 'Scoreboard'
                    Suite = 'HeavySaveGate'
                    Scenario = 'Az.Full.Save'
                    ComparisonScope = 'SaveCapableProviders'
                    BenchmarkInterpretation = 'Save scoreboard'
                    ModuleName = 'Az'
                    Host = 'WindowsPowerShell'
                    Operation = 'Save'
                    Engine = 'PSResourceGet'
                    MedianMs = '0'
                    Succeeded = '0'
                    Failed = '1'
                    Skipped = '0'
                },
                [pscustomobject]@{
                    BenchmarkRole = 'Scoreboard'
                    Suite = 'HeavySaveGate'
                    Scenario = 'Az.Full.Save'
                    ComparisonScope = 'SaveCapableProviders'
                    BenchmarkInterpretation = 'Save scoreboard'
                    ModuleName = 'Az'
                    Host = 'WindowsPowerShell'
                    Operation = 'Save'
                    Engine = 'PowerShellGet'
                    MedianMs = '151052.21'
                    Succeeded = '1'
                    Failed = '0'
                    Skipped = '0'
                }
            )

            New-ManagedBenchmarkScoreboard -EngineRows $engineRows
            """);

        var result = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(result);
        Assert.Equal("Managed", row.Property("FastestEngine"));
        Assert.Equal("Skipped", row.Property("ModuleFast"));
        Assert.Equal("Failed", row.Property("PSResourceGet"));
        Assert.Equal("151052.21 ms (17.64x)", row.Property("PowerShellGet"));
        Assert.Equal("ModuleFast", row.Property("SkippedEngines"));
        Assert.Equal("PSResourceGet", row.Property("FailedEngines"));
    }

    private static PowerShell CreateBenchmarkPowerShell(string script)
    {
        var scoreboardScript = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "ManagedModuleBenchmark.Scoreboard.ps1");
        var ps = PowerShell.Create();
        ps.AddScript(File.ReadAllText(scoreboardScript) + Environment.NewLine + script);
        return ps;
    }

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString()));
        Assert.Fail(message);
    }
}

internal static class PowerShellObjectExtensions
{
    public static string Property(this PSObject value, string name)
        => (string?)value.Properties[name]?.Value ?? string.Empty;
}
