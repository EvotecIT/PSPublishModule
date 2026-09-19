namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData("2026.09_build1", true)]
    [InlineData("1.2.3+preview~1", true)]
    [InlineData("../1.2.3", false)]
    [InlineData("1.2.3:bad", false)]
    [InlineData("1.2.3.", false)]
    [InlineData("CON.1", false)]
    [InlineData("COM1.Tool", false)]
    [InlineData("COM¹.Tool", false)]
    [InlineData("EvotecIT.OfficeIMO.Studio", true)]
    public void WingetManifestVersion_AcceptsOnlyPortablePathSegments(string version, bool expected)
        => Assert.Equal(expected, PowerForgeReleaseService.IsSafeWingetManifestPathSegment(version));

    [Theory]
    [InlineData("Microsoft.VCRedist.2015+.x64", true)]
    [InlineData("EvotecIT.OfficeIMO.Studio", true)]
    [InlineData("CON.App", false)]
    [InlineData("COM1.Tool", false)]
    [InlineData("COM¹.Tool", false)]
    [InlineData("Vendor.CON", false)]
    [InlineData("Vendor.COM¹", false)]
    [InlineData("Vendor.LPT²", false)]
    [InlineData("Vendor..App", false)]
    [InlineData("Vendor/App", false)]
    public void WingetPackageIdentifier_UsesWingetGrammarAndSafePathSegments(string identifier, bool expected)
        => Assert.Equal(expected, PowerForgeReleaseService.IsValidWingetPackageIdentifier(identifier));

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
