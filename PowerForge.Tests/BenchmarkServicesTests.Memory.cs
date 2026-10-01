using System.Management.Automation;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class PowerShellBenchmarkMemoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Operation")]
    [InlineData("Validation")]
    public void Runner_RecordsOperationMemoryEvenWhenTheOperationOrValidationFails(string failure)
    {
        var suite = CreateMemorySuite();
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        suite.Setup = ScriptBlock.Create("param($case, $run) $run.SetupBuffer = [byte[]]::new(32MB)");
        suite.Engines.Single().Operations["Run"] = ScriptBlock.Create(
            "param($case, $run) $run.Buffer = [byte[]]::new(1MB); "
            + (failure == "Operation" ? "throw 'operation failed'" : ""));
        suite.Validate = ScriptBlock.Create(
            "param($case, $run) $run.ValidationBuffer = [byte[]]::new(32MB); "
            + (failure == "Validation" ? "throw 'validation failed'" : ""));

        var result = new PowerForge.PowerShellBenchmarkRunner().Run(suite);
        var sample = Assert.Single(result.Samples);
        Assert.Equal(failure.Length == 0 ? PowerForge.BenchmarkSampleStatus.Succeeded
            : PowerForge.BenchmarkSampleStatus.Failed, sample.Status);
        // An operation allocation counter must observe the actual array, while the
        // deliberately much larger setup/validation arrays lie outside its interval.
        Assert.InRange(sample.AllocatedBytes!.Value, 1L << 20, 16L << 20);
        Assert.NotNull(sample.WorkingSetDeltaBytes);
        if (failure.Length == 0)
            Assert.Equal((double)sample.AllocatedBytes.Value, Assert.Single(result.Summary).Metrics["AllocatedBytes"]);
        else Assert.Contains(failure, sample.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static PowerForge.PowerShellBenchmarkSuite CreateMemorySuite()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-benchmark-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var suite = new PowerForge.PowerShellBenchmarkSuite
        {
            Name = "memory", OutputRoot = root, SourceRoot = root,
            Artifacts = PowerForge.BenchmarkArtifactKind.None
        };
        var engine = new PowerForge.PowerShellBenchmarkEngine { Name = "Managed" };
        suite.Axes.Add(new PowerForge.PowerShellBenchmarkAxis { Name = "Engine", Values = { "Managed" } });
        suite.Axes.Add(new PowerForge.PowerShellBenchmarkAxis { Name = "Operation", Values = { "Run" } });
        suite.Engines.Add(engine);
        return suite;
    }
}

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void Artifacts_RoundTripNullableAndSignedMemoryCountersInRawCsv()
    {
        var root = CreateTempRoot();
        try
        {
            var success = Sample("memory", "case", "Run", "Managed", 1);
            success.AllocatedBytes = 9007199254740993;
            success.WorkingSetDeltaBytes = -128;
            success.Metrics["AllocatedBytes"] = 5; // A reserved metric must not duplicate the column.
            var failed = Sample("memory", "case", "Run", "Managed", 2);
            failed.Status = PowerForge.BenchmarkSampleStatus.Failed;
            failed.AllocatedBytes = 100;
            failed.WorkingSetDeltaBytes = 64;
            var missing = Sample("memory", "case", "Run", "Managed", 3);
            var result = new PowerForge.BenchmarkRunResult
            {
                RunId = "memory", Suite = "memory", Samples = new[] { success, failed, missing }
            };
            PowerForge.PowerShellBenchmarkArtifactWriter.WriteArtifacts(new PowerForge.PowerShellBenchmarkSuite
            {
                OutputRoot = root, Artifacts = PowerForge.BenchmarkArtifactKind.Csv
            }, result);
            var imported = new PowerForge.BenchmarkResultImporter().Import(result.Artifacts["samples.csv"]);
            Assert.Equal(3, imported.Samples.Length);
            for (int i = 0; i < result.Samples.Length; i++)
            {
                Assert.Equal(result.Samples[i].Status, imported.Samples[i].Status);
                Assert.Equal(result.Samples[i].AllocatedBytes, imported.Samples[i].AllocatedBytes);
                Assert.Equal(result.Samples[i].WorkingSetDeltaBytes, imported.Samples[i].WorkingSetDeltaBytes);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Summary_RequiresCompleteMemoryObservationsAndExcludesFailedSamples()
    {
        var first = Sample("memory", "case", "Run", "Managed", 1);
        first.AllocatedBytes = 100;
        first.WorkingSetDeltaBytes = -20;
        var second = Sample("memory", "case", "Run", "Managed", 2);
        second.AllocatedBytes = 300;
        second.WorkingSetDeltaBytes = 10;
        var failed = Sample("memory", "case", "Run", "Managed", 3);
        failed.Status = PowerForge.BenchmarkSampleStatus.Failed;
        failed.AllocatedBytes = 10000;
        var service = new PowerForge.BenchmarkSummaryService();

        var summary = Assert.Single(service.Summarize(new[] { first, second, failed }));
        Assert.Equal(200d, summary.Metrics["AllocatedBytes"]);
        Assert.Equal(-5d, summary.Metrics["WorkingSetDeltaBytes"]);
        second.AllocatedBytes = null;
        second.WorkingSetDeltaBytes = null;
        summary = Assert.Single(service.Summarize(new[] { first, second }));
        Assert.DoesNotContain("AllocatedBytes", summary.Metrics.Keys);
        Assert.DoesNotContain("WorkingSetDeltaBytes", summary.Metrics.Keys);
    }
}
