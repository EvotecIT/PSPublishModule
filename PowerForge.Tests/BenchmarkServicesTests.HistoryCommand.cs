using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void HistoryCommand_WhatIfDoesNotReadOrWriteCalibrationFiles()
    {
        string root = CreateTempRoot();
        try
        {
            using var runspace = HistoryRunspace();
            using var ps = PowerShell.Create(runspace);
            HistoryCommand(ps, root).AddParameter("Update").AddParameter("WhatIf", true);
            Assert.Empty(ps.Invoke());
            Assert.False(ps.HadErrors);
            Assert.False(File.Exists(Path.Combine(root, "history.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void HistoryCommand_AllowCalibrationDoesNotHideARegression()
    {
        string root = CreateTempRoot();
        try
        {
            using var runspace = HistoryRunspace();
            BenchmarkJson.Write(Path.Combine(root, "run.json"), HistoryRun(6, 100));
            using (var ps = PowerShell.Create(runspace))
            {
                HistoryCommand(ps, root).AddParameter("AllowCalibration");
                var result = Assert.IsType<BenchmarkHistoryResult>(Assert.Single(ps.Invoke()).BaseObject);
                Assert.True(result.Calibrating);
                Assert.False(result.Passed);
                Assert.False(ps.HadErrors);
                Assert.False(File.Exists(Path.Combine(root, "history.json")));
            }
            var service = new BenchmarkHistoryService();
            BenchmarkJson.Write(Path.Combine(root, "history.json"), AcceptedHistory(service, HistoryRequest()));
            var slow = HistoryRun(6, 200);
            var added = HistoryRun(6, 100).Samples;
            foreach (var sample in added) sample.Scenario = "new-lane";
            slow.Samples = slow.Samples.Concat(added).ToArray();
            BenchmarkJson.Write(Path.Combine(root, "run.json"), slow);
            byte[] previous = File.ReadAllBytes(Path.Combine(root, "history.json"));
            using (var ps = PowerShell.Create(runspace))
            {
                HistoryCommand(ps, root).AddParameter("AllowCalibration");
                var error = Assert.ThrowsAny<RuntimeException>(() => ps.Invoke());
                Assert.StartsWith("BenchmarkHistoryFailed", error.ErrorRecord.FullyQualifiedErrorId);
            }
            Assert.Equal(previous, File.ReadAllBytes(Path.Combine(root, "history.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static Runspace HistoryRunspace()
    {
        var state = InitialSessionState.CreateDefault();
        state.Commands.Add(new SessionStateCmdletEntry("Test-BenchmarkHistory", typeof(PSPublishModule.TestBenchmarkHistoryCommand), null));
        var runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        return runspace;
    }

    private static PowerShell HistoryCommand(PowerShell ps, string root) => ps.AddCommand("Test-BenchmarkHistory")
        .AddParameter("ResultPath", Path.Combine(root, "run.json"))
        .AddParameter("HistoryPath", Path.Combine(root, "history.json"))
        .AddParameter("WorkloadId", "topology-v1").AddParameter("RunnerIdentity", "renderer-pool");
}
