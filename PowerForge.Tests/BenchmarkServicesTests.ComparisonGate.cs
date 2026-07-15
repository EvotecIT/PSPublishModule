using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void BenchmarkDsl_PropagatesFastestBaselineGate()
    {
        var script = System.Management.Automation.ScriptBlock.Create("""
benchmark 'gate' {
    axis Operation Run
    axis Engine Managed, Other
    engine Managed { operation Run { param($case, $run) } }
    engine Other { operation Run { param($case, $run) } }
    comparison Engine -Baseline Managed -Metric MedianMs -TieTolerance 0.05 -RequireBaselineFastest
}
""");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script));
        var comparison = Assert.Single(suite.Comparisons);

        Assert.True(comparison.RequireBaselineFastest);
        Assert.Equal(0.05, comparison.TieTolerance);
    }

    [Fact]
    public void ComparisonGate_AllowsBaselineWithinTieTolerance()
    {
        var suite = CreateGateSuite(tieTolerance: 0.05);
        var summary = new[]
        {
            GateSummary("Managed", 104),
            GateSummary("Other", 100)
        };

        PowerShellBenchmarkComparisonEvaluator.ValidateGates(suite, summary);
    }

    [Fact]
    public void ComparisonGate_RejectsMateriallySlowerBaseline()
    {
        var suite = CreateGateSuite(tieTolerance: 0.05);
        var summary = new[]
        {
            GateSummary("Managed", 106),
            GateSummary("Other", 100)
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => PowerShellBenchmarkComparisonEvaluator.ValidateGates(suite, summary));

        Assert.Contains("Managed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Other", exception.Message, StringComparison.Ordinal);
        Assert.Contains("5%", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparisonGate_RejectsFailedCompetitorLane()
    {
        var suite = CreateGateSuite(tieTolerance: 0.05);
        var failed = GateSummary("Other", null);
        failed.Status = "Failed";
        var summary = new[]
        {
            GateSummary("Managed", 100),
            failed
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => PowerShellBenchmarkComparisonEvaluator.ValidateGates(suite, summary));

        Assert.Contains("competitor Other failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparisonGate_RejectsPartiallyFailedCompetitorLaneWithTiming()
    {
        var suite = CreateGateSuite(tieTolerance: 0.05);
        var summary = new BenchmarkSummaryService().Summarize(new[]
        {
            GateSample("Managed", BenchmarkSampleStatus.Succeeded, 100, 0),
            GateSample("Other", BenchmarkSampleStatus.Succeeded, 90, 0),
            GateSample("Other", BenchmarkSampleStatus.Failed, 0, 1)
        });
        var competitor = Assert.Single(summary, row => row.Engine == "Other");

        Assert.Equal("Failed", competitor.Status);
        Assert.Equal(90, competitor.MedianMs);
        var exception = Assert.Throws<InvalidOperationException>(
            () => PowerShellBenchmarkComparisonEvaluator.ValidateGates(suite, summary));

        Assert.Contains("competitor Other failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparisonGate_RejectsFailedBaselineWhenEveryCompetitorIsSkipped()
    {
        var suite = CreateGateSuite(tieTolerance: 0.05);
        var failedBaseline = GateSummary("Managed", null);
        var skippedCompetitor = GateSummary("Other", null);
        skippedCompetitor.Status = "Skipped";
        var summary = new[] { failedBaseline, skippedCompetitor };

        var exception = Assert.Throws<InvalidOperationException>(
            () => PowerShellBenchmarkComparisonEvaluator.ValidateGates(suite, summary));

        Assert.Contains("baseline Managed failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HostExecutor_ChildRequestDefersComparisonGateValidation()
    {
        var request = new PowerShellBenchmarkHostRunRequest
        {
            SpecPath = "suite.benchmark.ps1",
            WorkingDirectory = "work",
            OutputRoot = "output"
        };

        var child = PowerShellBenchmarkHostExecutor.CreateChildRequest(
            request,
            "Current",
            "pwsh",
            "result.json",
            "readme-paths.txt",
            DateTimeOffset.UtcNow);

        Assert.False(child.ValidateComparisonGates);
    }

    [Fact]
    public void HostChildRunner_DisablesComparisonGatesOnlyWhenRequested()
    {
        var script = PowerForgeScripts.Load("Scripts/Benchmarks/TemporaryUserChildRunner.ps1");

        Assert.Contains("'ValidateComparisonGates'", script, StringComparison.Ordinal);
        Assert.Contains("$comparison.RequireBaselineFastest = $false", script, StringComparison.Ordinal);
    }

    private static PowerShellBenchmarkSuite CreateGateSuite(double tieTolerance)
    {
        var suite = new PowerShellBenchmarkSuite { Name = "gate" };
        suite.Comparisons.Add(new PowerShellBenchmarkComparison
        {
            Dimension = "Engine",
            Baseline = "Managed",
            Metrics = new[] { "MedianMs" },
            TieTolerance = tieTolerance,
            RequireBaselineFastest = true
        });
        return suite;
    }

    private static BenchmarkSummaryRow GateSummary(string engine, double? median)
        => new()
        {
            Suite = "gate",
            Scenario = "case",
            Operation = "Run",
            Engine = engine,
            Host = "Current",
            Os = "Windows",
            RunMode = "standard",
            Status = median.HasValue ? "Success" : "Failed",
            MedianMs = median,
            MeanMs = median,
            MinMs = median,
            MaxMs = median,
            P95Ms = median
        };

    private static BenchmarkSample GateSample(
        string engine,
        BenchmarkSampleStatus status,
        double durationMs,
        int iteration)
        => new()
        {
            RunId = "run",
            Suite = "gate",
            Scenario = "case",
            Operation = "Run",
            Engine = engine,
            Host = "Current",
            Os = "Windows",
            RunMode = "standard",
            Iteration = iteration,
            Status = status,
            DurationMs = durationMs
        };
}
