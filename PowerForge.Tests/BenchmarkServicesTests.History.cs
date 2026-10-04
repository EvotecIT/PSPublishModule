using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void History_CalibratesIndependentRunsAndReportsMeasuredNoise()
    {
        var service = new BenchmarkHistoryService();
        var request = HistoryRequest();
        BenchmarkHistory? history = null;
        double[] values = { 98, 99, 100, 102, 104 };
        for (int i = 0; i < values.Length; i++) history = service.Record(history, HistoryRun(i, values[i]), request);
        request.RelativeTolerance = 0.01;
        var passing = service.Evaluate(history, HistoryRun(6, 105), request);
        var lane = Assert.Single(passing.Metrics);
        Assert.True(passing.Passed);
        Assert.Equal(100, lane.BaselineMs);
        Assert.Equal(2, lane.NoiseMs);
        Assert.Equal(106, lane.AllowedMs);
        Assert.Equal(5, lane.HistoryRuns);
        var failing = service.Evaluate(history, HistoryRun(6, 107), request);
        Assert.False(failing.Passed);
        Assert.True(Assert.Single(failing.Metrics).Regressed);
        Assert.Equal(5, history!.Entries.Length);
    }

    [Fact]
    public void History_InsufficientHistoryAndSelfOrFutureRunsNeverPass()
    {
        var service = new BenchmarkHistoryService();
        var request = HistoryRequest();
        var current = HistoryRun(1, 100);
        var history = service.Record(null, current, request);
        history = service.Record(history, HistoryRun(2, 100), request);
        var result = service.Evaluate(history, current, request);
        Assert.False(result.Passed);
        Assert.True(result.Calibrating);
        Assert.Equal(0, Assert.Single(result.Metrics).HistoryRuns);
    }

    [Theory]
    [InlineData("OS")]
    [InlineData("CPU")]
    [InlineData("Architecture")]
    [InlineData("Runtime")]
    [InlineData("SDK")]
    [InlineData("Affinity")]
    [InlineData("Power")]
    [InlineData("Runner")]
    [InlineData("Workload")]
    public void History_DifferentRunnerOrWorkloadCannotReuseThresholds(string dimension)
    {
        var service = new BenchmarkHistoryService();
        var request = HistoryRequest();
        var history = AcceptedHistory(service, request);
        var current = HistoryRun(6, 100);
        switch (dimension)
        {
            case "OS": current.Environment.OsFamily = "Linux"; break;
            case "CPU": current.Environment.ProcessorName = "Other CPU"; break;
            case "Architecture": current.Environment.ProcessArchitecture = "Arm64"; break;
            case "Runtime": current.Environment.RuntimeVersion = ".NET 11"; break;
            case "SDK": current.Environment.DotNetSdkVersion = "10.0.304"; break;
            case "Affinity": current.Metadata["processAffinityMask"] = "0x00FF"; break;
            case "Power": current.Metadata["benchmark.PowerPlan"] = "Balanced"; break;
            case "Runner": request.RunnerIdentity = "other-pool"; break;
            case "Workload": request.WorkloadId = "other-fixture"; break;
        }
        var result = service.Evaluate(history, current, request);
        Assert.True(result.Calibrating);
        Assert.False(result.Passed);
        Assert.Equal(0, Assert.Single(result.Metrics).HistoryRuns);
    }

    [Fact]
    public void History_RecomputesSummaryAndRequiresIndependentRawIterations()
    {
        var service = new BenchmarkHistoryService();
        var run = HistoryRun(0, 100);
        run.Summary = new[] { new BenchmarkSummaryRow { MedianMs = 1, Status = "Succeeded", SampleCount = 999 } };
        foreach (var sample in run.Samples) sample.Metrics["MedianMs"] = 1;
        var entry = Assert.Single(service.Record(null, run, HistoryRequest()).Entries);
        Assert.Equal(100, Assert.Single(entry.Lanes).Value.MedianMs);
        run.Samples[1].Iteration = run.Samples[0].Iteration;
        Assert.Throws<InvalidOperationException>(() => service.Record(null, run, HistoryRequest()));
    }

    [Fact]
    public void History_ReplayIsIdempotentButChangedContentCannotReuseAnId()
    {
        var service = new BenchmarkHistoryService();
        var request = HistoryRequest();
        var run = HistoryRun(0, 100);
        var history = service.Record(null, run, request);
        Assert.Single(service.Record(history, run, request).Entries);
        run.Samples[0].DurationMs = 150;
        Assert.Throws<InvalidOperationException>(() => service.Record(history, run, request));
    }

    [Fact]
    public void History_MissingLaneFailsEvenDuringNewLaneCalibration()
    {
        var service = new BenchmarkHistoryService();
        var request = HistoryRequest();
        var history = AcceptedHistory(service, request);
        var current = HistoryRun(6, 100);
        foreach (var sample in current.Samples) sample.Scenario = "new-lane";
        var result = service.Evaluate(history, current, request);
        Assert.False(result.Passed);
        Assert.True(result.Calibrating);
        Assert.Contains(result.Metrics, metric => metric.Regressed && metric.ActualMs is null);
    }

    [Fact]
    public void History_IncompleteMeasuredLaneCannotBeAccepted()
    {
        var service = new BenchmarkHistoryService();
        var request = HistoryRequest();
        var current = HistoryRun(6, 100, 9);
        current.Samples = current.Samples.Take(8).ToArray();
        Assert.Throws<InvalidOperationException>(() => service.Record(null, current, request));
    }

    [Theory]
    [InlineData(BenchmarkSampleStatus.Failed)]
    [InlineData(BenchmarkSampleStatus.Skipped)]
    public void History_FailedUpdatePreservesAcceptedFile(BenchmarkSampleStatus status)
    {
        string root = CreateTempRoot();
        try
        {
            string path = Path.Combine(root, "history.json");
            var service = new BenchmarkHistoryService();
            var request = HistoryRequest();
            var update = service.EvaluateFile(path, HistoryRun(0, 100), request, update: true);
            Assert.True(update.Updated);
            Assert.False(update.Passed);
            byte[] previous = File.ReadAllBytes(path);
            var run = HistoryRun(1, 100); run.Samples[0].Status = status;
            Assert.Throws<InvalidOperationException>(() => service.EvaluateFile(path, run, request, update: true));
            Assert.Equal(previous, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void History_ConcurrentAcceptedUpdatesRetainAllIndependentRuns()
    {
        string root = CreateTempRoot();
        try
        {
            string path = Path.Combine(root, "history.json");
            Parallel.For(0, 10, i => new BenchmarkHistoryService().EvaluateFile(path, HistoryRun(i, 100), HistoryRequest(), update: true));
            var history = BenchmarkJson.Read<BenchmarkHistory>(path);
            Assert.Equal(10, history.Entries.Length);
            Assert.Equal(10, history.Entries.Select(entry => entry.RunId).Distinct().Count());
        }
        finally { Directory.Delete(root, true); }
    }

    private static BenchmarkHistory AcceptedHistory(BenchmarkHistoryService service, BenchmarkHistoryRequest request)
    {
        BenchmarkHistory? history = null;
        for (int i = 0; i < 5; i++) history = service.Record(history, HistoryRun(i, 100), request);
        return history!;
    }

    private static BenchmarkHistoryRequest HistoryRequest() => new() { WorkloadId = "topology-v1", RunnerIdentity = "renderer-pool" };

    private static BenchmarkRunResult HistoryRun(int serial, double median, int count = 5)
    {
        string id = "history-" + serial;
        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(serial);
        return new BenchmarkRunResult
        {
            RunId = id, Suite = "history", StartedUtc = timestamp, FinishedUtc = timestamp.AddSeconds(1),
            Samples = Enumerable.Range(0, count).Select(i => new BenchmarkSample
            {
                RunId = id, Suite = "history", Scenario = "prepare", Operation = "Execute", Engine = "Managed",
                Host = "Core", Os = "Windows", RunMode = "standard", Iteration = i,
                Status = BenchmarkSampleStatus.Succeeded, DurationMs = median
            }).ToArray(),
            Environment = new BenchmarkEnvironmentInfo { OsFamily = "Windows", OsArchitecture = "X64", ProcessArchitecture = "X64",
                ProcessorName = "Fixture CPU", RuntimeVersion = ".NET 10", Runner = "PowerForge 3", LogicalCoreCount = 16 },
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["warmupCount"] = "2", ["iterationCount"] = count.ToString(), ["runOrder"] = "Rotated", ["outlierMode"] = "None",
                ["memoryCleanup"] = "BeforeIteration", ["processAffinityMask"] = "0xFFFF", ["processPriority"] = "Normal",
                ["benchmark.PowerPlan"] = "High performance"
            }
        };
    }
}
