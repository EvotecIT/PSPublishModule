using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void MarkdownRenderer_LabelsDurationResultsWithinConfiguredToleranceAsTied()
    {
        var markdown = new BenchmarkMarkdownRenderer().RenderComparisonTable(new[]
        {
            new BenchmarkComparisonRow
            {
                Scenario = "baseline narrowly faster",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                BaselineEngine = "Managed",
                Actual = 100,
                Baseline = 100,
                Ratio = 1,
                TieTolerance = 0.05
            },
            new BenchmarkComparisonRow
            {
                Scenario = "baseline narrowly faster",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                BaselineEngine = "Managed",
                Actual = 104,
                Baseline = 100,
                Ratio = 1.04,
                TieTolerance = 0.05
            },
            new BenchmarkComparisonRow
            {
                Scenario = "baseline narrowly slower",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                BaselineEngine = "Managed",
                Actual = 104,
                Baseline = 104,
                Ratio = 1,
                TieTolerance = 0.05
            },
            new BenchmarkComparisonRow
            {
                Scenario = "baseline narrowly slower",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                BaselineEngine = "Managed",
                Actual = 100,
                Baseline = 104,
                Ratio = 100d / 104d,
                TieTolerance = 0.05
            },
            new BenchmarkComparisonRow
            {
                Scenario = "baseline materially slower",
                Operation = "Run",
                Engine = "Managed",
                Host = "Current",
                BaselineEngine = "Managed",
                Actual = 106,
                Baseline = 106,
                Ratio = 1,
                TieTolerance = 0.05
            },
            new BenchmarkComparisonRow
            {
                Scenario = "baseline materially slower",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                BaselineEngine = "Managed",
                Actual = 100,
                Baseline = 106,
                Ratio = 100d / 106d,
                TieTolerance = 0.05
            }
        });

        Assert.Contains("| baseline narrowly faster | Current | Run | 1.00x (100ms) | 1.04x (104ms) | Managed tied with Other |", markdown);
        Assert.Contains("| baseline narrowly slower | Current | Run | 1.00x (104ms) | 0.96x (100ms) | Managed tied with Other |", markdown);
        Assert.Contains("| baseline materially slower | Current | Run | 1.00x (106ms) | 0.94x (100ms) | Managed slower than Other |", markdown);
    }

    [Fact]
    public void MarkdownRenderer_PreservesExactRankingWithoutConfiguredTieTolerance()
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
                Actual = 101,
                Baseline = 101,
                Ratio = 1
            },
            new BenchmarkComparisonRow
            {
                Scenario = "case",
                Operation = "Run",
                Engine = "Other",
                Host = "Current",
                BaselineEngine = "Managed",
                Actual = 100,
                Baseline = 101,
                Ratio = 100d / 101d
            }
        });

        Assert.Contains("Managed slower than Other", markdown);
        Assert.DoesNotContain("tied", markdown, StringComparison.OrdinalIgnoreCase);
    }
}
