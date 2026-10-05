using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Runner_TimesOperationBodyAndRetainsFailedDurationWithoutGuardSetup(bool failOperation)
    {
        string root = CreateTempRoot();
        var previous = Runspace.DefaultRunspace;
        using var runspace = RunspaceFactory.CreateRunspace();
        runspace.Open();
        try
        {
            Runspace.DefaultRunspace = runspace;
            runspace.SessionStateProxy.SetVariable("PowerForgeBenchmarkDslCommandAliases", new SlowTimingAliases());
            runspace.SessionStateProxy.SetVariable("timingFixtureFailure", failOperation);
            runspace.SessionStateProxy.SetVariable("LASTEXITCODE", 13);
            var suite = new PowerShellBenchmarkSuite { Name = "timing", OutputRoot = root, WarmupCount = 0,
                IterationCount = 1, Artifacts = BenchmarkArtifactKind.None };
            var engine = new PowerShellBenchmarkEngine { Name = "Managed" };
            engine.Operations.Add("Run", ScriptBlock.Create("""
param($case, $run)
$watch = [System.Diagnostics.Stopwatch]::StartNew()
[System.Threading.Thread]::Sleep(40)
$watch.Stop()
$global:timingFixtureObservedMs = $watch.Elapsed.TotalMilliseconds
if ($timingFixtureFailure) { throw 'Observed operation failure' }
"""));
            suite.Engines.Add(engine);
            var result = new PowerShellBenchmarkRunner().Run(suite);
            var sample = Assert.Single(result.Samples);
            double innerMs = Convert.ToDouble(runspace.SessionStateProxy.GetVariable("timingFixtureObservedMs"));
            // Deliberately slow wrapper setup creates a large separation, rather than
            // relying on ordinary microsecond differences or a product speed threshold.
            Assert.InRange(sample.DurationMs, innerMs, innerMs + 300);
            Assert.Equal(failOperation ? BenchmarkSampleStatus.Failed : BenchmarkSampleStatus.Succeeded, sample.Status);
            if (failOperation) Assert.Contains("Observed operation failure", sample.Reason);
            Assert.Equal("GuardedScriptBodyV1", result.Metadata["operationTimingBoundary"]);
            Assert.Equal(13, runspace.SessionStateProxy.GetVariable("LASTEXITCODE"));
            Assert.Null(runspace.SessionStateProxy.InvokeCommand.GetCommand("timing-unused-alias", CommandTypes.Alias));
        }
        finally { Runspace.DefaultRunspace = previous; Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("operationTimingBoundary", "GuardedScriptBodyV1")]
    [InlineData("memorySamplingIntervalMilliseconds", "10")]
    public void History_ChangedMeasurementBoundaryOrEnabledSamplingRequiresItsOwnReferences(string key, string value)
    {
        var service = new BenchmarkHistoryService();
        var request = HistoryRequest();
        var history = AcceptedHistory(service, request);
        var current = HistoryRun(6, 100);
        current.Metadata[key] = value;
        var result = service.Evaluate(history, current, request);
        Assert.False(result.Passed);
        Assert.True(result.Calibrating);
        Assert.All(result.Metrics, metric => Assert.Equal(0, metric.HistoryRuns));
    }

    [Fact]
    public void History_AbsentAndDisabledMemorySamplingPreserveLegacyReferences()
    {
        var service = new BenchmarkHistoryService();
        var request = HistoryRequest();
        var history = AcceptedHistory(service, request);
        var current = HistoryRun(6, 100);
        current.Metadata["memorySamplingIntervalMilliseconds"] = "0";
        Assert.True(service.Evaluate(history, current, request).Passed);
    }

    private sealed class SlowTimingAliases
    {
        public IEnumerator GetEnumerator()
        {
            System.Threading.Thread.Sleep(600);
            return new Dictionary<string, string> { ["timing-unused-alias"] = "Write-Output" }.GetEnumerator();
        }
    }
}
