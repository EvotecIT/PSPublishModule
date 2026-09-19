namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void WingetPackageVersion_RejectsMixedInstallerVersions()
    {
        var package = new PowerForgeReleaseWingetPackage { PackageIdentifier = "EvotecIT.OfficeIMO.Studio" };
        var installers = new[]
        {
            new WingetManifestInstallerEntry { Asset = new PowerForgeReleaseAssetEntry { Version = "0.1.9758" } },
            new WingetManifestInstallerEntry { Asset = new PowerForgeReleaseAssetEntry { Version = "0.1.9759" } }
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ResolveWingetPackageVersion(package, installers));

        Assert.Contains("different versions", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WingetPackageVersion_RejectsExplicitVersionDifferentFromInstaller()
    {
        var package = new PowerForgeReleaseWingetPackage
        {
            PackageIdentifier = "EvotecIT.OfficeIMO.Studio", PackageVersion = "0.1.9759"
        };
        var installers = new[]
        {
            new WingetManifestInstallerEntry { Asset = new PowerForgeReleaseAssetEntry { Version = "0.1.9758" } }
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ResolveWingetPackageVersion(package, installers));

        Assert.Contains("does not match", exception.Message, StringComparison.Ordinal);
    }
}
