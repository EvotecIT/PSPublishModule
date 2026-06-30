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

    private static BenchmarkSample Sample(string suite, string scenario, string operation, string engine, double durationMs)
        => new()
        {
            RunId = "run",
            Suite = suite,
            Scenario = scenario,
            Operation = operation,
            Engine = engine,
            Host = "Current",
            Status = BenchmarkSampleStatus.Succeeded,
            DurationMs = durationMs
        };

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "pf-benchmark-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
