using System.Text.Json;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationSemanticUpstreamMonitorTests
{
    [Fact]
    public async Task MonitorReportsCurrentPinsWithoutMutatingTheReviewedDocument()
    {
        using var fixture = MonitorFixture.Create(CurrentRefsJson);
        var pinHash = File.ReadAllBytes(fixture.PinPath);

        var result = await RunAsync(fixture, failOnChange: false);

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(fixture.OutputPath));
        Assert.Empty(report.RootElement.GetProperty("ReviewRequests").EnumerateArray());
        Assert.All(report.RootElement.GetProperty("Profiles").EnumerateArray(),
            profile => Assert.Equal("Current", profile.GetProperty("Status").GetString()));
        Assert.Equal(pinHash, File.ReadAllBytes(fixture.PinPath));
    }

    [Fact]
    public async Task MonitorFailsClosedWithReviewProposalAndNeverAdvancesPins()
    {
        using var fixture = MonitorFixture.Create(ChangedRefsJson);
        var pinHash = File.ReadAllBytes(fixture.PinPath);

        var result = await RunAsync(fixture, failOnChange: true);

        Assert.NotEqual(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(fixture.OutputPath));
        var review = Assert.Single(report.RootElement.GetProperty("ReviewRequests").EnumerateArray());
        Assert.Equal(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, review.GetProperty("ProfileId").GetString());
        Assert.Equal("v7.6.5", review.GetProperty("PinnedTag").GetString());
        Assert.Equal("v7.6.6", review.GetProperty("ObservedTag").GetString());
        Assert.Equal(
            PowerShellCompilationSemanticOracleCaseCatalog.Cases.Count,
            review.GetProperty("AffectedCaseIds").GetArrayLength());
        Assert.Equal(pinHash, File.ReadAllBytes(fixture.PinPath));
    }

    [Fact]
    public async Task MonitorRejectsOutputAliasingTheImmutablePinDocument()
    {
        using var fixture = MonitorFixture.Create(CurrentRefsJson);
        var pinBytes = File.ReadAllBytes(fixture.PinPath);

        var result = await RunAsync(fixture, failOnChange: false, aliasOutputToPins: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(pinBytes, File.ReadAllBytes(fixture.PinPath));
    }

    [Fact]
    public void WorkflowIsReadOnlyScheduledAndRetainsTheReviewProposal()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "powerforge-powershell-semantic-upstream.yml"));
        Assert.Contains("schedule:", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: read", workflow, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerShellSemanticUpstreamMonitor.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("-FailOnChange", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull-request", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("issues: write", workflow, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProcessRunResult> RunAsync(
        MonitorFixture fixture,
        bool failOnChange,
        bool aliasOutputToPins = false)
    {
        var arguments = new List<string>
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath,
            "-PinPath", fixture.PinPath,
            "-ObservedRefsPath", fixture.ObservedRefsPath,
            "-OutputPath", aliasOutputToPins ? fixture.PinPath : fixture.OutputPath
        };
        if (failOnChange) arguments.Add("-FailOnChange");
        return await new ProcessRunner().RunAsync(new ProcessRunRequest(
            "pwsh",
            fixture.RootPath,
            arguments,
            TimeSpan.FromSeconds(30)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PSPublishModule.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Unable to locate repository root.");
    }

    private const string CurrentRefsJson = """
    [
      { "tag": "v7.4.19", "commit": "b3d5b858eba508785484768b4b3e318742416b83" },
      { "tag": "v7.6.5", "commit": "7acb29279dd64e646d821f75d1cc8ad59455a9a6" }
    ]
    """;

    private const string ChangedRefsJson = """
    [
      { "tag": "v7.4.19", "commit": "b3d5b858eba508785484768b4b3e318742416b83" },
      { "tag": "v7.6.5", "commit": "7acb29279dd64e646d821f75d1cc8ad59455a9a6" },
      { "tag": "v7.6.6", "commit": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }
    ]
    """;

    private sealed class MonitorFixture : IDisposable
    {
        private MonitorFixture(string rootPath, string scriptPath, string pinPath, string observedRefsPath, string outputPath)
        {
            RootPath = rootPath;
            ScriptPath = scriptPath;
            PinPath = pinPath;
            ObservedRefsPath = observedRefsPath;
            OutputPath = outputPath;
        }

        public string RootPath { get; }
        public string ScriptPath { get; }
        public string PinPath { get; }
        public string ObservedRefsPath { get; }
        public string OutputPath { get; }

        public static MonitorFixture Create(string observedRefs)
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeSemanticMonitorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var repositoryRoot = FindRepositoryRoot();
            var observed = Path.Combine(root, "observed.json");
            var pins = Path.Combine(root, "pins.json");
            File.WriteAllText(observed, observedRefs);
            File.Copy(
                Path.Combine(repositoryRoot, "PowerForge", "Resources", "PowerShellCompilation", "SemanticOracle", "host-artifact-pins.json"),
                pins);
            return new MonitorFixture(
                root,
                Path.Combine(repositoryRoot, "Build", "Invoke-PowerShellSemanticUpstreamMonitor.ps1"),
                pins,
                observed,
                Path.Combine(root, "report.json"));
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
