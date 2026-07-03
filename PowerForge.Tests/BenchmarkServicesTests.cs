using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    private static readonly Lazy<Runspace> BenchmarkDslRunspace = new(CreateBenchmarkDslRunspace);

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
    public void SummaryService_CalculatesPercentilesDeviationAndOutliers()
    {
        var samples = new[]
        {
            Sample("suite", "case", "Run", "Managed", 1),
            Sample("suite", "case", "Run", "Managed", 2),
            Sample("suite", "case", "Run", "Managed", 3),
            Sample("suite", "case", "Run", "Managed", 4),
            Sample("suite", "case", "Run", "Managed", 100)
        };

        var row = Assert.Single(new BenchmarkSummaryService().Summarize(samples, PowerShellBenchmarkOutlierMode.ExcludeMinMax));

        Assert.Equal(3, row.SampleCount);
        Assert.Equal(2, row.OutlierCount);
        Assert.Equal(3, row.MedianMs);
        Assert.Equal(3, row.MeanMs);
        Assert.Equal(3.9, row.P95Ms);
        Assert.Equal(3.98, row.P99Ms);
        Assert.Equal(1, row.StdDevMs);
        Assert.InRange(row.StdErrMs.GetValueOrDefault(), 0.57, 0.58);
    }

    [Fact]
    public void SummaryService_KeepsRunModesSeparate()
    {
        var quickManaged = Sample("suite", "case", "Run", "Managed", 10);
        quickManaged.RunMode = "quick";
        var publishManaged = Sample("suite", "case", "Run", "Managed", 30);
        publishManaged.RunMode = "publish";
        var quickOther = Sample("suite", "case", "Run", "Other", 20);
        quickOther.RunMode = "quick";
        var publishOther = Sample("suite", "case", "Run", "Other", 60);
        publishOther.RunMode = "publish";

        var service = new BenchmarkSummaryService();
        var summary = service.Summarize(new[] { quickManaged, publishManaged, quickOther, publishOther });
        var comparison = service.Compare(summary, "Managed");

        Assert.Equal(4, summary.Length);
        Assert.Contains(summary, row => row.Engine == "Managed" && row.RunMode == "quick" && row.MedianMs == 10);
        Assert.Contains(summary, row => row.Engine == "Managed" && row.RunMode == "publish" && row.MedianMs == 30);
        Assert.Contains(comparison, row => row.Engine == "Other" && row.RunMode == "quick" && row.Ratio == 2);
        Assert.Contains(comparison, row => row.Engine == "Other" && row.RunMode == "publish" && row.Ratio == 2);
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
    public void SummaryService_RejectsAmbiguousCaseOnlyComparisonBaselines()
    {
        var summary = new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", MedianMs = 10 },
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "managed", Host = "Current", MedianMs = 20 },
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Other", Host = "Current", MedianMs = 30 }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new BenchmarkSummaryService().Compare(summary, "MANAGED"));

        Assert.Contains("ambiguous", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Managed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("managed", ex.Message, StringComparison.Ordinal);
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
    public void SummaryService_KeepsCaseSensitiveVariableValuesSeparate()
    {
        var samples = new[]
        {
            Sample("suite", "case", "Run", "Managed", 10, new Dictionary<string, string?> { ["Input"] = "abc" }),
            Sample("suite", "case", "Run", "Managed", 20, new Dictionary<string, string?> { ["Input"] = "ABC" })
        };

        var summary = new BenchmarkSummaryService().Summarize(samples);

        Assert.Equal(2, summary.Length);
        Assert.Contains(summary, row => row.Variables["Input"] == "abc" && row.MedianMs == 10);
        Assert.Contains(summary, row => row.Variables["Input"] == "ABC" && row.MedianMs == 20);
    }

    [Fact]
    public void SummaryService_KeepsImportedVariablesNamedLikeAxesSeparate()
    {
        var samples = new[]
        {
            Sample("suite", "case", "Run", "Managed", 10, new Dictionary<string, string?> { ["Operation"] = "Read" }),
            Sample("suite", "case", "Run", "Managed", 20, new Dictionary<string, string?> { ["Operation"] = "Write" }),
            Sample("suite", "case", "Run", "Managed", 30, new Dictionary<string, string?> { ["Engine"] = "Local" }),
            Sample("suite", "case", "Run", "Managed", 40, new Dictionary<string, string?> { ["Host"] = "Remote" })
        };

        var summary = new BenchmarkSummaryService().Summarize(samples);

        Assert.Equal(4, summary.Length);
        Assert.Contains(summary, row => row.Variables.TryGetValue("Operation", out var value) && value == "Read" && row.MedianMs == 10);
        Assert.Contains(summary, row => row.Variables.TryGetValue("Operation", out var value) && value == "Write" && row.MedianMs == 20);
        Assert.Contains(summary, row => row.Variables.TryGetValue("Engine", out var value) && value == "Local" && row.MedianMs == 30);
        Assert.Contains(summary, row => row.Variables.TryGetValue("Host", out var value) && value == "Remote" && row.MedianMs == 40);
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
    public void SummaryService_CarriesStatusIntoComparisonRows()
    {
        var summary = new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", Status = "Succeeded", MedianMs = 10 },
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Other", Host = "Current", Status = "Skipped" }
        };

        var comparison = new BenchmarkSummaryService().Compare(summary, "Managed");

        var other = Assert.Single(comparison, row => row.Engine == "Other");
        Assert.Equal("Skipped", other.Status);
        Assert.Null(other.Actual);
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
    public void MarkdownRenderer_RendersComparisonAsPivotedReadmeTable()
    {
        var markdown = new BenchmarkMarkdownRenderer().RenderComparisonTable(new[]
        {
            new BenchmarkComparisonRow
            {
                Scenario = "SingleModule",
                Operation = "Install",
                Engine = "Managed",
                Host = "Core-7.6.3",
                BaselineEngine = "Managed",
                Metric = "MedianMs",
                Actual = 1000,
                Baseline = 1000,
                Ratio = 1,
                Variables = new Dictionary<string, string?> { ["ModuleName"] = "PSScriptAnalyzer" }
            },
            new BenchmarkComparisonRow
            {
                Scenario = "SingleModule",
                Operation = "Install",
                Engine = "ModuleFast",
                Host = "Core-7.6.3",
                BaselineEngine = "Managed",
                Metric = "MedianMs",
                Actual = 1500,
                Baseline = 1000,
                Ratio = 1.5,
                Variables = new Dictionary<string, string?> { ["ModuleName"] = "PSScriptAnalyzer" }
            }
        });

        Assert.Contains("| Scenario | Host | Operation | Managed | ModuleFast | Result |", markdown);
        Assert.Contains("| PSScriptAnalyzer | Core-7.6.3 | Install | 1.00x (1.00s) | 1.50x (1.50s) | Managed fastest |", markdown);
        Assert.DoesNotContain("Baseline Value", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkdownRenderer_PreservesMatrixAndMetricDimensions()
    {
        var markdown = new BenchmarkMarkdownRenderer().RenderComparisonTable(new[]
        {
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                BaselineEngine = "Managed",
                Metric = "MedianMs",
                Actual = 10,
                Baseline = 10,
                Ratio = 1,
                Variables = new Dictionary<string, string?> { ["Rows"] = "10" }
            },
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                BaselineEngine = "Managed",
                Metric = "MedianMs",
                Actual = 20,
                Baseline = 10,
                Ratio = 2,
                Variables = new Dictionary<string, string?> { ["Rows"] = "10" }
            },
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                BaselineEngine = "Managed",
                Metric = "P95Ms",
                Actual = 15,
                Baseline = 15,
                Ratio = 1,
                Variables = new Dictionary<string, string?> { ["Rows"] = "20" }
            },
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                BaselineEngine = "Managed",
                Metric = "P95Ms",
                Actual = 30,
                Baseline = 15,
                Ratio = 2,
                Variables = new Dictionary<string, string?> { ["Rows"] = "20" }
            }
        });

        Assert.Contains("| Scenario | Variables | Host | Operation | Metric | Managed | Other | Result |", markdown);
        Assert.Contains("| case | Rows=10 | Current | Run | MedianMs | 1.00x (10ms) | 2.00x (20ms) | Managed fastest |", markdown);
        Assert.Contains("| case | Rows=20 | Current | Run | P95Ms | 1.00x (15ms) | 2.00x (30ms) | Managed fastest |", markdown);
    }

    [Fact]
    public void MarkdownRenderer_PreservesBaselineDimension()
    {
        var markdown = new BenchmarkMarkdownRenderer().RenderComparisonTable(new[]
        {
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                BaselineEngine = "Managed",
                Metric = "MedianMs",
                Actual = 10,
                Baseline = 10,
                Ratio = 1
            },
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                BaselineEngine = "Managed",
                Metric = "MedianMs",
                Actual = 20,
                Baseline = 10,
                Ratio = 2
            },
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                BaselineEngine = "Other",
                Metric = "MedianMs",
                Actual = 10,
                Baseline = 20,
                Ratio = 0.5
            },
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                BaselineEngine = "Other",
                Metric = "MedianMs",
                Actual = 20,
                Baseline = 20,
                Ratio = 1
            }
        });

        Assert.Contains("| Scenario | Host | Operation | Baseline | Managed | Other | Result |", markdown);
        Assert.Contains("| case | Current | Run | Managed | 1.00x (10ms) | 2.00x (20ms) | Managed fastest |", markdown);
        Assert.Contains("| case | Current | Run | Other | 0.50x (10ms) | 1.00x (20ms) | Other slower than Managed |", markdown);
    }

    [Fact]
    public void MarkdownRenderer_RendersSkippedComparisonRowsAsSkipped()
    {
        var markdown = new BenchmarkMarkdownRenderer().RenderComparisonTable(new[]
        {
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                BaselineEngine = "Managed",
                Metric = "MedianMs",
                Status = "Succeeded",
                Actual = 10,
                Baseline = 10,
                Ratio = 1
            },
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                BaselineEngine = "Managed",
                Metric = "MedianMs",
                Status = "Skipped",
                Baseline = 10
            }
        });

        Assert.Contains("| case | Current | Run | 1.00x (10ms) | Skipped | Managed only successful |", markdown);
        Assert.DoesNotContain("Failed", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkdownRenderer_DoesNotFormatCustomMetricsAsDurations()
    {
        var markdown = new BenchmarkMarkdownRenderer().RenderComparisonTable(new[]
        {
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                BaselineEngine = "Managed",
                Metric = "RowsPerSecond",
                Actual = 1000,
                Baseline = 1000,
                Ratio = 1
            },
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                BaselineEngine = "Managed",
                Metric = "RowsPerSecond",
                Actual = 1200,
                Baseline = 1000,
                Ratio = 1.2
            }
        });

        Assert.Contains("| case | Current | Run | RowsPerSecond | 1.00x (1000) | 1.20x (1200) | Managed baseline |", markdown);
        Assert.DoesNotContain("1.00s", markdown, StringComparison.Ordinal);
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
    public void UpdateBenchmarkDocumentCommand_AllowsComparisonRendererWithoutSummaryPath()
    {
        var root = CreateTempRoot();
        var readme = Path.Combine(root, "README.md");
        var comparisonPath = Path.Combine(root, "comparison.json");
        File.WriteAllText(readme, "<!-- BENCHMARK:results:START -->\nold\n<!-- BENCHMARK:results:END -->\n");
        BenchmarkJson.Write(comparisonPath, new[]
        {
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                BaselineEngine = "Managed",
                Metric = "MedianMs",
                Actual = 20,
                Baseline = 10,
                Ratio = 2
            }
        });

        var initialSessionState = InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new SessionStateCmdletEntry("Update-BenchmarkDocument", typeof(PSPublishModule.UpdateBenchmarkDocumentCommand), helpFileName: null));
        using var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        using var ps = PowerShell.Create(runspace);
        ps.AddCommand("Update-BenchmarkDocument")
            .AddParameter("Path", readme)
            .AddParameter("BlockId", "results")
            .AddParameter("ComparisonPath", comparisonPath)
            .AddParameter("Renderer", "ComparisonTable");

        var output = ps.Invoke();

        Assert.False(ps.HadErrors);
        Assert.Single(output);
        var text = File.ReadAllText(readme);
        Assert.Contains("Other", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Managed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("old", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateBenchmarkDocumentCommand_ReadsComparisonFromRunReport()
    {
        var root = CreateTempRoot();
        var readme = Path.Combine(root, "README.md");
        var runReportPath = Path.Combine(root, "run-report.json");
        File.WriteAllText(readme, "<!-- BENCHMARK:results:START -->\nold\n<!-- BENCHMARK:results:END -->\n");
        BenchmarkJson.Write(runReportPath, new BenchmarkRunResult
        {
            Suite = "demo",
            Comparison = new[]
            {
                new BenchmarkComparisonRow
                {
                    Suite = "demo",
                    Scenario = "case",
                    Operation = "Run",
                    Engine = "Other",
                    Host = "Current",
                    BaselineEngine = "Managed",
                    Metric = "MedianMs",
                    Actual = 20,
                    Baseline = 10,
                    Ratio = 2
                }
            }
        });

        var initialSessionState = InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new SessionStateCmdletEntry("Update-BenchmarkDocument", typeof(PSPublishModule.UpdateBenchmarkDocumentCommand), helpFileName: null));
        using var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        using var ps = PowerShell.Create(runspace);
        ps.AddCommand("Update-BenchmarkDocument")
            .AddParameter("Path", readme)
            .AddParameter("BlockId", "results")
            .AddParameter("ComparisonPath", runReportPath)
            .AddParameter("Renderer", "ComparisonTable");

        var output = ps.Invoke();

        Assert.False(ps.HadErrors);
        Assert.Single(output);
        var text = File.ReadAllText(readme);
        Assert.Contains("Other", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Managed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("old", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestBenchmarkGateCommand_DefaultGroupByIncludesRunMode()
    {
        var groupBy = typeof(PSPublishModule.TestBenchmarkGateCommand)
            .GetProperty(nameof(PSPublishModule.TestBenchmarkGateCommand.GroupBy))!
            .GetValue(new PSPublishModule.TestBenchmarkGateCommand()) as string[];

        Assert.Contains("RunMode", groupBy!);
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
    public void GateService_RejectsNonFiniteTolerances()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", MedianMs = 100 }
        });

        Assert.Throws<ArgumentException>(() => new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            BaselineMode = BenchmarkBaselineMode.Update,
            RelativeTolerance = double.NaN
        }));

        Assert.Throws<ArgumentException>(() => new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            BaselineMode = BenchmarkBaselineMode.Update,
            AbsoluteToleranceMs = double.PositiveInfinity
        }));
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
    public void GateService_PreservesCaseSensitiveLaneKeys()
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
                MedianMs = 10,
                Variables = new Dictionary<string, string?> { ["Input"] = "abc" }
            },
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                MedianMs = 20,
                Variables = new Dictionary<string, string?> { ["Input"] = "ABC" }
            }
        });

        var update = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            BaselineMode = BenchmarkBaselineMode.Update
        });
        var verify = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath
        });

        Assert.True(update.Passed);
        Assert.True(update.BaselineUpdated);
        Assert.Equal(2, update.Metrics.Length);
        Assert.Equal(2, update.Metrics.Select(metric => metric.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.True(verify.Passed);
        Assert.DoesNotContain(verify.Metrics, metric => metric.MissingInBaseline || metric.MissingInCurrent);
    }

    [Fact]
    public void GateService_TrimsVariableGroupFields()
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
                MedianMs = 10,
                Variables = new Dictionary<string, string?> { ["Input"] = "A" }
            },
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                MedianMs = 20,
                Variables = new Dictionary<string, string?> { ["Input"] = "B" }
            }
        });

        var update = new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            BaselineMode = BenchmarkBaselineMode.Update,
            GroupBy = new[] { "Suite", " Variables.Input " }
        });

        Assert.True(update.Passed);
        Assert.Equal(2, update.Metrics.Length);
        Assert.Contains(update.Metrics, metric => metric.Key.Contains("|A|", StringComparison.Ordinal));
        Assert.Contains(update.Metrics, metric => metric.Key.Contains("|B|", StringComparison.Ordinal));
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
                ["suite|case|Run|Managed|Current||||MedianMs"] = 100
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
    public void GateService_RejectsNonFiniteBaselineMetric()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        File.WriteAllText(baselinePath, """{"metrics":{"suite|case|Run|Managed|Current||||MedianMs":1e999}}""");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", MedianMs = 100 }
        });

        var ex = Assert.Throws<InvalidOperationException>(() => new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath
        }));

        Assert.Contains("finite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GateService_RejectsNonNumericBaselineMetric()
    {
        var root = CreateTempRoot();
        var summaryPath = Path.Combine(root, "summary.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        File.WriteAllText(baselinePath, """{"metrics":{"suite|case|Run|Managed|Current||||MedianMs":"100"}}""");
        BenchmarkJson.Write(summaryPath, new[]
        {
            new BenchmarkSummaryRow { Suite = "suite", Scenario = "case", Operation = "Run", Engine = "Managed", Host = "Current", MedianMs = 100 }
        });

        var ex = Assert.Throws<InvalidOperationException>(() => new BenchmarkGateService().Evaluate(new BenchmarkGateRequest
        {
            SummaryPath = summaryPath,
            BaselinePath = baselinePath,
            FailOnNew = false
        }));

        Assert.Contains("finite number", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    private static PowerShellBenchmarkSuite[] EvaluateBenchmarkDsl(ScriptBlock scriptBlock, string? scriptRoot = null, IReadOnlyDictionary<string, string?>? benchmarkVariables = null)
    {
        if (Runspace.DefaultRunspace is null)
        {
            Runspace.DefaultRunspace = BenchmarkDslRunspace.Value;
        }

        ImportBenchmarkDslCommands(Runspace.DefaultRunspace);
        return PowerShellBenchmarkDslRuntime.Evaluate(scriptBlock, scriptRoot, benchmarkVariables);
    }

    private static Runspace CreateBenchmarkDslRunspace()
    {
        var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        return runspace;
    }

    private static void ImportBenchmarkDslCommands(Runspace runspace)
    {
        using var powerShell = PowerShell.Create(runspace);
        powerShell.AddCommand("Import-Module")
            .AddArgument(typeof(PSPublishModule.InvokeBenchmarkSuiteCommand).Assembly.Location)
            .AddParameter("Force");
        powerShell.Invoke();
        if (!powerShell.HadErrors) return;

        var message = string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString()));
        throw new InvalidOperationException("Failed to import PSPublishModule benchmark commands for test evaluation." + Environment.NewLine + message);
    }

}
