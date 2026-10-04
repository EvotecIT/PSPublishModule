using System.Collections.Concurrent;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Theory]
    [InlineData("host.C:\\PowerShell\\pwsh.exe.processAffinityMask", "0x00FF")]
    [InlineData("host.C:\\PowerShell\\pwsh.exe.processPriority", "High")]
    [InlineData("profile", "TemporaryLocalUser")]
    [InlineData("cooldownMilliseconds", "1000")]
    public void History_MergedChildPlacementAndExecutionPolicyRequireNewCalibration(string key, string value)
    {
        var service = new BenchmarkHistoryService();
        var request = HistoryRequest();
        BenchmarkHistory? history = null;
        for (int i = 0; i < 5; i++) history = service.Record(history, MergedHistoryRun(i), request);
        var current = MergedHistoryRun(6);
        Assert.True(service.Evaluate(history, current, request).Passed);
        current.Metadata[key] = value;
        var changed = service.Evaluate(history, current, request);
        Assert.True(changed.Calibrating);
        Assert.Equal(0, Assert.Single(changed.Metrics).HistoryRuns);
    }

    [Fact]
    public void History_HostMetadataOrderAndSourceCommitDoNotChangeCalibration()
    {
        var service = new BenchmarkHistoryService();
        var request = HistoryRequest();
        BenchmarkHistory? history = null;
        for (int i = 0; i < 5; i++) history = service.Record(history, MergedHistoryRun(i), request);
        var current = MergedHistoryRun(6);
        current.Metadata = current.Metadata.Reverse().ToDictionary(item => item.Key, item => item.Value);
        current.Metadata["benchmark.SourceCandidateCommit"] = "new-product-source";
        Assert.True(service.Evaluate(history, current, request).Passed);
    }

    [Fact]
    public async Task History_VerificationAndAtomicAcceptanceCanOverlapWithoutChangingVerificationInput()
    {
        string root = CreateTempRoot();
        try
        {
            string path = Path.Combine(root, "history.json");
            var service = new BenchmarkHistoryService();
            var request = HistoryRequest();
            BenchmarkHistory? history = null;
            for (int i = 0; i < 40; i++) history = service.Record(history, WideHistoryRun(i), request);
            BenchmarkJson.Write(path, history!);
            var initial = service.EvaluateFile(path, WideHistoryRun(1000), request);
            Assert.True(initial.Passed, string.Join("; ", initial.Messages));
            var failures = new ConcurrentQueue<Exception>();
            using var start = new ManualResetEventSlim();
            var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                start.Wait();
                try
                {
                    for (int i = 0; i < 100; i++)
                    {
                        var snapshot = service.EvaluateFile(path, WideHistoryRun(1000), request);
                        Assert.True(snapshot.Passed, string.Join("; ", snapshot.Messages));
                    }
                }
                catch (Exception error) { failures.Enqueue(error); }
            })).ToArray();
            var writer = Task.Run(() =>
            {
                start.Wait();
                try
                {
                    for (int i = 40; i < 70; i++) service.EvaluateFile(path, WideHistoryRun(i), request, update: true);
                }
                catch (Exception error) { failures.Enqueue(error); }
            });
            start.Set();
            await Task.WhenAll(readers.Append(writer));
            Assert.Empty(failures);
            Assert.Equal(70, BenchmarkJson.Read<BenchmarkHistory>(path).Entries.Length);
            byte[] accepted = File.ReadAllBytes(path);
            Assert.True(service.EvaluateFile(path, WideHistoryRun(1000), request).Passed);
            Assert.Equal(accepted, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }

    private static BenchmarkRunResult MergedHistoryRun(int serial)
    {
        var run = HistoryRun(serial, 100);
        run.Metadata.Remove("processAffinityMask");
        run.Metadata.Remove("processPriority");
        run.Metadata["host.C:\\PowerShell\\pwsh.exe.processAffinityMask"] = "0xFFFF";
        run.Metadata["host.C:\\PowerShell\\pwsh.exe.processPriority"] = "Normal";
        run.Metadata["profile"] = "Current";
        run.Metadata["cooldownMilliseconds"] = "0";
        return run;
    }

    private static BenchmarkRunResult WideHistoryRun(int serial)
    {
        var run = HistoryRun(serial, 100);
        run.Samples = Enumerable.Range(0, 32).SelectMany(lane => HistoryRun(serial, 100).Samples.Select(sample =>
        {
            sample.Scenario = "lane-" + lane;
            return sample;
        })).ToArray();
        return run;
    }
}
