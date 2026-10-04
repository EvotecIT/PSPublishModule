using PowerForge;

namespace PowerForge.Net472SmokeTests;

public sealed class BenchmarkHistoryNet472SmokeTests
{
    [Fact]
    public void History_UpdatesAndVerifiesAnAtomicFileUnderNet472()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.History.Smoke." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "history.json");
            var service = new BenchmarkHistoryService();
            var request = new BenchmarkHistoryRequest { WorkloadId = "smoke-v1", RunnerIdentity = "desktop", MinimumRuns = 3 };
            for (int i = 0; i < 3; i++) Assert.True(service.EvaluateFile(path, Run(i), request, update: true).Updated);
            byte[] accepted = File.ReadAllBytes(path);
            var result = service.EvaluateFile(path, Run(3), request);
            Assert.True(result.Passed);
            Assert.Equal(3, Assert.Single(result.Metrics).HistoryRuns);
            Assert.Equal(accepted, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }

    private static BenchmarkRunResult Run(int number)
    {
        string id = "desktop-" + number;
        var time = DateTimeOffset.UtcNow.AddMinutes(number);
        return new BenchmarkRunResult
        {
            RunId = id, Suite = "smoke", StartedUtc = time, FinishedUtc = time.AddSeconds(1),
            Environment = new BenchmarkEnvironmentInfo { OsFamily = "Windows", OsArchitecture = "X64", ProcessArchitecture = "X64",
                ProcessorName = "Smoke CPU", RuntimeVersion = ".NET Framework 4.8", Runner = "PowerForge" },
            Metadata = new Dictionary<string, string> { ["iterationCount"] = "5", ["outlierMode"] = "None" },
            Samples = Enumerable.Range(0, 5).Select(i => new BenchmarkSample
            {
                RunId = id, Suite = "smoke", Scenario = "single", Operation = "Execute", Engine = "Managed", Host = "Desktop",
                Iteration = i, Status = BenchmarkSampleStatus.Succeeded, DurationMs = 100
            }).ToArray()
        };
    }
}
