using System;

namespace PowerForge.Tests;

public class WingetManifestWriterTests
{
    [Fact]
    public void Build_ThrowsWhenNoInstallersAreProvided()
    {
        var winget = new PowerForgeReleaseWingetOptions();
        var package = new PowerForgeReleaseWingetPackage
        {
            PackageIdentifier = "Evotec.Test",
            PackageName = "Test",
            Publisher = "Evotec",
            License = "MIT",
            ShortDescription = "Test package"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            WingetManifestWriter.Build(winget, package, "1.0.0", Array.Empty<WingetManifestInstallerEntry>()));

        Assert.Contains("does not define any installers", exception.Message);
    }
}
