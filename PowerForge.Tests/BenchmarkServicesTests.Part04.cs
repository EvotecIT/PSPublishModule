using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
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
    public void Importer_PreservesBenchmarkDotNetJsonPercentiles()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "Method": "Write",
      "Statistics": {
        "Median": 200,
        "Percentiles": {
          "P95": 950,
          "P99": 990
        }
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);
        var row = Assert.Single(result.Summary);

        Assert.InRange(row.Metrics["P95"], 0.000949, 0.000951);
        Assert.InRange(row.Metrics["P99"], 0.000989, 0.000991);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetJsonDeviationStatistics()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "Method": "Write",
      "Statistics": {
        "Median": 200,
        "StandardError": 50,
        "StandardDeviation": 120
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);
        var row = Assert.Single(result.Summary);

        Assert.InRange(row.Metrics["StdErr"], 0.000049, 0.000051);
        Assert.InRange(row.Metrics["StdDev"], 0.000119, 0.000121);
        Assert.InRange(row.Metrics["StandardError"], 0.000049, 0.000051);
        Assert.InRange(row.Metrics["StandardDeviation"], 0.000119, 0.000121);
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
    public void Importer_PreservesNormalizedSampleOptionalFields()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,Iteration,Status,DurationMs,AllocatedBytes,WorkingSetDeltaBytes,OutputMetric,Reason\nsuite,case,Run,Managed,Current,0,Succeeded,12.5,4096,-128,42,\n");

        var sample = Assert.Single(new BenchmarkResultImporter().Import(csv).Samples);

        Assert.Equal(4096, sample.AllocatedBytes);
        Assert.Equal(-128, sample.WorkingSetDeltaBytes);
        Assert.Equal(42, sample.OutputMetric);
        Assert.DoesNotContain("AllocatedBytes", sample.Variables.Keys);
        Assert.DoesNotContain("WorkingSetDeltaBytes", sample.Variables.Keys);
        Assert.DoesNotContain("OutputMetric", sample.Variables.Keys);
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
    public void Importer_NormalizesBomCsvHeaders()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "benchmark-report.csv");
        File.WriteAllText(csv, "\uFEFFMethod,Mean [ms]\nFast,12.5\n");

        var sample = Assert.Single(new BenchmarkResultImporter().Import(csv, "demo").Samples);

        Assert.Equal("Fast", sample.Scenario);
        Assert.Equal(12.5, sample.DurationMs);
        Assert.DoesNotContain(sample.Variables.Keys, key => key.Contains("Method", StringComparison.OrdinalIgnoreCase));
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
    public void Importer_PreservesRunnerVariablesNamedMeanAndMedian()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,Mean,Median,Iteration,Status,DurationMs,Reason\nsuite,CaseA,Run,Managed,Current,small,typical,0,Succeeded,12.5,\n");

        var result = new BenchmarkResultImporter().Import(csv);
        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);

        Assert.Equal("small", sample.Variables["Mean"]);
        Assert.Equal("typical", sample.Variables["Median"]);
        Assert.Equal("small", row.Variables["Mean"]);
        Assert.Equal("typical", row.Variables["Median"]);
        Assert.DoesNotContain("Mean", sample.Metrics.Keys);
        Assert.DoesNotContain("Median", sample.Metrics.Keys);
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
    public void Importer_UsesSummaryCsvSuiteForSummaryOnlyDirectory()
    {
        var root = CreateTempRoot();
        var run = Path.Combine(root, "run1");
        Directory.CreateDirectory(run);
        File.WriteAllText(
            Path.Combine(run, "summary.csv"),
            "Suite,Scenario,Operation,Engine,Host,SampleCount,FailureCount,Status,MedianMs\nactual,case,Run,Managed,Current,1,0,Succeeded,12.5\n");

        var result = new BenchmarkResultImporter().Import(root);

        Assert.Equal("actual", result.Suite);
        Assert.Equal("actual", Assert.Single(result.Summary).Suite);
    }

    [Fact]
    public void RunnerPathSegments_EscapeInvalidCharactersWithoutCollisions()
    {
        var colon = PowerShellBenchmarkPathSegments.Value("Case:A");
        var question = PowerShellBenchmarkPathSegments.Value("Case?A");

        Assert.NotEqual(colon, question);
        Assert.Contains("%3A", colon, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%3F", question, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunnerPathSegments_DistinguishEmptyAndWhitespaceValues()
    {
        Assert.Equal("_", PowerShellBenchmarkPathSegments.Value(string.Empty));
        Assert.Equal("%20", PowerShellBenchmarkPathSegments.Value(" "));
        Assert.NotEqual(PowerShellBenchmarkPathSegments.Value(string.Empty), PowerShellBenchmarkPathSegments.Value(" "));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("PRN.txt")]
    [InlineData("COM1")]
    [InlineData("LPT9.log")]
    [InlineData("case.")]
    public void RunnerPathSegments_EscapeWindowsReservedSegments(string value)
    {
        var segment = PowerShellBenchmarkPathSegments.Value(value);

        Assert.NotEqual(value, segment);
        Assert.False(segment.EndsWith(".", StringComparison.Ordinal));
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

        var suites = EvaluateBenchmarkDsl(script);
        var runner = new PowerShellBenchmarkRunner();

        Assert.Equal(2, suites.Length);
        Assert.Single(runner.Plan(suites[0]));
        Assert.Single(runner.Plan(suites[1]));
    }

    [Fact]
    public void DslRuntime_DoesNotCaptureAutomaticPwdVariable()
    {
        var root = CreateTempRoot();
        var child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName.Replace("'", "''");
        var script = ScriptBlock.Create($@"
benchmark 'pwd' {{
    cases {{ case A @{{}} }}
    axis Operation Run
    axis Engine Managed
    engine Managed {{
        operation Run {{
            param($case, $run)
            Push-Location -LiteralPath '{child}'
            try {{
                if ($PWD.ProviderPath -ne (Get-Location).ProviderPath) {{
                    throw ""PWD was captured instead of following the current location.""
                }}
            }} finally {{
                Pop-Location
            }}
        }}
    }}
}}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script, root));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
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

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

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

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

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

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

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

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

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

        var suite = Assert.Single(EvaluateBenchmarkDsl(script, root));
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

        var suite = Assert.Single(EvaluateBenchmarkDsl(script, root));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
    }

    [Fact]
    public void DslRuntime_PreservesPSScriptRootInExpandableStrings()
    {
        var root = CreateTempRoot();
        var fixture = Path.Combine(root, "fixture.txt");
        File.WriteAllText(fixture, "ok");
        var script = ScriptBlock.Create(@"
benchmark 'expandable-root' {
    axis Operation Run
    axis Engine Managed
    setup { param($case, $run) $run.FixtureText = Get-Content -LiteralPath ""$PSScriptRoot/fixture.txt"" -Raw }
    engine Managed { operation Run { param($case, $run) if ($run.FixtureText -ne 'ok') { throw 'expandable root missing' } } }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script, root));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
    }

    [Fact]
    public void DslRuntime_PreservesPSScriptRootInExpandableSubexpressions()
    {
        var root = CreateTempRoot();
        var fixture = Path.Combine(root, "fixture.txt");
        File.WriteAllText(fixture, "ok");
        var script = ScriptBlock.Create(@"
benchmark 'expandable-subexpression-root' {
    axis Operation Run
    axis Engine Managed
    setup { param($case, $run) $run.FixtureText = Get-Content -LiteralPath ""$($PSScriptRoot)/fixture.txt"" -Raw }
    engine Managed { operation Run { param($case, $run) if ($run.FixtureText -ne 'ok') { throw 'expandable subexpression root missing' } } }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script, root));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
    }

    [Fact]
    public void DslRuntime_DoesNotCaptureErrorPreferenceVariables()
    {
        var script = ScriptBlock.Create(@"
$ErrorActionPreference = 'Continue'
benchmark 'preferences' {
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) Write-Error 'should stop' } }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Contains("should stop", sample.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_DoesNotCaptureDefaultParameterValues()
    {
        var script = ScriptBlock.Create(@"
$PSDefaultParameterValues = @{ 'Write-Error:ErrorAction' = 'SilentlyContinue' }
benchmark 'default-parameter-values' {
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) Write-Error 'should stop' } }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Contains("should stop", sample.Reason, StringComparison.OrdinalIgnoreCase);
    }

}
