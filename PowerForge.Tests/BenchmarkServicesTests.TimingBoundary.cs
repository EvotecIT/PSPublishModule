using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Runner_TimesOperationBodyAndRetainsFailedDurationWithoutGuardSetup(bool failOperation, bool capturedDsl)
    {
        string root = CreateTempRoot();
        var previous = Runspace.DefaultRunspace;
        using var runspace = RunspaceFactory.CreateRunspace();
        runspace.Open();
        try
        {
            Runspace.DefaultRunspace = runspace;
            runspace.SessionStateProxy.SetVariable("timingFixtureFailure", failOperation);
            runspace.SessionStateProxy.SetVariable("LASTEXITCODE", 13);
            const string operation = """
param($case, $run)
$watch = [System.Diagnostics.Stopwatch]::StartNew()
[System.Threading.Thread]::Sleep(40)
$watch.Stop()
$global:timingFixtureObservedMs = $watch.Elapsed.TotalMilliseconds
if ($timingFixtureFailure) { throw 'Observed operation failure' }
""";
            PowerShellBenchmarkSuite suite;
            if (capturedDsl)
            {
                suite = Assert.Single(PowerShellBenchmarkDslRuntime.Evaluate(ScriptBlock.Create(
                    "New-BenchmarkSuite 'timing' { Add-BenchmarkEngine Managed { Add-BenchmarkOperation Run { "
                    + operation + " } } }")));
                suite.Engines.Single().Operations["Run"].Module.SessionState.PSVariable.Set(
                    "capturedFunctions", new SlowTimingAliases());
            }
            else
            {
                suite = new PowerShellBenchmarkSuite { Name = "timing" };
                var engine = new PowerShellBenchmarkEngine { Name = "Managed" };
                engine.Operations.Add("Run", ScriptBlock.Create(operation));
                suite.Engines.Add(engine);
            }
            suite.OutputRoot = root;
            suite.WarmupCount = 0;
            suite.IterationCount = 1;
            suite.Artifacts = BenchmarkArtifactKind.None;
            runspace.SessionStateProxy.SetVariable("PowerForgeBenchmarkDslCommandAliases", new SlowTimingAliases());
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Runner_NestedCapturedHandlersCannotStopTheirCallersTimer(bool recurse)
    {
        string root = CreateTempRoot();
        var previous = Runspace.DefaultRunspace;
        using var runspace = RunspaceFactory.CreateRunspace();
        runspace.Open();
        try
        {
            Runspace.DefaultRunspace = runspace;
            var invocation = new Hashtable();
            runspace.SessionStateProxy.SetVariable("timingNestedInvocation", invocation);
            var suite = Assert.Single(PowerShellBenchmarkDslRuntime.Evaluate(ScriptBlock.Create("""
New-BenchmarkSuite nested {
    Add-BenchmarkEngine Managed {
        Add-BenchmarkOperation Outer {
            param($case, $run)
            if ($timingNestedInvocation.Active) { [System.Threading.Thread]::Sleep(40); return }
            $innerWatch = [System.Diagnostics.Stopwatch]::StartNew()
            $timingNestedInvocation.Active = $true
            try { & $timingNestedInvocation.Handler $case $run }
            finally { $timingNestedInvocation.Active = $false }
            [System.Threading.Thread]::Sleep(300)
            $innerWatch.Stop()
            $global:timingNestedObservedMs = $innerWatch.Elapsed.TotalMilliseconds
        }
        Add-BenchmarkOperation Inner { param($case, $run) [System.Threading.Thread]::Sleep(40) }
    }
}
""")));
            var operations = suite.Engines.Single().Operations;
            invocation["Handler"] = operations[recurse ? "Outer" : "Inner"];
            operations.Remove("Inner");
            suite.OutputRoot = root;
            suite.WarmupCount = 0;
            suite.IterationCount = 1;
            suite.Artifacts = BenchmarkArtifactKind.None;
            var sample = Assert.Single(new PowerShellBenchmarkRunner().Run(suite).Samples);
            double observed = Convert.ToDouble(runspace.SessionStateProxy.GetVariable("timingNestedObservedMs"));
            Assert.Equal(BenchmarkSampleStatus.Succeeded, sample.Status);
            Assert.InRange(sample.DurationMs, observed, observed + 200);
        }
        finally { Runspace.DefaultRunspace = previous; Directory.Delete(root, true); }
    }
}
