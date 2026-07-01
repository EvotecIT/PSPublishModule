using System.Management.Automation;
using System.Text;

namespace PowerForge.Tests;

public sealed class ManagedModuleSimpleBenchmarkScriptTests
{
    [Fact]
    public void MeasureScript_ListsPublicScenariosWithoutRepair()
    {
        using var ps = PowerShell.Create();
        using var temp = new TemporaryDirectory();
        var script = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "Measure-ManagedModuleBenchmark.ps1");
        ps.Runspace.SessionStateProxy.SetVariable("scriptText", File.ReadAllText(script));
        ps.Runspace.SessionStateProxy.SetVariable("outputPath", Path.Combine(temp.Path, "results.csv"));
        ps.Runspace.SessionStateProxy.SetVariable("outputRoot", Path.Combine(temp.Path, "runs"));
        ps.AddScript("& ([scriptblock]::Create($scriptText)) -ListScenarios -OutputPath $outputPath -OutputRoot $outputRoot");

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var names = results.Select(static result => result.Properties["Name"].Value?.ToString()).ToArray();
        Assert.Equal(new[] { "SingleModule", "GraphAuthentication", "Graph", "AzAccounts", "Az" }, names);
        Assert.DoesNotContain(names, static name => string.Equals(name, "Repair", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateReadmeScript_ReplacesBenchmarkMarkerWithManagedBaselineTable()
    {
        using var temp = new TemporaryDirectory();
        var resultPath = Path.Combine(temp.Path, "results.csv");
        var readmePath = Path.Combine(temp.Path, "README.MD");
        File.WriteAllText(resultPath, BuildResultCsv(), Encoding.UTF8);
        File.WriteAllText(
            readmePath,
            "Before" + Environment.NewLine +
            "<!-- managed-module-benchmark-table:start -->" + Environment.NewLine +
            "old" + Environment.NewLine +
            "<!-- managed-module-benchmark-table:end -->" + Environment.NewLine +
            "After",
            Encoding.UTF8);

        using var ps = PowerShell.Create();
        var script = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "Update-ManagedModuleBenchmarkReadme.ps1");
        ps.Runspace.SessionStateProxy.SetVariable("scriptText", File.ReadAllText(script));
        ps.Runspace.SessionStateProxy.SetVariable("resultPath", resultPath);
        ps.Runspace.SessionStateProxy.SetVariable("readmePath", readmePath);
        ps.AddScript("& ([scriptblock]::Create($scriptText)) -ResultPath $resultPath -ReadmePath $readmePath");

        _ = ps.Invoke();

        AssertNoErrors(ps);
        var readme = File.ReadAllText(readmePath);
        Assert.Contains("| Scenario | Host | Operation | Managed | ModuleFast | PSResourceGet | PowerShellGet | Result |", readme, StringComparison.Ordinal);
        Assert.Contains("| PSScriptAnalyzer | PowerShell 7 | Find | 1.00x (0.10s) | NotEquivalent | 1.50x (0.15s) | 4.00x (0.40s) | Managed fastest |", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("old", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Repair", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BenchmarkFolder_ContainsOnlySimplePublicBenchmarkScripts()
    {
        var folder = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules");
        var files = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(static name => name).ToArray();

        Assert.Equal(
            new[]
            {
                "Invoke-ManagedModuleBenchmarkMatrix.ps1",
                "Measure-ManagedModuleBenchmark.ps1",
                "README.md",
                "Update-ManagedModuleBenchmarkReadme.ps1"
            },
            files);
    }

    [Fact]
    public void MeasureScript_DoesNotFakeNativeCurrentUserInstallIsolation()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoRootLocator.Find(),
            "Benchmarks",
            "ManagedModules",
            "Measure-ManagedModuleBenchmark.ps1"));

        Assert.Contains("SkipNativeCurrentUserInstall", script, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowUserProfileInstall", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pass -AllowUserProfileInstall to measure native install providers", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Enter-NativeTemporaryCurrentUserProfile", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PFBenchmarkBackup", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MeasureScript_EmitsManagedPhaseColumnsForProfilerTriage()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoRootLocator.Find(),
            "Benchmarks",
            "ManagedModules",
            "Measure-ManagedModuleBenchmark.ps1"));

        Assert.Contains("ManagedPackageCount", script, StringComparison.Ordinal);
        Assert.Contains("ManagedDownloadMillisecondsSum", script, StringComparison.Ordinal);
        Assert.Contains("ManagedExtractionMillisecondsSum", script, StringComparison.Ordinal);
        Assert.Contains("ManagedDependencyMillisecondsRoot", script, StringComparison.Ordinal);
        Assert.Contains("ManagedPromotionMillisecondsSum", script, StringComparison.Ordinal);
        Assert.Contains("LastManagedBenchmarkResult", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BenchmarkMatrixScript_HasFocusedManagedModuleFastProfile()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoRootLocator.Find(),
            "Benchmarks",
            "ManagedModules",
            "Invoke-ManagedModuleBenchmarkMatrix.ps1"));

        Assert.Contains("ManagedVsModuleFast", script, StringComparison.Ordinal);
        Assert.Contains("$Operation = @('Install')", script, StringComparison.Ordinal);
        Assert.Contains("$Engine = @('Managed', 'ModuleFast')", script, StringComparison.Ordinal);
        Assert.Contains("$ModuleFastSource = 'https://pwsh.gallery/index.json'", script, StringComparison.Ordinal);
        Assert.Contains("[string] $ComparisonProfile = 'ManagedVsModuleFast'", script, StringComparison.Ordinal);
        Assert.Contains("ModuleFastModulePath", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MeasureScript_EmitsBenchmarkProvenanceColumns()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoRootLocator.Find(),
            "Benchmarks",
            "ManagedModules",
            "Measure-ManagedModuleBenchmark.ps1"));

        Assert.Contains("RepositoryUri = $RepositoryUri", script, StringComparison.Ordinal);
        Assert.Contains("ModuleFastSource = if ($EngineName -eq 'ModuleFast')", script, StringComparison.Ordinal);
        Assert.Contains("ModuleFastModulePath = if ($EngineName -eq 'ModuleFast')", script, StringComparison.Ordinal);
        Assert.Contains("EngineCommandPath = $engineMetadata.CommandPath", script, StringComparison.Ordinal);
        Assert.Contains("EngineModuleBase = $engineMetadata.ModuleBase", script, StringComparison.Ordinal);
        Assert.Contains("EngineModuleVersion = $engineMetadata.ModuleVersion", script, StringComparison.Ordinal);
    }

    private static string BuildResultCsv()
        => string.Join(Environment.NewLine,
            "TimestampUtc,Host,Scenario,ScenarioLabel,ModuleName,Version,Operation,Engine,Iteration,Status,Milliseconds,Seconds,Reason",
            "2026-06-29T00:00:00Z,PowerShell 7,SingleModule,PSScriptAnalyzer,PSScriptAnalyzer,1.25.0,Find,Managed,1,Succeeded,100,0.1,",
            "2026-06-29T00:00:00Z,PowerShell 7,SingleModule,PSScriptAnalyzer,PSScriptAnalyzer,1.25.0,Find,ModuleFast,1,NotEquivalent,0,0,No equivalent",
            "2026-06-29T00:00:00Z,PowerShell 7,SingleModule,PSScriptAnalyzer,PSScriptAnalyzer,1.25.0,Find,PSResourceGet,1,Succeeded,150,0.15,",
            "2026-06-29T00:00:00Z,PowerShell 7,SingleModule,PSScriptAnalyzer,PSScriptAnalyzer,1.25.0,Find,PowerShellGet,1,Succeeded,400,0.4,");

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(static error => error.ToString()));
        Assert.Fail(message);
    }
}
