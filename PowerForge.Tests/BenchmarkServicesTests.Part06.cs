using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Theory]
    [InlineData("Scenario")]
    [InlineData("Name")]
    [InlineData("Suite")]
    [InlineData("RunId")]
    [InlineData("Status")]
    [InlineData("DurationMs")]
    [InlineData("Reason")]
    public void Runner_RejectsReservedMatrixAxisNames(string axisName)
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = axisName, Values = { "one", "two" } });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("reserved matrix axis", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(axisName, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Scenario")]
    [InlineData("Operation")]
    [InlineData("Engine")]
    [InlineData("Host")]
    [InlineData("Status")]
    [InlineData("DurationMs")]
    public void Runner_RejectsReservedCaseVariableNames(string variableName)
    {
        var suite = CreateRunnableSuite();
        var benchmarkCase = new PowerShellBenchmarkCase { Name = "Lookup" };
        benchmarkCase.Values[variableName] = "value";
        suite.Cases.Add(benchmarkCase);

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("reserved case variable", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(variableName, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RejectsProfileAxisAsSuiteMetadata()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Profile", Values = { "Current", "TemporaryLocalUser" } });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("Profile axis", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("suite metadata", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_UsesPlanningProfileForSkipRules()
    {
        var suite = CreateRunnableSuite();
        suite.Profile = PowerShellBenchmarkProfileKind.Current;
        suite.PlanningProfile = PowerShellBenchmarkProfileKind.TemporaryLocalUser;
        suite.Skip = ScriptBlock.Create("param($case) $case.Profile -ne 'TemporaryLocalUser'");

        var item = Assert.Single(new PowerShellBenchmarkRunner().Plan(suite));

        Assert.False(item.IsSkipped);
        Assert.Equal("TemporaryLocalUser", item.Values["Profile"]);
    }

    [Fact]
    public void Runner_RejectsMatrixAxesThatShadowCaseVariables()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { 20 } });
        suite.Cases.Add(new PowerShellBenchmarkCase
        {
            Name = "Lookup",
            Values = { ["Rows"] = 10 }
        });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("conflicts", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rows", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RejectsMetricVariableCsvHeaderCollisions()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { 10 } });
        suite.Metrics.Add(new PowerShellBenchmarkMetric { Name = "Rows", ScriptBlock = ScriptBlock.Create("42") });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("Rows", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("variable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RejectsDuplicateProgrammaticMetricNames()
    {
        var suite = CreateRunnableSuite();
        suite.Metrics.Add(new PowerShellBenchmarkMetric { Name = "Rows", ScriptBlock = ScriptBlock.Create("1") });
        suite.Metrics.Add(new PowerShellBenchmarkMetric { Name = "rows", ScriptBlock = ScriptBlock.Create("2") });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("duplicate metric", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rows", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Runner_IgnoresStaleGlobalLastExitCodeWhenNoNativeCommandRuns()
    {
        var previousRunspace = Runspace.DefaultRunspace;
        using var runspace = RunspaceFactory.CreateRunspace();
        runspace.Open();
        Runspace.DefaultRunspace = runspace;
        using (var ps = PowerShell.Create(runspace))
        {
            ps.AddScript("$global:LASTEXITCODE = 42");
            ps.Invoke();
        }
        var script = ScriptBlock.Create(@"
benchmark 'stale-native-exit' {
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) $run.Seen = 'ok' } }
}
");

        try
        {
            var suite = Assert.Single(EvaluateBenchmarkDsl(script));
            suite.WarmupCount = 0;
            suite.IterationCount = 1;
            var result = new PowerShellBenchmarkRunner().Run(suite);

            var sample = Assert.Single(result.Samples);
            Assert.Equal(BenchmarkSampleStatus.Succeeded, sample.Status);
        }
        finally
        {
            Runspace.DefaultRunspace = previousRunspace;
        }
    }

    [Fact]
    public void Runner_EvaluatesSkipRulesOnlyDuringPlanning()
    {
        var suite = CreateRunnableSuite();
        suite.Skip = ScriptBlock.Create("if ($null -eq $script:SkipCounter) { $script:SkipCounter = 0 }; $script:SkipCounter++; $script:SkipCounter -gt 1");

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
            Values = { ["Rows"] = 10 }
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
    public void Runner_HonorsSequentialRunOrderPolicy()
    {
        var suite = CreateRunnableSuite();
        suite.IterationCount = 2;
        suite.RunOrder = PowerShellBenchmarkRunOrder.Sequential;
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { 1, 2, 3 } });

        var result = new PowerShellBenchmarkRunner().Run(suite);
        var firstIteration = result.Samples.Where(sample => sample.Iteration == 0).Select(sample => sample.Variables["Rows"]).ToArray();
        var secondIteration = result.Samples.Where(sample => sample.Iteration == 1).Select(sample => sample.Variables["Rows"]).ToArray();

        Assert.Equal(new[] { "1", "2", "3" }, firstIteration);
        Assert.Equal(new[] { "1", "2", "3" }, secondIteration);
        Assert.Equal("Sequential", result.Metadata["runOrder"]);
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
    public void Runner_DoesNotRecordNonFiniteCustomMetrics()
    {
        var suite = CreateRunnableSuite();
        suite.Metrics.Add(new PowerShellBenchmarkMetric { Name = "NotANumber", ScriptBlock = ScriptBlock.Create("[double]::NaN") });
        suite.Metrics.Add(new PowerShellBenchmarkMetric { Name = "Infinite", ScriptBlock = ScriptBlock.Create("[double]::PositiveInfinity") });

        var result = new PowerShellBenchmarkRunner().Run(suite);
        var sample = Assert.Single(result.Samples);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, sample.Status);
        Assert.DoesNotContain("NotANumber", sample.Metrics.Keys);
        Assert.DoesNotContain("Infinite", sample.Metrics.Keys);
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
        Assert.Contains("Suite,Scenario,Operation,Engine,Host,OS,RunMode,Rows,Iteration,Status,DurationMs,Reason,TinyMetric", samplesCsv);
        Assert.Contains($",Managed,{result.Samples[0].Host},{result.Samples[0].Os},{result.Samples[0].RunMode},10,0,Succeeded", samplesCsv);
        Assert.Contains(result.Samples[0].DurationMs.ToString("G17", System.Globalization.CultureInfo.InvariantCulture), samplesCsv);
        Assert.Contains(",0.00042", samplesCsv);
        Assert.Contains("Suite,Scenario,Operation,Engine,Host,OS,RunMode,Rows,SampleCount,FailureCount,OutlierCount,Status,MedianMs,MeanMs,MinMs,MaxMs,P95Ms,P99Ms,StdDevMs,StdErrMs,FailureReasons,TinyMetric", summaryCsv);
        Assert.Contains(",0.00042", summaryCsv);
    }

    [Fact]
    public void Runner_EscapesCsvArtifactHeaders()
    {
        var suite = CreateRunnableSuite();
        suite.Artifacts = BenchmarkArtifactKind.Csv;
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows,Size", Values = { 10 } });
        suite.Metrics.Add(new PowerShellBenchmarkMetric { Name = "Metric,Name", ScriptBlock = ScriptBlock.Create("1") });

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var samplesHeader = File.ReadLines(result.Artifacts["samples.csv"]).First();
        var summaryHeader = File.ReadLines(result.Artifacts["summary.csv"]).First();
        Assert.Contains("\"Rows,Size\"", samplesHeader, StringComparison.Ordinal);
        Assert.Contains("\"Metric,Name\"", samplesHeader, StringComparison.Ordinal);
        Assert.Contains("\"Rows,Size\"", summaryHeader, StringComparison.Ordinal);
        Assert.Contains("\"Metric,Name\"", summaryHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownRenderer_RendersConflictingVariableNames()
    {
        var markdown = new BenchmarkMarkdownRenderer().RenderSummaryTable(new[]
        {
            new BenchmarkSummaryRow
            {
                Suite = "suite",
                Scenario = "case",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                Status = "Succeeded",
                Variables = new Dictionary<string, string?>
                {
                    ["Engine"] = "ParamEngine",
                    ["Operation"] = "ParamOperation",
                    ["Host"] = "ParamHost",
                    ["Scenario"] = "ParamScenario"
                }
            }
        });

        Assert.Contains("Engine=ParamEngine", markdown, StringComparison.Ordinal);
        Assert.Contains("Operation=ParamOperation", markdown, StringComparison.Ordinal);
        Assert.Contains("Host=ParamHost", markdown, StringComparison.Ordinal);
        Assert.Contains("Scenario=ParamScenario", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_TreatsNonTerminatingHandlerErrorsAsFailures()
    {
        var suite = CreateRunnableSuite();
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) Write-Error 'boom'");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Contains("Operation failed", sample.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("boom", sample.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_ReportsFailureStageAndPowerShellLocation()
    {
        var suite = CreateRunnableSuite();
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("""
param($case, $run)
throw 'handler exploded'
""");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Contains("Operation failed", sample.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("handler exploded", sample.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Line:", sample.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("throw 'handler exploded'", sample.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RecordsElapsedDurationForFailedOperations()
    {
        var suite = CreateRunnableSuite();
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) Start-Sleep -Milliseconds 20; throw 'late failure'");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Contains("Operation failed", sample.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(sample.DurationMs > 0);
    }

    [Fact]
    public void Runner_ReportsValidationFailureStage()
    {
        var suite = CreateRunnableSuite();
        suite.Validate = ScriptBlock.Create("param($case, $run) throw 'bad output'");

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Contains("Validation failed", sample.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bad output", sample.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_ReportsMetricFailureStage()
    {
        var suite = CreateRunnableSuite();
        suite.Metrics.Add(new PowerShellBenchmarkMetric { Name = "Rows", ScriptBlock = ScriptBlock.Create("throw 'metric broke'") });

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Contains("Metrics failed", sample.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("metric broke", sample.Reason, StringComparison.OrdinalIgnoreCase);
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
    public void Runner_RethrowsPipelineStoppedExceptions()
    {
        var suite = CreateRunnableSuite();
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) throw [System.Management.Automation.PipelineStoppedException]::new()");

        var ex = Assert.ThrowsAny<Exception>(() => new PowerShellBenchmarkRunner().Run(suite));

        Assert.Contains("PipelineStopped", ex.ToString(), StringComparison.OrdinalIgnoreCase);
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
    public void Runner_PreservesFirstNativeFailureWhenLaterCommandResetsExitCode()
    {
        var suite = CreateRunnableSuite();
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) $global:LASTEXITCODE = 23; $global:LASTEXITCODE = 0");

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
    public void Runner_WritesFailureArtifactsBeforeComparisonErrors()
    {
        var suite = CreateRunnableSuite();
        suite.Artifacts = BenchmarkArtifactKind.Json;
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create("param($case, $run) throw 'baseline failed'");
        var other = new PowerShellBenchmarkEngine { Name = "Other" };
        other.Operations["Run"] = ScriptBlock.Create("param($case, $run)");
        suite.Engines.Add(other);
        suite.Axes.Single(axis => axis.Name == "Engine").Values.Add("Other");
        suite.Comparisons.Add(new PowerShellBenchmarkComparison { Dimension = "Engine", Baseline = "Managed" });

        var ex = Assert.Throws<InvalidOperationException>(() => new PowerShellBenchmarkRunner().Run(suite));
        var samplesPath = Assert.Single(Directory.GetFiles(suite.OutputRoot, "samples.json", SearchOption.AllDirectories));
        var summaryPath = Assert.Single(Directory.GetFiles(suite.OutputRoot, "summary.json", SearchOption.AllDirectories));
        var reportPath = Assert.Single(Directory.GetFiles(suite.OutputRoot, "run-report.json", SearchOption.AllDirectories));
        var samples = BenchmarkJson.Read<BenchmarkSample[]>(samplesPath);

        Assert.Contains("Managed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MedianMs", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(samples, sample => sample.Engine == "Managed" && sample.Status == BenchmarkSampleStatus.Failed);
        Assert.True(File.Exists(summaryPath));
        Assert.True(File.Exists(reportPath));
    }

    [Fact]
    public void Runner_UsesDefaultComparisonMetricDuringExecution()
    {
        var suite = CreateRunnableSuite();
        var other = new PowerShellBenchmarkEngine { Name = "Other" };
        other.Operations["Run"] = ScriptBlock.Create("param($case, $run)");
        suite.Engines.Add(other);
        suite.Axes.Single(axis => axis.Name == "Engine").Values.Add("Other");
        suite.Comparisons.Add(new PowerShellBenchmarkComparison
        {
            Dimension = "Engine",
            Baseline = "Managed",
            Metrics = Array.Empty<string>()
        });

        var result = new PowerShellBenchmarkRunner().Run(suite);

        var row = Assert.Single(result.Comparison, row => row.Engine == "Other");
        Assert.Equal("MedianMs", row.Metric);
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
    public void Runner_RejectsUnsupportedComparisonMetricsBeforeMeasurement()
    {
        var suite = CreateRunnableSuite();
        var output = Path.Combine(suite.OutputRoot, "comparison-metric-side-effect.txt");
        suite.Metrics.Add(new PowerShellBenchmarkMetric { Name = "RowsPerSecond", ScriptBlock = ScriptBlock.Create("42") });
        suite.Engines[0].Operations["Run"] = ScriptBlock.Create($"[IO.File]::WriteAllText('{output.Replace("'", "''")}', 'executed')");
        suite.Comparisons.Add(new PowerShellBenchmarkComparison
        {
            Dimension = "Engine",
            Baseline = "Managed",
            Metrics = new[] { "RowsPerSecondd" }
        });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Run(suite));

        Assert.Contains("RowsPerSecondd", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("P95")]
    [InlineData("P99")]
    [InlineData("StdDev")]
    [InlineData("StdErr")]
    public void Runner_AllowsTimingAliasComparisonMetrics(string metricName)
    {
        var suite = CreateRunnableSuite();
        suite.IterationCount = 2;
        var other = new PowerShellBenchmarkEngine { Name = "Other" };
        other.Operations["Run"] = ScriptBlock.Create("param($case, $run)");
        suite.Engines.Add(other);
        suite.Axes.Single(axis => axis.Name == "Engine").Values.Add("Other");
        suite.Comparisons.Add(new PowerShellBenchmarkComparison
        {
            Dimension = "Engine",
            Baseline = "Managed",
            Metrics = new[] { metricName }
        });

        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Contains(result.Comparison, row => row.Engine == "Other" && row.Metric == metricName && row.Actual.HasValue);
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

}
