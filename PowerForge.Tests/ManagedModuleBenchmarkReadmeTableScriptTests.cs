using System.Management.Automation;
using System.Text;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkReadmeTableScriptTests
{
    [Fact]
    public void ReadmeTable_UsesManagedBaselineAndCapabilityMarkers()
    {
        using var temp = new TemporaryDirectory();
        var scoreboardPath = Path.Combine(temp.Path, "suite-scoreboard.csv");
        File.WriteAllText(scoreboardPath, BuildScoreboardCsv(), Encoding.UTF8);

        using var ps = PowerShell.Create();
        var script = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "Export-ManagedModuleBenchmarkReadmeTable.ps1");
        ps.Runspace.SessionStateProxy.SetVariable("scriptText", File.ReadAllText(script));
        ps.Runspace.SessionStateProxy.SetVariable("scoreboardPath", scoreboardPath);
        ps.AddScript("& ([scriptblock]::Create($scriptText)) -ScoreboardPath $scoreboardPath");

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var table = Assert.Single(results).BaseObject.ToString() ?? string.Empty;
        Assert.Contains("| `Graph.Full.SameSource` | PowerShell 7 | Install | 1.00x (4.00s) | 1.50x (6.00s) | Not in this gate | Not in this gate | InstallSameSource |", table, StringComparison.Ordinal);
        Assert.Contains("| `Graph.Authentication.InstallExact.NoOpForce` | Windows PowerShell 5.1 | InstallNoOp | 1.00x (0.60s) | Skipped | 1.50x (0.90s) | Not in this gate | InstallWithModuleFast |", table, StringComparison.Ordinal);
        Assert.Contains("| `Az.Full.Save` | Windows PowerShell 5.1 | Save | 1.00x (5.80s) | Not equivalent | Failed | Skipped | SaveCapableProviders |", table, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadmeTable_CanSplitRowsByOperationFamily()
    {
        using var temp = new TemporaryDirectory();
        var scoreboardPath = Path.Combine(temp.Path, "suite-scoreboard.csv");
        File.WriteAllText(scoreboardPath, BuildScoreboardCsv(), Encoding.UTF8);

        using var ps = PowerShell.Create();
        var script = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "Export-ManagedModuleBenchmarkReadmeTable.ps1");
        ps.Runspace.SessionStateProxy.SetVariable("scriptText", File.ReadAllText(script));
        ps.Runspace.SessionStateProxy.SetVariable("scoreboardPath", scoreboardPath);
        ps.AddScript("& ([scriptblock]::Create($scriptText)) -ScoreboardPath $scoreboardPath -SplitByOperation");

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var table = Assert.Single(results).BaseObject.ToString() ?? string.Empty;
        var installIndex = table.IndexOf("#### Install", StringComparison.Ordinal);
        var saveIndex = table.IndexOf("#### Save", StringComparison.Ordinal);
        Assert.True(installIndex >= 0, table);
        Assert.True(saveIndex > installIndex, table);
        Assert.Contains("| `Graph.Full.SameSource` | PowerShell 7 | Install | 1.00x (4.00s) | 1.50x (6.00s) | Not in this gate | Not in this gate | InstallSameSource |", table, StringComparison.Ordinal);
        Assert.Contains("| `Az.Full.Save` | Windows PowerShell 5.1 | Save | 1.00x (5.80s) | Not equivalent | Failed | Skipped | SaveCapableProviders |", table, StringComparison.Ordinal);
    }

    private static string BuildScoreboardCsv()
        => string.Join(Environment.NewLine,
            "BenchmarkRole,Suite,Scenario,ComparisonScope,BenchmarkInterpretation,Host,Operation,ManagedStatus,ManagedMs,ModuleFastStatus,ModuleFastMs,PSResourceGetStatus,PSResourceGetMs,PowerShellGetStatus,PowerShellGetMs",
            "Scoreboard,SpeedGate,Graph.Full.SameSource,InstallSameSource,Install scoreboard,PowerShell7,Install,Succeeded,4000.00,Succeeded,6000.00,,,,",
            "Scoreboard,LifecycleGate,Graph.Authentication.InstallExact.NoOpForce,InstallWithModuleFast,Install scoreboard,WindowsPowerShell,InstallNoOp,Succeeded,600.00,Skipped,,Succeeded,900.00,,",
            "Scoreboard,HeavySaveGate,Az.Full.Save,SaveCapableProviders,Save scoreboard: compare save-capable providers only; ModuleFast has no equivalent save command.,WindowsPowerShell,Save,Succeeded,5801.61,Skipped,,Failed,,Skipped,");

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(static error => error.ToString()));
        Assert.Fail(message);
    }
}
