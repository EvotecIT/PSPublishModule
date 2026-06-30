using System.Management.Automation;
using PowerForge;

namespace PowerForge.Tests;

public sealed class BenchmarkServicesTests
{
    [Fact]
    public void SummaryService_CalculatesMedianAndComparisonRatio()
    {
        var samples = new[]
        {
            Sample("suite", "case", "Run", "Managed", 10),
            Sample("suite", "case", "Run", "Managed", 30),
            Sample("suite", "case", "Run", "Other", 20)
        };

        var service = new BenchmarkSummaryService();
        var summary = service.Summarize(samples);
        var comparison = service.Compare(summary, "Managed");

        var managed = Assert.Single(summary, r => r.Engine == "Managed");
        Assert.Equal(20, managed.MedianMs);
        var other = Assert.Single(comparison, r => r.Engine == "Other");
        Assert.Equal(1, other.Ratio);
    }

    [Fact]
    public void SummaryService_KeepsVariablesSeparateAndMarksPartialFailures()
    {
        var samples = new[]
        {
            Sample("suite", "case", "Run", "Managed", 10, new Dictionary<string, string?> { ["Rows"] = "10" }),
            Sample("suite", "case", "Run", "Managed", 20, new Dictionary<string, string?> { ["Rows"] = "20" }),
            new BenchmarkSample
            {
                RunId = "run",
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                Status = BenchmarkSampleStatus.Failed,
                Variables = new Dictionary<string, string?> { ["Rows"] = "10" }
            },
            Sample("suite", "case", "Run", "Other", 30, new Dictionary<string, string?> { ["Rows"] = "10" })
        };

        var summary = new BenchmarkSummaryService().Summarize(samples);

        Assert.Equal(3, summary.Length);
        Assert.Contains(summary, r => r.Engine == "Managed" && r.Variables["Rows"] == "10" && r.MedianMs == 10);
        Assert.Contains(summary, r => r.Engine == "Managed" && r.Variables["Rows"] == "20" && r.MedianMs == 20);
        Assert.Contains(summary, r => r.Engine == "Other" && r.Status == "Failed" && r.FailureCount == 1);
    }

    [Fact]
    public void DocumentUpdater_ReplacesMarkerBlock()
    {
        var root = CreateTempRoot();
        var readme = Path.Combine(root, "README.md");
        File.WriteAllText(readme, "Before\n<!-- BENCHMARK:demo:START -->\nold\n<!-- BENCHMARK:demo:END -->\nAfter\n");

        var result = new BenchmarkDocumentUpdater().UpdateBlock(readme, "demo", "| A |\n| --- |\n| 1 |\n");

        Assert.True(result.Changed);
        var text = File.ReadAllText(readme);
        Assert.Contains("| 1 |", text);
        Assert.DoesNotContain("old", text);
    }

    [Fact]
    public void MarkdownRenderer_IncludesVariablesColumn()
    {
        var markdown = new BenchmarkMarkdownRenderer().RenderSummaryTable(new[]
        {
            new BenchmarkSummaryRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                SampleCount = 1,
                Status = "Succeeded",
                Variables = new Dictionary<string, string?> { ["Rows"] = "100" }
            }
        });

        Assert.Contains("| Scenario | Variables |", markdown);
        Assert.Contains("Rows=100", markdown);
    }

    [Fact]
    public void GateService_UpdateAndVerifyDetectsRegression()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", MedianMs = 100 }
        });

        var service = new BenchmarkGateService();
        var update = service.Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            BaselineMode = BenchmarkBaselineMode.Update
        });
        Assert.True(update.Passed);
        Assert.True(update.BaselineUpdated);

        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", MedianMs = 200 }
        });
        var verify = service.Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            RelativeTolerance = 0.10
        });

        Assert.False(verify.Passed);
        Assert.Contains(verify.Metrics, m => m.Regressed);
    }

    [Fact]
    public void GateService_FailsWhenBaselineMetricDisappears()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(baselinePath, new
        {
            metrics = new Dictionary<string, double>
            {
                ["suite|case|Run|Managed|Current||MedianMs"] = 100
            }
        });
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", Status = "Failed" }
        });

        var verify = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath
        });

        Assert.False(verify.Passed);
        Assert.Contains(verify.Metrics, m => m.MissingInCurrent);
    }

    [Fact]
    public void GateService_NormalizesMetricNamesInKeys()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", MedianMs = 100 }
        });

        var service = new BenchmarkGateService();
        service.Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            Metric = "medianMs",
            BaselineMode = BenchmarkBaselineMode.Update
        });
        var verify = service.Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            Metric = "MedianMs"
        });

        Assert.True(verify.Passed);
        Assert.DoesNotContain(verify.Metrics, m => m.MissingInCurrent || m.MissingInBaseline);
    }

    [Fact]
    public void BenchmarkJson_WritesAndReadsStringStatuses()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "samples.json");
        BenchmarkJson.Write(path, new[] { Sample("suite", "case", "Run", "Managed", 1) });

        var json = File.ReadAllText(path);
        Assert.Contains("\"status\": \"Succeeded\"", json, StringComparison.Ordinal);
        var samples = BenchmarkJson.Read<BenchmarkSample[]>(path);
        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(samples).Status);
    }

    [Fact]
    public void Importer_ReadsBenchmarkDotNetCsv()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Mean\nWrite,1.500 ms\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        var row = Assert.Single(result.Summary);
        Assert.Equal("Write", row.Scenario);
        Assert.Equal(1.5, row.MedianMs);
    }

    [Fact]
    public void Importer_HonorsBenchmarkDotNetHeaderUnits()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Mean [us]\nWrite,1500\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        Assert.Equal(1.5, Assert.Single(result.Summary).MedianMs);
    }

    [Fact]
    public void Importer_PropagatesSuiteOverrideIntoNormalizedRun()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "run-report.json");
        BenchmarkJson.Write(path, new BenchmarkRunResult
        {
            Suite = "old",
            Samples = new[] { Sample("old", "case", "Run", "Managed", 1) }
        });

        var result = new BenchmarkResultImporter().Import(path, "new");

        Assert.Equal("new", result.Suite);
        Assert.Equal("new", Assert.Single(result.Samples).Suite);
        Assert.Equal("new", Assert.Single(result.Summary).Suite);
    }

    [Fact]
    public void Importer_ReadsBenchmarkDotNetJson()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "DisplayInfo": "Write",
      "Statistics": {
        "Mean": 1500000
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);

        var row = Assert.Single(result.Summary);
        Assert.Equal("Write", row.Scenario);
        Assert.Equal(1.5, row.MedianMs);
    }

    [Fact]
    public void Importer_UsesBenchmarkDotNetMedianAndScalesNanoseconds()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "DisplayInfo": "Write",
      "Statistics": {
        "Mean": 900,
        "Median": 500
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);

        Assert.Equal(0.0005, Assert.Single(result.Summary).MedianMs);
    }

    [Fact]
    public void Importer_ReadsNormalizedCsvDurations()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Scenario,Operation,Engine,Host,DurationMs\nWrite,Run,Managed,Current,12.5\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        Assert.Equal(12.5, Assert.Single(result.Summary).MedianMs);
    }

    [Fact]
    public void Importer_PreservesNormalizedCsvStatusSuiteAndVariables()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,Rows,Iteration,Status,DurationMs,Reason\nold,Write,Run,Managed,Current,10,0,Failed,12.5,boom\n");

        var result = new BenchmarkResultImporter().Import(csv);
        var sample = Assert.Single(result.Samples);

        Assert.Equal("old", result.Suite);
        Assert.Equal("old", sample.Suite);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Equal("boom", sample.Reason);
        Assert.Equal("10", sample.Variables["Rows"]);
        Assert.DoesNotContain("DurationMs", sample.Variables.Keys);
        Assert.Equal("Failed", Assert.Single(result.Summary).Status);
    }

    [Fact]
    public void Importer_DirectoryUsesSamplesWithoutMixingSummaryRows()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "samples.csv"), "Suite,Scenario,Operation,Engine,Host,Rows,Iteration,Status,DurationMs,Reason\nsuite,Write,Run,Managed,Current,10,0,Succeeded,12.5,\n");
        File.WriteAllText(Path.Combine(root, "summary.csv"), "Suite,Scenario,Operation,Engine,Host,Rows,SampleCount,FailureCount,Status,MedianMs,MeanMs,MinMs,MaxMs\nsuite,Write,Run,Managed,Current,10,1,0,Succeeded,12.5,12.5,12.5,12.5\n");

        var result = new BenchmarkResultImporter().Import(root);

        Assert.Single(result.Samples);
        Assert.Single(result.Summary);
        Assert.Equal("10", Assert.Single(result.Summary).Variables["Rows"]);
    }

    [Fact]
    public void DslRuntime_ResolvesShortAndLongEngineForms()
    {
        var script = ScriptBlock.Create(@"
benchmark 'short' {
    cases { case A @{} }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}

New-BenchmarkSuite 'long' {
    Add-BenchmarkCases { Add-BenchmarkCase B @{} }
    Add-BenchmarkAxis Operation Run
    Add-BenchmarkAxis Engine Managed
    Add-BenchmarkEngine Managed { Add-BenchmarkOperation Run { param($case, $run) } }
}
");

        var suites = PowerShellBenchmarkDslRuntime.Evaluate(script);
        var runner = new PowerShellBenchmarkRunner();

        Assert.Equal(2, suites.Length);
        Assert.Single(runner.Plan(suites[0]));
        Assert.Single(runner.Plan(suites[1]));
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_ExposesSuiteOverrideParameter()
    {
        var property = typeof(PSPublishModule.InvokeBenchmarkSuiteCommand).GetProperty("Suite");

        Assert.NotNull(property);
        Assert.Contains(property!.GetCustomAttributes(inherit: true), attribute => attribute is ParameterAttribute);
    }

    [Fact]
    public void Runner_RejectsUnsupportedHostAxis()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Host", Values = { "PowerShell7" } });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("only supports the current PowerShell host", ex.Message);
    }

    [Fact]
    public void Runner_FailsMalformedSuitesWithNoWork()
    {
        var suite = new PowerShellBenchmarkSuite { Name = "empty" };

        var ex = Assert.Throws<InvalidOperationException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("engine values", ex.Message);
    }

    [Fact]
    public void Runner_PreservesMultipleDataBlockItems()
    {
        var suite = CreateRunnableSuite();
        suite.Data = ScriptBlock.Create("1; 2; 3");
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) if (([object[]]$run.Data).Count -ne 3) { throw 'missing data' }");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
    }

    [Fact]
    public void Runner_DoesNotPreCreateOutputPathAsDirectory()
    {
        var suite = CreateRunnableSuite();
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) [IO.File]::WriteAllText($run.OutputPath, 'ok')");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
        Assert.True(File.Exists(Path.Combine(suite.OutputRoot, result.RunId, "Default", "Managed", "Run", "matrix", "0", "output")));
    }

    [Fact]
    public void Runner_SeparatesOutputPathsByMatrixVariables()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { 10, 20 } });
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) [IO.File]::WriteAllText($run.OutputPath, [string]$case.Rows)");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(2, result.Samples.Length);
        Assert.True(File.Exists(Path.Combine(suite.OutputRoot, result.RunId, "Default", "Managed", "Run", "Rows=10", "0", "output")));
        Assert.True(File.Exists(Path.Combine(suite.OutputRoot, result.RunId, "Default", "Managed", "Run", "Rows=20", "0", "output")));
    }

    [Fact]
    public void Runner_WritesMatrixVariablesToCsvArtifacts()
    {
        var suite = CreateRunnableSuite();
        suite.Artifacts = BenchmarkArtifactKind.Csv;
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { 10 } });

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var samplesCsv = File.ReadAllText(result.Artifacts["samples.csv"]);
        var summaryCsv = File.ReadAllText(result.Artifacts["summary.csv"]);
        Assert.Contains("Suite,Scenario,Operation,Engine,Host,Rows,Iteration,Status,DurationMs,Reason", samplesCsv);
        Assert.Contains("suite,Default,Run,Managed,Current,10,0,Succeeded", samplesCsv);
        Assert.Contains("Suite,Scenario,Operation,Engine,Host,Rows,SampleCount,FailureCount,Status,MedianMs,MeanMs,MinMs,MaxMs", summaryCsv);
    }

    [Fact]
    public void Runner_TreatsNonTerminatingHandlerErrorsAsFailures()
    {
        var suite = CreateRunnableSuite();
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) Write-Error 'boom'");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Contains("boom", sample.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RecordsWarmupFailureWithoutAbortingSuite()
    {
        var suite = CreateRunnableSuite();
        suite.WarmupCount = 1;
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("throw 'warmup failed'");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Equal(-1, sample.Iteration);
    }

    [Fact]
    public void Runner_WritesRunReportAfterArtifactMapIsPopulated()
    {
        var suite = CreateRunnableSuite();
        suite.Artifacts = BenchmarkArtifactKind.Json;

        var result = new PowerShellBenchmarkRunner().Run(suite);
        var report = BenchmarkJson.Read<BenchmarkRunResult>(result.Artifacts["run-report.json"]);

        Assert.Contains("run-report.json", report.Artifacts.Keys);
        Assert.Contains("samples.json", report.Artifacts.Keys);
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
