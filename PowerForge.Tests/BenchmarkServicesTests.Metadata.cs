using PowerForge;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Theory]
    [InlineData(" 10.0.303\r\n", "10.0.303")]
    [InlineData("10.0.100-preview.7.25380.108", "10.0.100-preview.7.25380.108")]
    [InlineData(null, "")]
    [InlineData("The command could not be loaded.", "")]
    [InlineData("10.0.303\n8.0.100", "")]
    public void BenchmarkEnvironmentMetadata_OnlyAcceptsAnSdkVersion(string? output, string expected)
        => Assert.Equal(expected, PowerShellBenchmarkEnvironmentMetadata.NormalizeDotNetSdkVersion(output));

    [Fact]
    public void BenchmarkEnvironmentMetadata_RespectsSourceSdkSelectionWithoutRequiringAnSdk()
    {
        string root = CreateTempRoot();
        try
        {
            var current = PowerShellBenchmarkEnvironmentMetadata.BuildEnvironment(RepoRootLocator.Find());
            Assert.False(string.IsNullOrWhiteSpace(current.DotNetSdkVersion));
            File.WriteAllText(Path.Combine(root, "global.json"),
                """{"sdk":{"version":"99.0.999","rollForward":"disable"}}""");
            Assert.Equal(string.Empty, PowerShellBenchmarkEnvironmentMetadata.BuildEnvironment(root).DotNetSdkVersion);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BenchmarkEnvironmentMetadata_RecordsThePowerShellHostVersion()
    {
        var metadata = PowerShellBenchmarkEnvironmentMetadata.Build(new PowerShellBenchmarkSuite { Name = "host" });
        var host = PowerShellBenchmarkHostRuntime.GetCurrentHostLabel();

        Assert.Equal($"{metadata["psEdition"]}-{metadata["pwsh"]}", host, ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(PowerShellBenchmarkEnvironmentMetadata.BuildEnvironment().ProcessorName));
    }

    [Fact]
    public void BenchmarkEnvironmentMetadata_EnforcesChildProcessTimeoutBeforeReadingToEnd()
    {
        string fileName;
        string arguments;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            arguments = "/d /s /c \"ping -n 10 127.0.0.1 > nul\"";
        }
        else
        {
            fileName = "/bin/sleep";
            arguments = "10";
        }

        var stopwatch = Stopwatch.StartNew();
        string? result = PowerShellBenchmarkEnvironmentMetadata.ReadProcessValue(
            fileName,
            arguments,
            timeoutMilliseconds: 100);
        stopwatch.Stop();

        Assert.Null(result);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Timed process returned after {stopwatch.Elapsed}.");
    }

    [Fact]
    public void BenchmarkDsl_WritesDeclaredProvenanceToMetadataArtifact()
    {
        var root = CreateTempRoot();
        var escapedRoot = root.Replace("'", "''");
        var script = System.Management.Automation.ScriptBlock.Create($$"""
New-BenchmarkSuite 'metadata' -OutputRoot '{{escapedRoot}}' {
    Set-BenchmarkPolicy -Warmup 0 -Iterations 1
    Add-BenchmarkMetadata ToolVersion '1.2.3-beta1'
    Add-BenchmarkAxis Operation Run
    Add-BenchmarkEngine Managed { Add-BenchmarkOperation Run { param($case, $run) } }
}
""");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script, RepoRootLocator.Find()));
        var result = new PowerShellBenchmarkRunner().Run(suite);
        var artifact = BenchmarkJson.Read<Dictionary<string, string>>(result.Artifacts["metadata.json"]);

        Assert.Equal("1.2.3-beta1", suite.Metadata["ToolVersion"]);
        Assert.Equal("1.2.3-beta1", result.Metadata["benchmark.ToolVersion"]);
        Assert.Equal("1.2.3-beta1", artifact["benchmark.ToolVersion"]);
        Assert.False(string.IsNullOrWhiteSpace(result.Metadata["gitSha"]));
        Assert.True(result.Metadata.ContainsKey("gitWorktreeClean"));
        Assert.False(string.IsNullOrWhiteSpace(result.Environment.RuntimeVersion));
        Assert.False(string.IsNullOrWhiteSpace(result.Environment.Runner));
        Assert.False(string.IsNullOrWhiteSpace(result.Environment.ProcessorName));
        Assert.False(string.IsNullOrWhiteSpace(result.Environment.DotNetSdkVersion));
    }
}
