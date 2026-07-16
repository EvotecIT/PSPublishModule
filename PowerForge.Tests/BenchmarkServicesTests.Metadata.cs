using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void BenchmarkEnvironmentMetadata_RecordsThePowerShellHostVersion()
    {
        var metadata = PowerShellBenchmarkEnvironmentMetadata.Build(new PowerShellBenchmarkSuite { Name = "host" });
        var host = PowerShellBenchmarkHostRuntime.GetCurrentHostLabel();

        Assert.EndsWith(metadata["pwsh"], host, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(metadata["psEdition"], host, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BenchmarkDsl_WritesDeclaredProvenanceToMetadataArtifact()
    {
        var root = CreateTempRoot();
        var escapedRoot = root.Replace("'", "''");
        var script = System.Management.Automation.ScriptBlock.Create($$"""
benchmark 'metadata' -out '{{escapedRoot}}' {
    policy -Warmup 0 -Iterations 1
    metadata ToolVersion '1.2.3-beta1'
    axis Operation Run
    engine Managed { operation Run { param($case, $run) } }
}
""");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script));
        var result = new PowerShellBenchmarkRunner().Run(suite);
        var artifact = BenchmarkJson.Read<Dictionary<string, string>>(result.Artifacts["metadata.json"]);

        Assert.Equal("1.2.3-beta1", suite.Metadata["ToolVersion"]);
        Assert.Equal("1.2.3-beta1", result.Metadata["benchmark.ToolVersion"]);
        Assert.Equal("1.2.3-beta1", artifact["benchmark.ToolVersion"]);
    }
}
