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
    public void SummaryService_RejectsMissingComparisonBaseline()
    {
        var summary = new BenchmarkSummaryService().Summarize(new[]
        {
            Sample("suite", "case", "Run", "Other", 20)
        });

        var ex = Assert.Throws<InvalidOperationException>(() => new BenchmarkSummaryService().Compare(summary, "Managed"));

        Assert.Contains("Managed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SummaryService_RejectsComparisonBaselineWithNoMetric()
    {
        var summary = new[]
        {
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                Status = "Failed"
            },
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                Status = "Succeeded",
                MedianMs = 10
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new BenchmarkSummaryService().Compare(summary, "Managed"));

        Assert.Contains("MedianMs", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void SummaryService_KeepsOperatingSystemsSeparate()
    {
        var windows = Sample("suite", "case", "Run", "Managed", 10);
        windows.Os = "Windows";
        var linux = Sample("suite", "case", "Run", "Managed", 30);
        linux.Os = "Linux";

        var summary = new BenchmarkSummaryService().Summarize(new[] { windows, linux });

        Assert.Equal(2, summary.Length);
        Assert.Contains(summary, row => row.Os == "Windows" && row.MedianMs == 10);
        Assert.Contains(summary, row => row.Os == "Linux" && row.MedianMs == 30);
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

        Assert.Contains("| Scenario | Variables | Operation | Host | OS |", markdown);
        Assert.Contains("Rows=100", markdown);
    }

    [Fact]
    public void MarkdownRenderer_KeepsTinyBenchmarkValuesVisible()
    {
        var markdown = new BenchmarkMarkdownRenderer().RenderSummaryTable(new[]
        {
            new BenchmarkSummaryRow
            {
                Scenario = "tiny",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                SampleCount = 1,
                Status = "Succeeded",
                MedianMs = 0.00042,
                MeanMs = 0.00043
            }
        });

        Assert.Contains("0.00042", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| 0 | 0 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateBenchmarkDocumentCommand_RejectsUnknownRenderers()
    {
        var root = CreateTempRoot();
        var readme = Path.Combine(root, "README.md");
        var summaryPath = Path.Combine(root, "summary.json");
        File.WriteAllText(readme, "<!-- BENCHMARK:results:START -->\nold\n<!-- BENCHMARK:results:END -->\n");
        BenchmarkJson.Write(summaryPath, new[] { new BenchmarkSummaryRow { Scenario = "case", Operation = "Run", Engine = "Managed" } });

        var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new System.Management.Automation.Runspaces.SessionStateCmdletEntry("Update-BenchmarkDocument", typeof(PSPublishModule.UpdateBenchmarkDocumentCommand), helpFileName: null));
        using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        using var ps = System.Management.Automation.PowerShell.Create(runspace);
        ps.AddCommand("Update-BenchmarkDocument")
            .AddParameter("Path", readme)
            .AddParameter("BlockId", "results")
            .AddParameter("SummaryPath", summaryPath)
            .AddParameter("Renderer", "ComparsionTable");

        var ex = Assert.Throws<System.Management.Automation.CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("ComparsionTable", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("old", File.ReadAllText(readme), StringComparison.OrdinalIgnoreCase);
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
    public void GateService_RejectsUnknownGroupByFields()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", MedianMs = 100 }
        });

        var ex = Assert.Throws<NotSupportedException>(() => new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            BaselineMode = BenchmarkBaselineMode.Update,
            GroupBy = new[] { "Suite", "Engne" }
        }));

        Assert.Contains("Engne", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GateService_RejectsDuplicateGroupKeys()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Find", Engine = "Managed", Host = "Current", MedianMs = 100 },
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Find", Engine = "Other", Host = "Current", MedianMs = 120 }
        });

        var ex = Assert.Throws<InvalidOperationException>(() => new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            BaselineMode = BenchmarkBaselineMode.Update,
            GroupBy = new[] { "Suite", "Scenario" }
        }));

        Assert.Contains("duplicate metric key", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GroupBy", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(baselinePath));
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
                ["suite|case|Run|Managed|Current|||MedianMs"] = 100
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
        Assert.Contains(verify.Messages, m => m.Contains("failed samples", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GateService_DefaultKeysIncludeOperatingSystem()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", Os = "Windows", MedianMs = 100 },
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", Os = "Linux", MedianMs = 200 }
        });

        var service = new BenchmarkGateService();
        var update = service.Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            BaselineMode = BenchmarkBaselineMode.Update
        });

        Assert.True(update.Passed);
        Assert.Equal(2, update.Metrics.Length);
        Assert.Contains(update.Metrics, metric => metric.Key.Contains("|Windows|", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(update.Metrics, metric => metric.Key.Contains("|Linux|", StringComparison.OrdinalIgnoreCase));
    }

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
                ["suite|case|Run|Managed|Current|||RowsPerSecond"] = 100
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
    public void Importer_ReadsSamplesJsonArrayAsSamples()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "samples.json");
        BenchmarkJson.Write(path, new[] { Sample("suite", "case", "Run", "Managed", 2) });

        var result = new BenchmarkResultImporter().Import(path);

        Assert.Single(result.Samples);
        Assert.Equal(2, Assert.Single(result.Summary).MedianMs);
    }

    [Fact]
    public void Importer_DirectoryUsesLatestRunReport()
    {
        var root = CreateTempRoot();
        var older = Path.Combine(root, "20260101-000000-old");
        var newer = Path.Combine(root, "20260102-000000-new");
        Directory.CreateDirectory(older);
        Directory.CreateDirectory(newer);
        var olderReport = Path.Combine(older, "run-report.json");
        var newerReport = Path.Combine(newer, "run-report.json");
        BenchmarkJson.Write(olderReport, new BenchmarkRunResult { Suite = "old", Samples = new[] { Sample("old", "case", "Run", "Managed", 1) } });
        BenchmarkJson.Write(newerReport, new BenchmarkRunResult { Suite = "new", Samples = new[] { Sample("new", "case", "Run", "Managed", 2) } });
        File.SetLastWriteTimeUtc(olderReport, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(newerReport, DateTime.UtcNow);

        var result = new BenchmarkResultImporter().Import(root);

        Assert.Equal("new", result.Suite);
        Assert.Single(result.Samples);
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
    public void Importer_UsesStableBenchmarkDotNetMethodName()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "DisplayInfo": "Write [Rows=10]",
      "Method": "Write",
      "Parameters": "Rows=10",
      "Statistics": {
        "Median": 500
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);
        var sample = Assert.Single(result.Samples);

        Assert.Equal("Write", sample.Scenario);
        Assert.Equal("10", sample.Variables["Rows"]);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetJobIdentity()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "Method": "Write",
      "Job": "Net80",
      "Parameters": "Rows=10",
      "Statistics": {
        "Median": 500
      }
    },
    {
      "Method": "Write",
      "Job": "Net10",
      "Parameters": "Rows=10",
      "Statistics": {
        "Median": 700
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);

        Assert.Contains(result.Summary, row => row.Engine == "Net80" && row.Variables["Rows"] == "10");
        Assert.Contains(result.Summary, row => row.Engine == "Net10" && row.Variables["Rows"] == "10");
    }

    [Fact]
    public void Importer_DirectoryDiscoversBenchmarkDotNetJsonReports()
    {
        var root = CreateTempRoot();
        var artifactRoot = Path.Combine(root, "BenchmarkDotNet.Artifacts");
        Directory.CreateDirectory(artifactRoot);
        File.WriteAllText(Path.Combine(artifactRoot, "Demo-report-full-compressed.json"), """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "Method": "Write",
      "Statistics": {
        "Median": 500
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(root);

        Assert.Single(result.Samples);
        Assert.Equal("Write", Assert.Single(result.Summary).Scenario);
    }

    [Fact]
    public void Importer_DirectoryUsesDirectorySuiteForBenchmarkDotNetJsonReports()
    {
        var root = CreateTempRoot();
        var artifactRoot = Path.Combine(root, "DirectorySuite");
        Directory.CreateDirectory(artifactRoot);
        File.WriteAllText(Path.Combine(artifactRoot, "Demo-report-full.json"), """
{
  "Title": "json-title",
  "Benchmarks": [
    {
      "Method": "Write",
      "Statistics": {
        "Median": 500
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(artifactRoot);
        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);

        Assert.Equal("DirectorySuite", result.Suite);
        Assert.Equal("DirectorySuite", sample.Suite);
        Assert.Equal("DirectorySuite", row.Suite);
    }

    [Fact]
    public void Importer_DirectoryPrefersFullBenchmarkDotNetJsonOverCsv()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "Demo-report.csv"), "Method,Median\nWrite,1.000 ms\n");
        File.WriteAllText(Path.Combine(root, "Demo-report-full.json"), """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "FullName": "Demo.Bench.Write()",
      "Method": "Write",
      "Statistics": {
        "Median": 200
      },
      "Memory": {
        "BytesAllocatedPerOperation": 4096
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(root);
        var row = Assert.Single(result.Summary);

        Assert.InRange(row.MedianMs.GetValueOrDefault(), 0.000199, 0.000201);
        Assert.Equal("Demo.Bench", row.Variables["Type"]);
        Assert.Equal(4096, row.Metrics["Allocated"]);
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
    public void Importer_PreservesBenchmarkDotNetHostAndSplitsParameters()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "HostEnvironmentInfo": {
    "BenchmarkDotNetCaption": "BenchmarkDotNet v0.15",
    "RuntimeVersion": ".NET 10.0",
    "Architecture": "X64",
    "OperatingSystem": "Windows"
  },
  "Benchmarks": [
    {
      "DisplayInfo": "Write",
      "Parameters": "Rows=10, Profile=Fast",
      "Statistics": {
        "Median": 500
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);
        var sample = Assert.Single(result.Samples);

        Assert.Contains(".NET 10.0", sample.Host, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("10", sample.Variables["Rows"]);
        Assert.Equal("Fast", sample.Variables["Profile"]);
        Assert.DoesNotContain("Parameters", sample.Variables.Keys);
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
    public void Importer_PrefersBenchmarkDotNetMedianUnits()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Median [us],Mean [us]\nWrite,1500,9000\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        Assert.Equal(1.5, Assert.Single(result.Summary).MedianMs);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetStatisticsAsMetrics()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Rows,Median [us],Mean [us],Min [us],Max [us],Error,StdDev,Allocated\nWrite,10,1500,9000,1000,12000,1.2,3.4,8 KB\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");
        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);

        Assert.Equal("10", sample.Variables["Rows"]);
        Assert.DoesNotContain("Median [us]", sample.Variables.Keys);
        Assert.DoesNotContain("Mean [us]", sample.Variables.Keys);
        Assert.DoesNotContain("Min [us]", sample.Variables.Keys);
        Assert.DoesNotContain("Max [us]", sample.Variables.Keys);
        Assert.DoesNotContain("Error", sample.Variables.Keys);
        Assert.DoesNotContain("StdDev", sample.Variables.Keys);
        Assert.DoesNotContain("Allocated", sample.Variables.Keys);
        Assert.Equal(1.5, sample.Metrics["MedianMs"]);
        Assert.Equal(9, sample.Metrics["MeanMs"]);
        Assert.Equal(1, sample.Metrics["MinMs"]);
        Assert.Equal(12, sample.Metrics["MaxMs"]);
        Assert.Equal(1.2, sample.Metrics["Error"]);
        Assert.Equal(3.4, sample.Metrics["StdDev"]);
        Assert.Equal(8192, sample.Metrics["Allocated"]);
        Assert.Equal(1.5, row.MedianMs);
        Assert.Equal(9, row.MeanMs);
        Assert.Equal(1, row.MinMs);
        Assert.Equal(12, row.MaxMs);
        Assert.Equal(8192, row.Metrics["Allocated"]);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetJsonTypeIdentity()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "FullName": "Demo.FastBench.Write()",
      "Method": "Write",
      "Statistics": {
        "Median": 500
      }
    },
    {
      "FullName": "Demo.SlowBench.Write()",
      "Method": "Write",
      "Statistics": {
        "Median": 700
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);

        Assert.Equal(2, result.Summary.Length);
        Assert.Contains(result.Summary, row => row.Scenario == "Write" && row.Variables["Type"] == "Demo.FastBench");
        Assert.Contains(result.Summary, row => row.Scenario == "Write" && row.Variables["Type"] == "Demo.SlowBench");
    }

    [Fact]
    public void Importer_DirectoryDeduplicatesBenchmarkDotNetJsonVariants()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "Demo-report.json"), """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "Method": "Write",
      "Statistics": {
        "Median": 100
      }
    }
  ]
}
""");
        File.WriteAllText(Path.Combine(root, "Demo-report-full.json"), """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "FullName": "Demo.Bench.Write()",
      "Method": "Write",
      "Statistics": {
        "Median": 200
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(root);
        var row = Assert.Single(result.Summary);

        Assert.InRange(row.MedianMs.GetValueOrDefault(), 0.000199, 0.000201);
        Assert.Equal("Demo.Bench", row.Variables["Type"]);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetJsonMemoryMetrics()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "FullName": "Demo.Bench.Write()",
      "Method": "Write",
      "Statistics": {
        "Median": 200
      },
      "Memory": {
        "Gen0Collections": 3,
        "Gen1Collections": 2,
        "Gen2Collections": 1,
        "BytesAllocatedPerOperation": 4096
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);
        var row = Assert.Single(result.Summary);

        Assert.Equal(4096, row.Metrics["Allocated"]);
        Assert.Equal(3, row.Metrics["Gen0"]);
        Assert.Equal(2, row.Metrics["Gen1"]);
        Assert.Equal(1, row.Metrics["Gen2"]);
        Assert.Equal(4096, row.Metrics["BytesAllocatedPerOperation"]);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetJsonPrimaryStatistics()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "FullName": "Demo.Bench.Write()",
      "Method": "Write",
      "Statistics": {
        "Median": 200,
        "Mean": 300,
        "Min": 100,
        "Max": 400,
        "Error": 50
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);
        var row = Assert.Single(result.Summary);

        Assert.InRange(row.MedianMs.GetValueOrDefault(), 0.000199, 0.000201);
        Assert.InRange(row.MeanMs.GetValueOrDefault(), 0.000299, 0.000301);
        Assert.InRange(row.MinMs.GetValueOrDefault(), 0.000099, 0.000101);
        Assert.InRange(row.MaxMs.GetValueOrDefault(), 0.000399, 0.000401);
        Assert.InRange(row.Metrics["Error"], 0.000049, 0.000051);
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
    public void Importer_PreservesCsvCustomMetrics()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,Rows,Iteration,Status,DurationMs,Reason,RowsPerSecond\nsuite,Write,Run,Managed,Current,10,0,Succeeded,12.5,,42\n");

        var result = new BenchmarkResultImporter().Import(csv);
        var sample = Assert.Single(result.Samples);

        Assert.Equal(42, sample.Metrics["RowsPerSecond"]);
        Assert.Equal(42, Assert.Single(result.Summary).Metrics["RowsPerSecond"]);
        Assert.DoesNotContain("RowsPerSecond", sample.Variables.Keys);
    }

    [Fact]
    public void Importer_ParsesGenericCsvMetricsWithoutDurationUnits()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,Iteration,Status,DurationMs,Reason,Rows\nsuite,Write,Run,Managed,Current,0,Succeeded,12.5,,42\n");

        var result = new BenchmarkResultImporter().Import(csv);
        var sample = Assert.Single(result.Samples);

        Assert.Equal(42, sample.Metrics["Rows"]);
        Assert.Equal(42, Assert.Single(result.Summary).Metrics["Rows"]);
    }

    [Fact]
    public void Importer_PreservesRunnerScenarioWhenMethodIsVariable()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,Method,Iteration,Status,DurationMs,Reason\nsuite,CaseA,Run,Managed,Current,Fast,0,Succeeded,12.5,\n");

        var result = new BenchmarkResultImporter().Import(csv);
        var sample = Assert.Single(result.Samples);

        Assert.Equal("CaseA", sample.Scenario);
        Assert.Equal("Fast", sample.Variables["Method"]);
        Assert.Equal("Fast", Assert.Single(result.Summary).Variables["Method"]);
    }

    [Fact]
    public void Importer_PreservesRunnerBenchmarkAndJobVariables()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,Benchmark,Job,Iteration,Status,DurationMs,Reason\nsuite,CaseA,Run,Managed,Current,FastBench,Net10,0,Succeeded,12.5,\n");

        var result = new BenchmarkResultImporter().Import(csv);
        var sample = Assert.Single(result.Samples);

        Assert.Equal("CaseA", sample.Scenario);
        Assert.Equal("FastBench", sample.Variables["Benchmark"]);
        Assert.Equal("Net10", sample.Variables["Job"]);
        Assert.Equal("FastBench", Assert.Single(result.Summary).Variables["Benchmark"]);
    }

    [Fact]
    public void Importer_KeepsRunnerVariablesNamedLikeBenchmarkDotNetStatistics()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,Ratio,Allocated,Gen0,Iteration,Status,DurationMs,Reason\nsuite,CaseA,Run,Managed,Current,baseline,small,lane0,0,Succeeded,12.5,\n");

        var result = new BenchmarkResultImporter().Import(csv);
        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);

        Assert.Equal("baseline", sample.Variables["Ratio"]);
        Assert.Equal("small", sample.Variables["Allocated"]);
        Assert.Equal("lane0", sample.Variables["Gen0"]);
        Assert.Equal("baseline", row.Variables["Ratio"]);
        Assert.DoesNotContain("Ratio", sample.Metrics.Keys);
        Assert.DoesNotContain("Allocated", sample.Metrics.Keys);
    }

    [Fact]
    public void Importer_HandlesQuotedMultilineCsvRecords()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,Rows,Iteration,Status,DurationMs,Reason\nsuite,Write,Run,Managed,Current,10,0,Failed,12.5,\"line one\nline two\"\n");

        var result = new BenchmarkResultImporter().Import(csv);
        var sample = Assert.Single(result.Samples);

        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Equal("line one\nline two", sample.Reason);
        Assert.Single(result.Summary);
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
    public void Importer_DirectoryUsesLatestRunnerSamplesCsv()
    {
        var root = CreateTempRoot();
        var older = Path.Combine(root, "20260101-000000-old");
        var newer = Path.Combine(root, "20260102-000000-new");
        Directory.CreateDirectory(older);
        Directory.CreateDirectory(newer);
        var olderSamples = Path.Combine(older, "samples.csv");
        var newerSamples = Path.Combine(newer, "samples.csv");
        File.WriteAllText(olderSamples, "Suite,Scenario,Operation,Engine,Host,Rows,Iteration,Status,DurationMs,Reason\nsuite,Old,Run,Managed,Current,1,0,Succeeded,1,\n");
        File.WriteAllText(newerSamples, "Suite,Scenario,Operation,Engine,Host,Rows,Iteration,Status,DurationMs,Reason\nsuite,New,Run,Managed,Current,2,0,Succeeded,2,\n");
        File.SetLastWriteTimeUtc(olderSamples, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(newerSamples, DateTime.UtcNow);

        var result = new BenchmarkResultImporter().Import(root);
        var sample = Assert.Single(result.Samples);

        Assert.Equal("suite", result.Suite);
        Assert.Equal("New", sample.Scenario);
        Assert.Equal("2", sample.Variables["Rows"]);
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
    public void DslRuntime_StopsCaseSourceErrors()
    {
        var script = ScriptBlock.Create(@"
benchmark 'bad' {
    cases { Add-BenchmarkCaseSource { Write-Error 'case boom' } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => PowerShellBenchmarkDslRuntime.Evaluate(script));

        Assert.Contains("case boom", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_StopsCaseSourceNativeCommandFailures()
    {
        var script = ScriptBlock.Create(@"
benchmark 'bad' {
    cases { Add-BenchmarkCaseSource { dotnet --not-a-real-option } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => PowerShellBenchmarkDslRuntime.Evaluate(script));

        Assert.Contains("stopped", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_StopsDesktopNativeExitCodes()
    {
        var script = ScriptBlock.Create(@"
benchmark 'bad' {
    cases { Add-BenchmarkCaseSource { $global:LASTEXITCODE = 23 } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => PowerShellBenchmarkDslRuntime.Evaluate(script));

        Assert.Contains("23", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_StopsRootSpecErrors()
    {
        var script = ScriptBlock.Create(@"
Write-Error 'root boom'
benchmark 'bad' {
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => PowerShellBenchmarkDslRuntime.Evaluate(script));

        Assert.Contains("root boom", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_PreservesScriptRootInNestedBlocks()
    {
        var root = CreateTempRoot();
        var script = ScriptBlock.Create(@"
benchmark 'rooted' {
    cases { case A @{ Root = $PSScriptRoot } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var suite = Assert.Single(PowerShellBenchmarkDslRuntime.Evaluate(script, root));
        var benchmarkCase = Assert.Single(suite.Cases);

        Assert.Equal(root, benchmarkCase.Values["Root"]);
    }

    [Fact]
    public void DslRuntime_PreservesSpecLocalVariablesInCapturedBlocks()
    {
        var root = CreateTempRoot();
        var fixture = Path.Combine(root, "fixture.txt");
        File.WriteAllText(fixture, "ok");
        var script = ScriptBlock.Create(@"
$fixture = Join-Path $PSScriptRoot 'fixture.txt'
benchmark 'closure' {
    axis Operation Run
    axis Engine Managed
    setup { param($case, $run) $run.FixtureText = Get-Content -LiteralPath $fixture -Raw }
    engine Managed { operation Run { param($case, $run) if ($run.FixtureText -ne 'ok') { throw 'closure missing' } } }
}
");

        var suite = Assert.Single(PowerShellBenchmarkDslRuntime.Evaluate(script, root));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
    }

    [Fact]
    public void DslRuntime_PreservesSpecLocalHelperFunctionsInCapturedBlocks()
    {
        var root = CreateTempRoot();
        var fixture = Path.Combine(root, "fixture.txt");
        File.WriteAllText(fixture, "ok");
        var script = ScriptBlock.Create(@"
function Read-FixtureText {
    param([string] $Path)
    Get-Content -LiteralPath $Path -Raw
}
$fixture = Join-Path $PSScriptRoot 'fixture.txt'
benchmark 'helpers' {
    axis Operation Run
    axis Engine Managed
    setup { param($case, $run) $run.FixtureText = Read-FixtureText -Path $fixture }
    engine Managed { operation Run { param($case, $run) if ($run.FixtureText -ne 'ok') { throw 'helper missing' } } }
}
");

        var suite = Assert.Single(PowerShellBenchmarkDslRuntime.Evaluate(script, root));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
    }

    [Fact]
    public void DslRuntime_RestoresCapturedHelperFunctionsAfterUse()
    {
        var previousRunspace = System.Management.Automation.Runspaces.Runspace.DefaultRunspace;
        using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace();
        runspace.Open();
        System.Management.Automation.Runspaces.Runspace.DefaultRunspace = runspace;
        try
        {
            using (var ps = System.Management.Automation.PowerShell.Create(runspace))
            {
                ps.AddScript("function Get-PowerForgeBenchmarkProbe { 'original' }");
                ps.Invoke();
            }

            var script = ScriptBlock.Create(@"
function Get-PowerForgeBenchmarkProbe { 'captured' }
benchmark 'helpers' {
    axis Operation Run
    axis Engine Managed
    setup { param($case, $run) $run.Probe = Get-PowerForgeBenchmarkProbe }
    engine Managed { operation Run { param($case, $run) if ($run.Probe -ne 'captured') { throw 'helper missing' } } }
}
");

            var suite = Assert.Single(PowerShellBenchmarkDslRuntime.Evaluate(script));
            suite.WarmupCount = 0;
            suite.IterationCount = 1;
            var result = new PowerShellBenchmarkRunner().Run(suite);

            using var verify = System.Management.Automation.PowerShell.Create(runspace);
            verify.AddScript("Get-PowerForgeBenchmarkProbe");
            var restored = Assert.Single(verify.Invoke()).BaseObject;
            Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
            Assert.Equal("original", restored);
        }
        finally
        {
            System.Management.Automation.Runspaces.Runspace.DefaultRunspace = previousRunspace;
        }
    }

    [Fact]
    public void DslRuntime_RejectsDuplicateEngineNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'dup' {
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
    engine managed { operation Other { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => PowerShellBenchmarkDslRuntime.Evaluate(script));

        Assert.Contains("already defines engine", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_RejectsDuplicateAxisNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'dup-axis' {
    axis Rows 1
    axis rows 2
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => PowerShellBenchmarkDslRuntime.Evaluate(script));

        Assert.Contains("already defines axis", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_RejectsDuplicateOperationNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'dup-operation' {
    axis Operation Run
    axis Engine Managed
    engine Managed {
        operation Run { param($case, $run) }
        operation run { param($case, $run) }
    }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => PowerShellBenchmarkDslRuntime.Evaluate(script));

        Assert.Contains("already defines operation", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_RejectsDuplicateMetricNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'dup-metric' {
    axis Operation Run
    axis Engine Managed
    metric RowsPerSecond { 1 }
    metric rowspersecond { 2 }
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => PowerShellBenchmarkDslRuntime.Evaluate(script));

        Assert.Contains("already defines metric", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_PreservesNearestCapturedScopeAndUserPathVariables()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "outer.txt"), "outer");
        File.WriteAllText(Path.Combine(root, "inner.txt"), "inner");
        var script = ScriptBlock.Create(@"
$Path = Join-Path $PSScriptRoot 'outer.txt'
$fixture = 'outer'
benchmark 'closure' {
    $Path = Join-Path $PSScriptRoot 'inner.txt'
    $fixture = 'inner'
    axis Operation Run
    axis Engine Managed
    setup {
        param($case, $run)
        $run.FixtureText = Get-Content -LiteralPath $Path -Raw
        $run.FixtureName = $fixture
    }
    engine Managed {
        operation Run {
            param($case, $run)
            if ($run.FixtureText -ne 'inner' -or $run.FixtureName -ne 'inner') {
                throw 'wrong captured scope'
            }
        }
    }
}
");

        var suite = Assert.Single(PowerShellBenchmarkDslRuntime.Evaluate(script, root));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
    }

    [Fact]
    public void DslRuntime_HandlesOrderedDictionaryCaseSources()
    {
        var script = ScriptBlock.Create(@"
benchmark 'ordered' {
    cases { Add-BenchmarkCaseSource { [ordered]@{ Name = 'A'; Rows = 10 } } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var suite = Assert.Single(PowerShellBenchmarkDslRuntime.Evaluate(script));
        var benchmarkCase = Assert.Single(suite.Cases);

        Assert.Equal("A", benchmarkCase.Name);
        Assert.Equal(10, benchmarkCase.Values["Rows"]);
    }

    [Fact]
    public void DslRuntime_HonorsArtifactsNone()
    {
        var script = ScriptBlock.Create(@"
benchmark 'none' {
    artifacts None
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var suite = Assert.Single(PowerShellBenchmarkDslRuntime.Evaluate(script));

        Assert.Equal(BenchmarkArtifactKind.None, suite.Artifacts);
    }

    [Fact]
    public void DslRuntime_RejectsUnknownArtifactNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'bad' {
    artifacts Json, MarkDownn
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => PowerShellBenchmarkDslRuntime.Evaluate(script));

        Assert.Contains("MarkDownn", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_ExposesSuiteOverrideParameter()
    {
        var property = typeof(PSPublishModule.InvokeBenchmarkSuiteCommand).GetProperty("Suite");

        Assert.NotNull(property);
        Assert.Contains(property!.GetCustomAttributes(inherit: true), attribute => attribute is ParameterAttribute);
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_ResolvesRelativeOutputRootFromPowerShellLocation()
    {
        var processRoot = CreateTempRoot();
        var shellRoot = CreateTempRoot();
        var previousCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = processRoot;
            var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
            initialSessionState.Commands.Add(new System.Management.Automation.Runspaces.SessionStateCmdletEntry("Invoke-BenchmarkSuite", typeof(PSPublishModule.InvokeBenchmarkSuiteCommand), helpFileName: null));
            using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
            runspace.Open();
            using var ps = System.Management.Automation.PowerShell.Create(runspace);
            ps.AddCommand("Set-Location").AddParameter("Path", shellRoot);
            ps.Invoke();
            ps.Commands.Clear();

            var settings = ScriptBlock.Create(@"
benchmark 'path' -out 'relative-out' {
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");
            ps.AddCommand("Invoke-BenchmarkSuite")
                .AddParameter("Settings", settings)
                .AddParameter("WarmupCount", 0)
                .AddParameter("IterationCount", 1);

            var result = Assert.IsType<BenchmarkRunResult>(Assert.Single(ps.Invoke()).BaseObject);

            Assert.StartsWith(Path.Combine(shellRoot, "relative-out"), result.Artifacts["run-report.json"], StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(processRoot, "relative-out")));
        }
        finally
        {
            Environment.CurrentDirectory = previousCurrentDirectory;
        }
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
    public void Runner_LabelsCurrentHostWithRuntime()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Host", Values = { "Current" } });

        var item = Assert.Single(new PowerShellBenchmarkRunner().Plan(suite));

        Assert.NotEqual("Current", item.Host, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("-", item.Host, StringComparison.Ordinal);
        Assert.Equal(item.Host, item.Values["Host"]);
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
        Assert.DoesNotContain("Scenario", Assert.Single(result.Samples).Variables.Keys);
    }

    [Fact]
    public void Runner_LabelsOperatingSystemsDistinctly()
    {
        var suite = CreateRunnableSuite();

        var result = new PowerShellBenchmarkRunner().Run(suite);
        var sample = Assert.Single(result.Samples);

        Assert.Contains(sample.Os, new[] { "Windows", "Linux", "macOS" });
    }

    [Fact]
    public void Runner_ExcludesGeneratedCaseNameFromVariables()
    {
        var suite = CreateRunnableSuite();
        suite.Cases.Clear();
        suite.Cases.Add(new PowerShellBenchmarkCase
        {
            Name = "Generated",
            Values = { ["Name"] = "Generated", ["Rows"] = 10 }
        });

        var result = new PowerShellBenchmarkRunner().Run(suite);
        var sample = Assert.Single(result.Samples);

        Assert.Equal("Generated", sample.Scenario);
        Assert.Equal("10", sample.Variables["Rows"]);
        Assert.DoesNotContain("Name", sample.Variables.Keys);
    }

    [Fact]
    public void Runner_UsesFreshCaseObjectForEachMeasuredIteration()
    {
        var suite = CreateRunnableSuite();
        suite.IterationCount = 2;
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) if ($case.Seen) { throw 'case reused' } $case.Seen = $true");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(2, result.Samples.Length);
        Assert.All(result.Samples, sample => Assert.Equal(BenchmarkSampleStatus.Succeeded, sample.Status));
    }

    [Fact]
    public void Runner_RotatesWorkItemsBetweenMeasuredIterations()
    {
        var suite = CreateRunnableSuite();
        suite.IterationCount = 2;
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { 1, 2, 3 } });

        var result = new PowerShellBenchmarkRunner().Run(suite);
        var firstIteration = result.Samples.Where(sample => sample.Iteration == 0).Select(sample => sample.Variables["Rows"]).ToArray();
        var secondIteration = result.Samples.Where(sample => sample.Iteration == 1).Select(sample => sample.Variables["Rows"]).ToArray();

        Assert.Equal(new[] { "1", "2", "3" }, firstIteration);
        Assert.Equal(new[] { "2", "3", "1" }, secondIteration);
    }

    [Fact]
    public void Runner_PairsImplicitOperationsWithTheirEngines()
    {
        var first = new PowerShellBenchmarkEngine { Name = "First" };
        first.Operations["FirstRun"] = ScriptBlock.Create("param($case, $run)");
        var second = new PowerShellBenchmarkEngine { Name = "Second" };
        second.Operations["SecondRun"] = ScriptBlock.Create("param($case, $run)");
        var suite = new PowerShellBenchmarkSuite
        {
            Name = "suite",
            OutputRoot = CreateTempRoot(),
            WarmupCount = 0,
            IterationCount = 1,
            Artifacts = BenchmarkArtifactKind.None
        };
        suite.Engines.Add(first);
        suite.Engines.Add(second);

        var plan = new PowerShellBenchmarkRunner().Plan(suite);

        Assert.Equal(2, plan.Length);
        Assert.Contains(plan, item => item.Engine == "First" && item.Operation == "FirstRun");
        Assert.Contains(plan, item => item.Engine == "Second" && item.Operation == "SecondRun");
        Assert.DoesNotContain(plan, item => item.Engine == "First" && item.Operation == "SecondRun");
        Assert.DoesNotContain(plan, item => item.Engine == "Second" && item.Operation == "FirstRun");
    }

    [Fact]
    public void Runner_AppliesSkipBeforeRequiringOperationHandlers()
    {
        var first = new PowerShellBenchmarkEngine { Name = "First" };
        first.Operations["Read"] = ScriptBlock.Create("param($case, $run)");
        var second = new PowerShellBenchmarkEngine { Name = "Second" };
        second.Operations["Write"] = ScriptBlock.Create("param($case, $run)");
        var suite = new PowerShellBenchmarkSuite
        {
            Name = "suite",
            OutputRoot = CreateTempRoot(),
            WarmupCount = 0,
            IterationCount = 1,
            Artifacts = BenchmarkArtifactKind.None,
            Skip = ScriptBlock.Create("param($case) ($case.Engine -eq 'First' -and $case.Operation -eq 'Write') -or ($case.Engine -eq 'Second' -and $case.Operation -eq 'Read')")
        };
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Engine", Values = { "First", "Second" } });
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Operation", Values = { "Read", "Write" } });
        suite.Engines.Add(first);
        suite.Engines.Add(second);

        var runner = new PowerShellBenchmarkRunner();
        var plan = runner.Plan(suite);
        var result = runner.Run(suite);

        Assert.Equal(4, plan.Length);
        Assert.Equal(2, plan.Count(item => item.IsSkipped));
        Assert.Equal(2, result.Samples.Count(sample => sample.Status == BenchmarkSampleStatus.Succeeded));
        Assert.Equal(2, result.Samples.Count(sample => sample.Status == BenchmarkSampleStatus.Skipped));
    }

    [Fact]
    public void Runner_SetsDurationMsBeforeCapturingMetrics()
    {
        var suite = CreateRunnableSuite();
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) Start-Sleep -Milliseconds 10");
        suite.Metrics.Add(new PowerShellBenchmarkMetric
        {
            Name = "RowsPerSecond",
            ScriptBlock = ScriptBlock.Create("param($case, $run) if ($run.DurationMs -le 0) { throw 'missing duration' }; 1000 / [double]$run.DurationMs")
        });

        var result = new PowerShellBenchmarkRunner().Run(suite);
        var sample = Assert.Single(result.Samples);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, sample.Status);
        Assert.True(sample.DurationMs > 0);
        Assert.True(sample.Metrics["RowsPerSecond"] > 0);
    }

    [Fact]
    public void Runner_SkipsValidationAndMetricsDuringWarmup()
    {
        var suite = CreateRunnableSuite();
        suite.WarmupCount = 1;
        suite.Validate = ScriptBlock.Create("param($case, $run) if ($run.Iteration -lt 0) { throw 'warmup validation should not run' }");
        suite.Metrics.Add(new PowerShellBenchmarkMetric
        {
            Name = "MeasuredOnly",
            ScriptBlock = ScriptBlock.Create("param($case, $run) if ($run.Iteration -lt 0) { throw 'warmup metric should not run' }; 1")
        });

        var result = new PowerShellBenchmarkRunner().Run(suite);
        var sample = Assert.Single(result.Samples);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, sample.Status);
        Assert.Equal(0, sample.Iteration);
        Assert.Equal(1, sample.Metrics["MeasuredOnly"]);
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
    public void Runner_EscapesMatrixOutputPathDelimiters()
    {
        var suite = CreateRunnableSuite();
        suite.Cases.Clear();
        suite.Cases.Add(new PowerShellBenchmarkCase { Name = "Same", Values = { ["A"] = "B_C=D" } });
        suite.Cases.Add(new PowerShellBenchmarkCase { Name = "Same", Values = { ["A"] = "B", ["C"] = "D" } });
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) [IO.File]::WriteAllText($run.OutputPath, 'ok')");

        var result = new PowerShellBenchmarkRunner().Run(suite);
        var outputFiles = Directory.GetFiles(Path.Combine(suite.OutputRoot, result.RunId), "output", SearchOption.AllDirectories);

        Assert.Equal(2, outputFiles.Length);
        Assert.Contains(outputFiles, path => path.Contains("A=B%5FC%3DD", StringComparison.Ordinal));
        Assert.Contains(outputFiles, path => path.Contains("A=B_C=D", StringComparison.Ordinal));
    }

    [Fact]
    public void Runner_WritesMatrixVariablesToCsvArtifacts()
    {
        var suite = CreateRunnableSuite();
        suite.Artifacts = BenchmarkArtifactKind.Csv;
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { 10 } });
        suite.Metrics.Add(new PowerShellBenchmarkMetric { Name = "TinyMetric", ScriptBlock = ScriptBlock.Create("0.00042") });
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) Start-Sleep -Milliseconds 5");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var samplesCsv = File.ReadAllText(result.Artifacts["samples.csv"]);
        var summaryCsv = File.ReadAllText(result.Artifacts["summary.csv"]);
        Assert.Contains("Suite,Scenario,Operation,Engine,Host,OS,Rows,Iteration,Status,DurationMs,Reason,TinyMetric", samplesCsv);
        Assert.Contains($",Managed,{result.Samples[0].Host},{result.Samples[0].Os},10,0,Succeeded", samplesCsv);
        Assert.Contains(result.Samples[0].DurationMs.ToString("G17", System.Globalization.CultureInfo.InvariantCulture), samplesCsv);
        Assert.Contains(",0.00042", samplesCsv);
        Assert.Contains("Suite,Scenario,Operation,Engine,Host,OS,Rows,SampleCount,FailureCount,Status,MedianMs,MeanMs,MinMs,MaxMs,TinyMetric", summaryCsv);
        Assert.Contains(",0.00042", summaryCsv);
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
    public void Runner_TreatsNativeCommandExitsAsFailures()
    {
        var suite = CreateRunnableSuite();
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) dotnet --not-a-real-option");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Contains("stopped", sample.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_TreatsDesktopNativeExitCodesAsFailures()
    {
        var suite = CreateRunnableSuite();
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) $global:LASTEXITCODE = 23");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Contains("23", sample.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_AllowsSetupToAddRunContextProperties()
    {
        var suite = CreateRunnableSuite();
        suite.Setup = ScriptBlock.Create("param($case, $run) $run.Prepared = 'ok'");
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) if ($run.Prepared -ne 'ok') { throw 'missing setup property' }");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
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
        suite.Artifacts = BenchmarkArtifactKind.Json | BenchmarkArtifactKind.Csv | BenchmarkArtifactKind.Markdown;

        var result = new PowerShellBenchmarkRunner().Run(suite);
        var report = BenchmarkJson.Read<BenchmarkRunResult>(result.Artifacts["run-report.json"]);

        Assert.Contains("run-report.json", report.Artifacts.Keys);
        Assert.Contains("samples.json", report.Artifacts.Keys);
        Assert.Contains("metadata.json", report.Artifacts.Keys);
        Assert.Contains("samples.csv", report.Artifacts.Keys);
        Assert.Contains("summary.csv", report.Artifacts.Keys);
        Assert.Contains("summary.md", report.Artifacts.Keys);
        Assert.Contains("comparison.md", report.Artifacts.Keys);
        Assert.True(File.Exists(result.Artifacts["metadata.json"]));
    }

    [Fact]
    public void Runner_RejectsUnsupportedComparisonDimensions()
    {
        var suite = CreateRunnableSuite();
        var output = Path.Combine(suite.OutputRoot, "comparison-side-effect.txt");
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create($"[IO.File]::WriteAllText('{output.Replace("'", "''")}', 'executed')");
        suite.Comparisons.Add(new PowerShellBenchmarkComparison { Dimension = "Operation", Baseline = "Run" });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Run(suite));

        Assert.Contains("Operation", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Runner_RejectsMissingComparisonBaselineBeforeMeasurement()
    {
        var suite = CreateRunnableSuite();
        var output = Path.Combine(suite.OutputRoot, "missing-baseline-side-effect.txt");
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create($"[IO.File]::WriteAllText('{output.Replace("'", "''")}', 'executed')");
        suite.Comparisons.Add(new PowerShellBenchmarkComparison { Dimension = "Engine", Baseline = "Manged" });

        var ex = Assert.Throws<InvalidOperationException>(() => new PowerShellBenchmarkRunner().Run(suite));

        Assert.Contains("Manged", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("baseline engine", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

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
