using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void GateService_HonorsHigherIsBetterMetrics()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(baselinePath, new
        {
            metrics = new Dictionary<string, double>
            {
                ["suite|case|Run|Managed|Current||||RowsPerSecond"] = 100
            }
        });
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                Metrics = new Dictionary<string, double> { ["RowsPerSecond"] = 80 }
            }
        });

        var verify = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            Metric = "RowsPerSecond",
            RelativeTolerance = 0.10
        });

        Assert.False(verify.Passed);
        Assert.Contains(verify.Metrics, metric => metric.Regressed);
        Assert.Contains(verify.Messages, message => message.Contains("HigherIsBetter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GateService_EscapesVariableDelimitersInKeys()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                MedianMs = 1,
                Variables = new Dictionary<string, string?> { ["A"] = "1;B=2" }
            },
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                MedianMs = 2,
                Variables = new Dictionary<string, string?> { ["A"] = "1", ["B"] = "2" }
            }
        });

        var update = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            BaselineMode = BenchmarkBaselineMode.Update
        });

        Assert.True(update.Passed);
        Assert.Equal(2, update.Metrics.Length);
        Assert.Equal(2, update.Metrics.Select(metric => metric.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(update.Metrics, metric => metric.Key.Contains(@"A=1\;B\=2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GateService_DefaultKeysKeepConflictingVariableNames()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                MedianMs = 1,
                Variables = new Dictionary<string, string?> { ["Engine"] = "A" }
            },
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                MedianMs = 2,
                Variables = new Dictionary<string, string?> { ["Engine"] = "B" }
            }
        });

        var update = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            BaselineMode = BenchmarkBaselineMode.Update
        });

        Assert.True(update.Passed);
        Assert.Equal(2, update.Metrics.Length);
        Assert.Contains(update.Metrics, metric => metric.Key.Contains("Engine=A", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(update.Metrics, metric => metric.Key.Contains("Engine=B", StringComparison.OrdinalIgnoreCase));
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
    public void GateService_CanonicalizesCustomMetricNamesInKeys()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                Metrics = new Dictionary<string, double> { ["RowsPerSecond"] = 100 }
            }
        });

        var service = new BenchmarkGateService();
        service.Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            Metric = "RowsPerSecond",
            BaselineMode = BenchmarkBaselineMode.Update
        });
        var verify = service.Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            Metric = "rowspersecond"
        });

        Assert.True(verify.Passed);
        Assert.DoesNotContain(verify.Metrics, m => m.MissingInCurrent || m.MissingInBaseline);
    }

    [Fact]
    public void GateService_FailsWhenNoMetricValuesAreProduced()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", MedianMs = 100 }
        });

        var update = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            Metric = "P95Ms",
            BaselineMode = BenchmarkBaselineMode.Update
        });

        Assert.False(update.Passed);
        Assert.False(update.BaselineUpdated);
        Assert.False(File.Exists(baselinePath));
        Assert.Contains(update.Messages, message => message.Contains("No benchmark metric values", StringComparison.OrdinalIgnoreCase));

        BenchmarkJson.Write(baselinePath, new { metrics = new Dictionary<string, double>() });
        var verify = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            Metric = "P95Ms"
        });

        Assert.False(verify.Passed);
        Assert.Contains(verify.Messages, message => message.Contains("No benchmark metric values", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GateService_FailsWhenSuccessfulRowsMissRequestedMetric()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                Status = "Succeeded",
                Metrics = new Dictionary<string, double> { ["RowsPerSecond"] = 100 }
            },
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                Status = "Succeeded"
            }
        });

        var update = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            Metric = "RowsPerSecond",
            BaselineMode = BenchmarkBaselineMode.Update
        });

        Assert.False(update.Passed);
        Assert.False(update.BaselineUpdated);
        Assert.Contains(update.Messages, message => message.Contains("Other", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(update.Messages, message => message.Contains("RowsPerSecond", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GateService_FailsWhenMetricValueIsNotFinite()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        File.WriteAllText(summaryPath, """
[
  {
    "suite": "suite",
    "scenario": "case",
    "operation": "Run",
    "engine": "Managed",
    "host": "Current",
    "status": "Succeeded",
    "metrics": { "RowsPerSecond": 1e999 }
  }
]
""");

        var update = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            Metric = "RowsPerSecond",
            BaselineMode = BenchmarkBaselineMode.Update
        });

        Assert.False(update.Passed);
        Assert.False(update.BaselineUpdated);
        Assert.DoesNotContain(update.Metrics, metric => string.Equals(metric.Key, "RowsPerSecond", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(update.Messages, message => message.Contains("RowsPerSecond", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GateService_IgnoresSkippedRowsWhenMetricIsMissing()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                Status = "Succeeded",
                Metrics = new Dictionary<string, double> { ["RowsPerSecond"] = 100 }
            },
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "SkippedEngine",
                Host = "Current",
                Status = "Skipped"
            }
        });

        var update = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            Metric = "RowsPerSecond",
            BaselineMode = BenchmarkBaselineMode.Update
        });

        Assert.True(update.Passed);
        Assert.True(update.BaselineUpdated);
        Assert.Single(update.Metrics);
        Assert.DoesNotContain(update.Messages, message => message.Contains("SkippedEngine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TestBenchmarkGateCommand_WhatIfSkipsBaselineUpdate()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", MedianMs = 100 }
        });
        var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new System.Management.Automation.Runspaces.SessionStateCmdletEntry("Test-BenchmarkGate", typeof(PSPublishModule.TestBenchmarkGateCommand), helpFileName: null));
        using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        using var ps = System.Management.Automation.PowerShell.Create(runspace);
        ps.AddCommand("Test-BenchmarkGate")
            .AddParameter("SummaryPath", summaryPath)
            .AddParameter("BaselinePath", baselinePath)
            .AddParameter("Update")
            .AddParameter("WhatIf", true);

        ps.Invoke();

        Assert.False(File.Exists(baselinePath));
    }

    [Fact]
    public void ImportBenchmarkResultCommand_WhatIfSkipsNormalizedOutputWrite()
    {
        var root = CreateTempRoot();
        var samplesPath = Path.Combine(root, "samples.json");
        var outputPath = Path.Combine(root, "normalized.json");
        BenchmarkJson.Write(samplesPath, new[]
        {
            Sample("suite", "case", "Run", "Managed", 100)
        });
        File.WriteAllText(outputPath, "old");
        var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new System.Management.Automation.Runspaces.SessionStateCmdletEntry("Import-BenchmarkResult", typeof(PSPublishModule.ImportBenchmarkResultCommand), helpFileName: null));
        using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        using var ps = System.Management.Automation.PowerShell.Create(runspace);
        ps.AddCommand("Import-BenchmarkResult")
            .AddParameter("Path", samplesPath)
            .AddParameter("OutputPath", outputPath)
            .AddParameter("WhatIf", true);

        var result = Assert.IsType<BenchmarkRunResult>(Assert.Single(ps.Invoke()).BaseObject);

        Assert.Equal("old", File.ReadAllText(outputPath));
        Assert.DoesNotContain("normalized.json", result.Artifacts.Keys);
    }

    [Fact]
    public void GateService_ResolvesGroupedVariablesCaseInsensitively()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                MedianMs = 100,
                Variables = new Dictionary<string, string?> { ["Rows"] = "10" }
            }
        });

        var service = new BenchmarkGateService();
        service.Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            GroupBy = new[] { "Variables.rows" },
            BaselineMode = BenchmarkBaselineMode.Update
        });

        var verify = service.Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            GroupBy = new[] { "Variables.rows" }
        });

        Assert.True(verify.Passed);
        Assert.DoesNotContain(verify.Metrics, metric => metric.MissingInBaseline || metric.MissingInCurrent);
    }

    [Fact]
    public void SummaryService_ReadsCustomMetricsCaseInsensitively()
    {
        var row = new BenchmarkSummaryRow
        {
            Metrics = new Dictionary<string, double>
            {
                ["RowsPerSecond"] = 42
            }
        };

        Assert.Equal(42, BenchmarkSummaryService.GetMetricValue(row, "rowspersecond"));
    }

    [Fact]
    public void SummaryService_DropsMetricsMissingFromSomeSuccessfulSamples()
    {
        var first = Sample("suite", "case", "Run", "Managed", 10);
        first.Metrics["Rows"] = 1;
        var second = Sample("suite", "case", "Run", "Managed", 20);

        var row = Assert.Single(new BenchmarkSummaryService().Summarize(new[] { first, second }));

        Assert.DoesNotContain("Rows", row.Metrics.Keys);
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
    public void BenchmarkJson_ReadsPascalCasePowerShellJson()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "run-report.json");
        File.WriteAllText(path, """
{
  "Suite": "demo",
  "Samples": [
    {
      "Suite": "demo",
      "Scenario": "case",
      "Operation": "Run",
      "Engine": "Managed",
      "Host": "Current",
      "Iteration": 0,
      "Status": "Succeeded",
      "DurationMs": 1.25
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);

        Assert.Equal("demo", result.Suite);
        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
        Assert.Equal(1.25, Assert.Single(result.Summary).MedianMs);
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
    public void Importer_HonorsBenchmarkDotNetSecondHeaderUnits()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Median [s],Mean [s]\nWrite,1.5,9\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        Assert.Equal(1500, Assert.Single(result.Summary).MedianMs);
        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetParametersNamedLikeMetadata()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Job,Engine,Operation,Host,Mean\nWrite,JobA,ParamEngine,ParamOperation,ParamHost,1.500 ms\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);
        Assert.Equal("Write", sample.Scenario);
        Assert.Equal("JobA", sample.Engine);
        Assert.Equal("Run", sample.Operation);
        Assert.Equal(string.Empty, sample.Host);
        Assert.Equal("ParamEngine", sample.Variables["Engine"]);
        Assert.Equal("ParamOperation", sample.Variables["Operation"]);
        Assert.Equal("ParamHost", sample.Variables["Host"]);
        Assert.Equal("JobA", row.Engine);
        Assert.Equal("ParamEngine", row.Variables["Engine"]);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetStatusParameter()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Status,Mean\nWrite,Failed,1.500 ms\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");
        var sample = Assert.Single(result.Samples);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, sample.Status);
        Assert.Equal("Failed", sample.Variables["Status"]);
        Assert.Equal(1.5, sample.DurationMs);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetSuiteParameter()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Suite,Mean\nWrite,ParameterSuite,1.500 ms\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");
        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);

        Assert.Equal("demo", sample.Suite);
        Assert.Equal("demo", row.Suite);
        Assert.Equal("ParameterSuite", sample.Variables["Suite"]);
        Assert.Equal("ParameterSuite", row.Variables["Suite"]);
    }

    [Fact]
    public void Importer_KeepsBenchmarkDotNetCsvDetectionWithReservedParameterNames()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Iteration,DurationMs,Mean\nWrite,Cold,InputName,1.500 ms\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);
        Assert.Equal("Write", sample.Scenario);
        Assert.Equal(1.5, sample.DurationMs);
        Assert.Equal("Cold", sample.Variables["Iteration"]);
        Assert.Equal("InputName", sample.Variables["DurationMs"]);
        Assert.Equal(1.5, row.MedianMs);
    }

    [Fact]
    public void Importer_PrefersBenchmarkDotNetStatisticColumnsOverAliasParameters()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,MedianMs,MeanMs,Mean [ms]\nWrite,ParamMedian,ParamMean,1.500\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);
        Assert.Equal(1.5, sample.DurationMs);
        Assert.Equal(1.5, row.MedianMs);
        Assert.Equal("ParamMedian", sample.Variables["MedianMs"]);
        Assert.Equal("ParamMean", sample.Variables["MeanMs"]);
        Assert.Equal("ParamMedian", row.Variables["MedianMs"]);
        Assert.Equal("ParamMean", row.Variables["MeanMs"]);
    }

    [Fact]
    public void Importer_FallsBackPastBareBenchmarkDotNetStatisticParameters()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Median,Mean [ms]\nWrite,ParamMedian,1.500\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);
        Assert.Equal(BenchmarkSampleStatus.Succeeded, sample.Status);
        Assert.Equal(1.5, sample.DurationMs);
        Assert.Equal(1.5, row.MedianMs);
        Assert.Equal("ParamMedian", sample.Variables["Median"]);
    }

    [Fact]
    public void Importer_PrefersDurationMsForNormalizedSampleCsv()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,Iteration,Status,DurationMs,Reason,Mean\nsuite,case,Run,Managed,Current,0,Succeeded,12,,999\n");

        var result = new BenchmarkResultImporter().Import(csv);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(12, sample.DurationMs);
        Assert.Equal(999, sample.Metrics["Mean"]);
        Assert.Equal(12, Assert.Single(result.Summary).MedianMs);
    }

}
