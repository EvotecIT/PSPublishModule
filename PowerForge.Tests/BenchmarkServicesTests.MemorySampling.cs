using System.Management.Automation;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class PowerShellBenchmarkMemorySamplingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Operation")]
    [InlineData("Validation")]
    public void Sampling_keeps_operation_observations_on_success_and_failure(string failure)
    {
        string root = Path.Combine(Path.GetTempPath(), "pf-memory-sampling-" + Guid.NewGuid().ToString("N"));
        var suite = CreateSuite(root);
        suite.MemorySamplingIntervalMilliseconds = 5;
        suite.Engines.Single().Operations["Run"] = ScriptBlock.Create(
            "param($case, $run) $run.Buffer = [byte[]]::new(65536); "
            + (failure == "Operation" ? "throw 'operation failed'" : ""));
        suite.Validate = ScriptBlock.Create("param($case, $run) "
            + (failure == "Validation" ? "throw 'validation failed'" : ""));
        try
        {
            var result = new PowerShellBenchmarkRunner().Run(suite);
            var sample = Assert.Single(result.Samples);
            Assert.Equal(failure.Length == 0 ? BenchmarkSampleStatus.Succeeded : BenchmarkSampleStatus.Failed, sample.Status);
            Assert.True(sample.Metrics["MemorySampleCount"] >= 2); // Initial and operation-end observations are deterministic.
            Assert.Equal(0, sample.Metrics["MemorySamplingFailed"]);
            Assert.Equal(5, sample.Metrics["MemorySamplingIntervalMs"]);
            Assert.Equal(sample.Metrics["SampledMaxManagedHeapBytes"] - sample.Metrics["BaselineManagedHeapBytes"],
                sample.Metrics["SampledManagedHeapDeltaBytes"]);
            Assert.True(sample.Metrics["SampledManagedHeapDeltaBytes"] >= 0);
            Assert.Equal("5", result.Metadata["memorySamplingIntervalMilliseconds"]);
            Assert.NotNull(sample.AllocatedBytes);
            if (failure.Length > 0) Assert.Contains(failure, sample.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Planning_rejects_invalid_sampling_intervals(int interval)
    {
        var suite = CreateSuite("unused");
        suite.MemorySamplingIntervalMilliseconds = interval;
        Assert.Throws<ArgumentOutOfRangeException>(() => new PowerShellBenchmarkRunner().Plan(suite));
    }

    [Fact]
    public void Disabled_sampling_preserves_existing_custom_metric_names()
    {
        string root = Path.Combine(Path.GetTempPath(), "pf-memory-disabled-" + Guid.NewGuid().ToString("N"));
        var suite = CreateSuite(root);
        suite.Metrics.Add(new PowerShellBenchmarkMetric
        {
            Name = "BaselineManagedHeapBytes", ScriptBlock = ScriptBlock.Create("param($case, $run) 123")
        });
        try
        {
            var result = new PowerShellBenchmarkRunner().Run(suite);
            var sample = Assert.Single(result.Samples);
            Assert.Equal(123, sample.Metrics["BaselineManagedHeapBytes"]);
            Assert.DoesNotContain("MemorySampleCount", sample.Metrics.Keys);
            suite.MemorySamplingIntervalMilliseconds = 5;
            Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sampling_rejects_ambiguous_case_or_axis_columns(bool axis)
    {
        var suite = CreateSuite("unused");
        if (axis)
            suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "MemorySampleCount", Values = { 1 } });
        else
            suite.Cases.Add(new PowerShellBenchmarkCase { Name = "case", Values = { ["MemorySampleCount"] = 1 } });
        Assert.NotEmpty(new PowerShellBenchmarkRunner().Plan(suite));
        suite.MemorySamplingIntervalMilliseconds = 5;
        Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));
    }

    private static PowerShellBenchmarkSuite CreateSuite(string root)
    {
        var suite = new PowerShellBenchmarkSuite { Name = "memory-sampling", OutputRoot = root, WarmupCount = 0, IterationCount = 1 };
        var engine = new PowerShellBenchmarkEngine { Name = "Managed" };
        engine.Operations["Run"] = ScriptBlock.Create("param($case, $run)");
        suite.Engines.Add(engine);
        return suite;
    }
}
