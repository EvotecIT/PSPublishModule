using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishArtifactResultTests
{
    [Fact]
    public void ToString_UsesZipPath_WhenAvailable()
    {
        var artifact = new DotNetPublishArtefactResult
        {
            Target = "TestimoX.CLI",
            Framework = "net10.0-windows",
            Runtime = "win-x64",
            Style = DotNetPublishStyle.PortableCompat,
            OutputDir = @"C:\repo\Artifacts\DotNetPublish\TestimoX.CLI",
            ZipPath = @"C:\repo\Artifacts\DotNetPublish\TestimoX.CLI.zip"
        };

        Assert.Equal(
            @"TestimoX.CLI (net10.0-windows, win-x64, PortableCompat) -> C:\repo\Artifacts\DotNetPublish\TestimoX.CLI.zip",
            artifact.ToString());
    }

    [Fact]
    public void ToString_FallsBackToOutputDirectory_WhenZipIsNotAvailable()
    {
        var artifact = new DotNetPublishArtefactResult
        {
            Target = "TestimoX.Service",
            Framework = "net10.0-windows",
            Runtime = "win-x64",
            Style = DotNetPublishStyle.PortableCompat,
            OutputDir = @"C:\repo\Artifacts\DotNetPublish\TestimoX.Service"
        };

        Assert.Equal(
            @"TestimoX.Service (net10.0-windows, win-x64, PortableCompat) -> C:\repo\Artifacts\DotNetPublish\TestimoX.Service",
            artifact.ToString());
    }
}
