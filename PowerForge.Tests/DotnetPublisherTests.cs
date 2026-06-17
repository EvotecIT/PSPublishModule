using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotnetPublisherTests
{
    [Fact]
    public void BuildPublishArguments_AppendsAdditionalRestoreSources()
    {
        var sourceA = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "Feed A");
        var sourceB = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "Feed B");

        var args = DotnetPublisher.BuildPublishArguments(
            configuration: "Release",
            version: "1.2.3",
            tfm: "net10.0",
            useIsolatedArtifacts: true,
            artifacts: Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "artifacts"),
            maxCpuCountArgument: "-m:1",
            publishDir: Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "publish"),
            restoreSources: new[] { sourceA, sourceB, sourceA });

        Assert.Contains(args, arg => string.Equals(
            arg,
            $"-p:RestoreAdditionalProjectSources={sourceA};{sourceB}",
            StringComparison.Ordinal));
        Assert.Single(args, arg => arg.StartsWith("-p:RestoreAdditionalProjectSources=", StringComparison.Ordinal));
    }
}
