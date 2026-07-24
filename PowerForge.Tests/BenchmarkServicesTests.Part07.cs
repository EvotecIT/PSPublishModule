using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void Runner_RejectsSkippedComparisonBaselineBeforeMeasurement()
    {
        var suite = CreateRunnableSuite();
        var output = Path.Combine(suite.OutputRoot, "skipped-baseline-side-effect.txt");
        var other = new PowerShellBenchmarkEngine { Name = "Other" };
        other.Operations["Run"] = ScriptBlock.Create($"[IO.File]::WriteAllText('{output.Replace("'", "''")}', 'executed')");
        suite.Engines.Add(other);
        suite.Axes.Single(axis => axis.Name == "Engine").Values.Add("Other");
        suite.Skip = ScriptBlock.Create("param($case) $case.Engine -eq 'Managed'");
        suite.Comparisons.Add(new PowerShellBenchmarkComparison { Dimension = "Engine", Baseline = "Managed" });

        var ex = Assert.Throws<InvalidOperationException>(() => new PowerShellBenchmarkRunner().Run(suite));

        Assert.Contains("Managed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no runnable work items", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Runner_RejectsSkippedComparisonBaselinePerLaneBeforeMeasurement()
    {
        var suite = CreateRunnableSuite();
        var output = Path.Combine(suite.OutputRoot, "skipped-lane-baseline-side-effect.txt");
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { 1, 2 } });
        var other = new PowerShellBenchmarkEngine { Name = "Other" };
        other.Operations["Run"] = ScriptBlock.Create($"[IO.File]::WriteAllText('{output.Replace("'", "''")}', 'executed')");
        suite.Engines.Add(other);
        suite.Axes.Single(axis => axis.Name == "Engine").Values.Add("Other");
        suite.Skip = ScriptBlock.Create("param($case) $case.Engine -eq 'Managed' -and $case.Rows -eq 2");
        suite.Comparisons.Add(new PowerShellBenchmarkComparison { Dimension = "Engine", Baseline = "Managed" });

        var ex = Assert.Throws<InvalidOperationException>(() => new PowerShellBenchmarkRunner().Run(suite));

        Assert.Contains("Managed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rows=2", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Runner_RejectsSkippedComparisonBaselineForStringLaneBeforeMeasurement()
    {
        var suite = CreateRunnableSuite();
        var output = Path.Combine(suite.OutputRoot, "string-lane-side-effect.txt");
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Input", Values = { "alpha", "beta" } });
        var other = new PowerShellBenchmarkEngine { Name = "Other" };
        other.Operations["Run"] = ScriptBlock.Create($"[IO.File]::WriteAllText('{output.Replace("'", "''")}', 'executed')");
        suite.Engines.Add(other);
        suite.Axes.Single(axis => axis.Name == "Engine").Values.Add("Other");
        suite.Skip = ScriptBlock.Create("param($case) $case.Engine -eq 'Managed' -and $case.Input -eq 'beta'");
        suite.Comparisons.Add(new PowerShellBenchmarkComparison { Dimension = "Engine", Baseline = "Managed" });

        var ex = Assert.Throws<InvalidOperationException>(() => new PowerShellBenchmarkRunner().Run(suite));

        Assert.Contains("Managed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Input=beta", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Runner_AllowsFullySkippedComparisonGroups()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { 1, 2 } });
        var other = new PowerShellBenchmarkEngine { Name = "Other" };
        other.Operations["Run"] = ScriptBlock.Create("param($case, $run)");
        suite.Engines.Add(other);
        suite.Axes.Single(axis => axis.Name == "Engine").Values.Add("Other");
        suite.Skip = ScriptBlock.Create("param($case) $case.Rows -eq 2");
        suite.Comparisons.Add(new PowerShellBenchmarkComparison { Dimension = "Engine", Baseline = "Managed" });

        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(4, result.Samples.Length);
        Assert.Equal(2, result.Samples.Count(sample => sample.Status == BenchmarkSampleStatus.Skipped));
        Assert.DoesNotContain(result.Comparison, row => row.Variables.TryGetValue("Rows", out var rows) && rows == "2");
    }

    [Fact]
    public void Runner_RejectsUnknownReadmeRenderers()
    {
        var suite = CreateRunnableSuite();
        var output = Path.Combine(suite.OutputRoot, "readme-side-effect.txt");
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create($"[IO.File]::WriteAllText('{output.Replace("'", "''")}', 'executed')");
        var readme = Path.Combine(CreateTempRoot(), "README.md");
        File.WriteAllText(readme, "<!-- BENCHMARK:results:START -->\nold\n<!-- BENCHMARK:results:END -->\n");
        suite.ReadmeBlocks.Add(new PowerShellBenchmarkReadmeBlock
        {
            Path = readme,
            BlockId = "results",
            Renderer = "ComparsionTable"
        });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Run(suite));

        Assert.Contains("ComparsionTable", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Runner_PreflightsReadmeBlocksBeforeMeasurement()
    {
        var suite = CreateRunnableSuite();
        var output = Path.Combine(suite.OutputRoot, "readme-marker-side-effect.txt");
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create($"[IO.File]::WriteAllText('{output.Replace("'", "''")}', 'executed')");
        var readme = Path.Combine(CreateTempRoot(), "README.md");
        File.WriteAllText(readme, "<!-- BENCHMARK:other:START -->\nold\n<!-- BENCHMARK:other:END -->\n");
        suite.ReadmeBlocks.Add(new PowerShellBenchmarkReadmeBlock
        {
            Path = readme,
            BlockId = "results",
            Renderer = "SummaryTable"
        });

        var ex = Assert.Throws<InvalidOperationException>(() => new PowerShellBenchmarkRunner().Run(suite));

        Assert.Contains("results", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Runner_DoesNotUpdateReadmeBlocksWhenValidationFails()
    {
        var suite = CreateRunnableSuite();
        suite.Artifacts = BenchmarkArtifactKind.Json;
        suite.Validate = ScriptBlock.Create("throw 'expected validation failure'");
        var readme = Path.Combine(CreateTempRoot(), "README.md");
        const string original = "<!-- BENCHMARK:results:START -->\nvalidated content\n<!-- BENCHMARK:results:END -->\n";
        File.WriteAllText(readme, original);
        suite.ReadmeBlocks.Add(new PowerShellBenchmarkReadmeBlock
        {
            Path = readme,
            BlockId = "results",
            Renderer = "SummaryTable"
        });

        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Failed, Assert.Single(result.Samples).Status);
        Assert.Equal(original, File.ReadAllText(readme));
        Assert.True(result.Artifacts.ContainsKey("summary.json"));
    }

    [Fact]
    public void TemporaryUserExecutor_CapturesFileBackedCallerModules()
    {
        var modulePath = typeof(PSPublishModule.TestBenchmarkGateCommand).Assembly.Location;
        var previousRunspace = Runspace.DefaultRunspace;
        using var runspace = RunspaceFactory.CreateRunspace();
        runspace.Open();
        Runspace.DefaultRunspace = runspace;
        try
        {
            using var powerShell = PowerShell.Create();
            powerShell.Runspace = runspace;
            powerShell.AddCommand("Import-Module").AddArgument(modulePath).AddParameter("Force");
            powerShell.Invoke();
            Assert.False(powerShell.HadErrors);

            var modulePaths = PowerShellBenchmarkTemporaryUserExecutor.GetImportableCallerModulePaths();

            Assert.Contains(modulePaths, path => string.Equals(path, Path.GetFullPath(modulePath), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Runspace.DefaultRunspace = previousRunspace;
        }
    }

    [Fact]
    public void TemporaryUserExecutor_KeepsReadmePathsPositionalForChild()
    {
        var request = new PowerShellBenchmarkTemporaryUserRequest
        {
            ReadmePaths = new[] { "README.md", "README.md", "docs.md", string.Empty }
        };

        var paths = PowerShellBenchmarkTemporaryUserExecutor.GetReadmePathsForChild(request);

        Assert.Equal(new[] { "README.md", "README.md", "docs.md" }, paths);
    }

    [Fact]
    public void TemporaryUserExecutor_RewritesEnrichedMetadataArtifact()
    {
        var root = CreateTempRoot();
        var reportPath = Path.Combine(root, "run-report.json");
        var metadataPath = Path.Combine(root, "metadata.json");
        var result = new BenchmarkRunResult
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["profile"] = PowerShellBenchmarkProfileKind.TemporaryLocalUser.ToString(),
                ["cleanup"] = PowerShellBenchmarkCleanupMode.KeepOnFailure.ToString()
            },
            Artifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["run-report.json"] = reportPath,
                ["metadata.json"] = metadataPath
            }
        };
        BenchmarkJson.Write(reportPath, new BenchmarkRunResult
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["profile"] = PowerShellBenchmarkProfileKind.Current.ToString()
            }
        });
        BenchmarkJson.Write(metadataPath, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["profile"] = PowerShellBenchmarkProfileKind.Current.ToString()
        });

        PowerShellBenchmarkTemporaryUserExecutor.RewriteEnrichedArtifacts(result);

        var report = BenchmarkJson.Read<BenchmarkRunResult>(reportPath);
        var metadata = BenchmarkJson.Read<Dictionary<string, string>>(metadataPath);
        Assert.Equal(PowerShellBenchmarkProfileKind.TemporaryLocalUser.ToString(), report.Metadata["profile"]);
        Assert.Equal(PowerShellBenchmarkProfileKind.TemporaryLocalUser.ToString(), metadata["profile"]);
        Assert.Equal(PowerShellBenchmarkCleanupMode.KeepOnFailure.ToString(), metadata["cleanup"]);
    }

    [Fact]
    public void TemporaryUserExecutor_RecoversLatestRunReportForLateChildFailures()
    {
        var root = CreateTempRoot();
        var runRoot = Path.Combine(root, "out", "run");
        Directory.CreateDirectory(runRoot);
        var reportPath = Path.Combine(runRoot, "run-report.json");
        var resultPath = Path.Combine(root, "result.json");
        BenchmarkJson.Write(reportPath, new BenchmarkRunResult
        {
            RunId = "run",
            Suite = "suite",
            Samples = new[]
            {
                new BenchmarkSample
                {
                    RunId = "run",
                    Suite = "suite",
                    Scenario = "case",
                    Operation = "Run",
                    Engine = "Managed",
                    Host = "Current",
                    Status = BenchmarkSampleStatus.Succeeded,
                    DurationMs = 1
                }
            }
        });

        var copied = PowerShellBenchmarkTemporaryUserExecutor.TryCopyLatestRunReport(Path.Combine(root, "out"), resultPath, DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.True(copied);
        Assert.Equal("run", BenchmarkJson.Read<BenchmarkRunResult>(resultPath).RunId);
    }

    [Fact]
    public void TemporaryUserExecutor_DoesNotRecoverStaleRunReport()
    {
        var root = CreateTempRoot();
        var runRoot = Path.Combine(root, "out", "old-run");
        Directory.CreateDirectory(runRoot);
        var reportPath = Path.Combine(runRoot, "run-report.json");
        var resultPath = Path.Combine(root, "result.json");
        BenchmarkJson.Write(reportPath, new BenchmarkRunResult
        {
            RunId = "old-run",
            Suite = "suite",
            Samples = new[] { Sample("suite", "case", "Run", "Managed", 1) }
        });
        File.SetLastWriteTimeUtc(reportPath, DateTime.UtcNow.AddMinutes(-10));

        var copied = PowerShellBenchmarkTemporaryUserExecutor.TryCopyLatestRunReport(Path.Combine(root, "out"), resultPath, DateTimeOffset.UtcNow);

        Assert.False(copied);
        Assert.False(File.Exists(resultPath));
    }

    [Fact]
    public void TemporaryUserExecutor_DoesNotUseWindowsAppsPowerShellAliasForTempUser()
    {
        var root = CreateTempRoot();
        var windowsApps = Path.Combine(root, "WindowsApps", "pwsh.exe");
        var programFilesPwsh = Path.Combine(root, "ProgramFiles", "PowerShell", "7", "pwsh.exe");
        var windowsPowerShell = Path.Combine(root, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(windowsApps)!);
        Directory.CreateDirectory(Path.GetDirectoryName(windowsPowerShell)!);
        File.WriteAllText(windowsApps, string.Empty);
        File.WriteAllText(windowsPowerShell, string.Empty);

        var resolved = PowerShellBenchmarkTemporaryUserExecutor.ResolvePowerShellExecutable(windowsApps, programFilesPwsh, windowsPowerShell);

        Assert.Null(resolved);
    }

    private static BenchmarkSample Sample(
        string suite,
        string scenario,
        string operation,
        string engine,
        double durationMs,
        Dictionary<string, string?>? variables = null)
        => new()
        {
            RunId = "run",
            Suite = suite,
            Scenario = scenario,
            Operation = operation,
            Engine = engine,
            Host = "Current",
            Status = BenchmarkSampleStatus.Succeeded,
            DurationMs = durationMs,
            Variables = variables ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        };

    private static PowerShellBenchmarkSuite CreateRunnableSuite()
    {
        var engine = new PowerShellBenchmarkEngine { Name = "Managed" };
        engine.Operations["Run"] = ScriptBlock.Create("param($case, $run)");
        var suite = new PowerShellBenchmarkSuite
        {
            Name = "suite",
            OutputRoot = CreateTempRoot(),
            WarmupCount = 0,
            IterationCount = 1,
            Artifacts = BenchmarkArtifactKind.None
        };
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Engine", Values = { "Managed" } });
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Operation", Values = { "Run" } });
        suite.Engines.Add(engine);
        return suite;
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "pf-benchmark-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
