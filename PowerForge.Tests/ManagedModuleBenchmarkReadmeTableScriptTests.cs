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
        ps.AddScript("& ([scriptblock]::Create($scriptText)) -ScoreboardPath $scoreboardPath -PublicComparison");

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var table = Assert.Single(results).BaseObject.ToString() ?? string.Empty;
        Assert.Contains("| Scenario | Host | Operation | Managed | ModuleFast | PSResourceGet | PowerShellGet | Result |", table, StringComparison.Ordinal);
        Assert.Contains("| ThreadJob single module | PowerShell 7 | Find | 1.00x (0.10s) | Not equivalent | 1.50x (0.15s) | 4.00x (0.40s) | Managed fastest |", table, StringComparison.Ordinal);
        Assert.Contains("| ThreadJob single module | PowerShell 7 | Save force | 1.00x (0.02s) | Not equivalent | Not equivalent | 65.00x (1.30s) | Managed fastest |", table, StringComparison.Ordinal);
        Assert.Contains("| Graph full family | Windows PowerShell 5.1 | Save | 1.00x (5.80s) | Not equivalent | Failed | Skipped | Managed only successful |", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Graph.Full.SameSource", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Repair", table, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadmeTable_PublicComparisonSplitsRowsByOperationFamily()
    {
        using var temp = new TemporaryDirectory();
        var scoreboardPath = Path.Combine(temp.Path, "suite-scoreboard.csv");
        File.WriteAllText(scoreboardPath, BuildScoreboardCsv(), Encoding.UTF8);

        using var ps = PowerShell.Create();
        var script = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "Export-ManagedModuleBenchmarkReadmeTable.ps1");
        ps.Runspace.SessionStateProxy.SetVariable("scriptText", File.ReadAllText(script));
        ps.Runspace.SessionStateProxy.SetVariable("scoreboardPath", scoreboardPath);
        ps.AddScript("& ([scriptblock]::Create($scriptText)) -ScoreboardPath $scoreboardPath -PublicComparison -SplitByOperation");

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var table = Assert.Single(results).BaseObject.ToString() ?? string.Empty;
        var findIndex = table.IndexOf("#### Find", StringComparison.Ordinal);
        var installIndex = table.IndexOf("#### Install", StringComparison.Ordinal);
        var saveIndex = table.IndexOf("#### Save", StringComparison.Ordinal);
        var updateIndex = table.IndexOf("#### Update", StringComparison.Ordinal);
        Assert.True(findIndex >= 0, table);
        Assert.True(installIndex > findIndex, table);
        Assert.True(saveIndex > installIndex, table);
        Assert.True(updateIndex > saveIndex, table);
        Assert.DoesNotContain("#### Repair", table, StringComparison.Ordinal);
        Assert.Contains("| ThreadJob single module | PowerShell 7 | Update force | 1.00x (0.60s) | Not equivalent | 1.33x (0.80s) | 2.00x (1.20s) | Managed fastest |", table, StringComparison.Ordinal);
        Assert.Contains("| Graph full family | Windows PowerShell 5.1 | Save | 1.00x (5.80s) | Not equivalent | Failed | Skipped | Managed only successful |", table, StringComparison.Ordinal);
    }

    private static string BuildScoreboardCsv()
        => string.Join(Environment.NewLine,
            "BenchmarkRole,Suite,Scenario,ComparisonScope,BenchmarkInterpretation,Host,Operation,ManagedStatus,ManagedMs,ModuleFastStatus,ModuleFastMs,PSResourceGetStatus,PSResourceGetMs,PowerShellGetStatus,PowerShellGetMs",
            "Scoreboard,PublicComparisonGate,ThreadJob.SingleModule.PublicComparison,PublicComparison,Public comparison,PowerShell7,Find,Succeeded,100.00,Skipped,,Succeeded,150.00,Succeeded,400.00",
            "Scoreboard,PublicComparisonGate,ThreadJob.SingleModule.PublicComparison,PublicComparison,Public comparison,PowerShell7,InstallNoOp,Succeeded,120.00,Succeeded,180.00,Succeeded,220.00,Succeeded,600.00",
            "Scoreboard,PublicComparisonGate,ThreadJob.SingleModule.PublicComparison,PublicComparison,Public comparison,PowerShell7,SaveForce,Succeeded,20.00,Skipped,,Skipped,,Succeeded,1300.00",
            "Scoreboard,PublicComparisonGate,ThreadJob.SingleModule.PublicComparison,PublicComparison,Public comparison,PowerShell7,UpdateForce,Succeeded,600.00,Skipped,,Succeeded,800.00,Succeeded,1200.00",
            "Scoreboard,PublicComparisonGate,Graph.Full.MultiModule.PublicComparison,PublicComparison,Public comparison,WindowsPowerShell,Save,Succeeded,5801.61,Skipped,,Failed,,Skipped,",
            "Scoreboard,SpeedGate,Graph.Full.SameSource,InstallSameSource,Install scoreboard,PowerShell7,Install,Succeeded,4000.00,Succeeded,6000.00,,,,",
            "Diagnostic,RepairGate,ThreadJob.Repair.CleanupPlanning,ManagedOnlyRepairPlan,Repair planning,PowerShell7,RepairPlan,Succeeded,200.00,Skipped,,Skipped,,Skipped,");

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(static error => error.ToString()));
        Assert.Fail(message);
    }
}
