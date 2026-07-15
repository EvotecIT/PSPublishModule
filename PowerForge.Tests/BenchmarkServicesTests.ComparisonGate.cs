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
}
