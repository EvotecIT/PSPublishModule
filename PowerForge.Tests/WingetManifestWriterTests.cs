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

    [Fact]
    public void Build_DefaultsBlankInstallerTypeToZip()
    {
        var yaml = WingetManifestWriter.Build(
            new PowerForgeReleaseWingetOptions(),
            CreatePackage(),
            "1.0.0",
            new[]
            {
                new WingetManifestInstallerEntry
                {
                    Architecture = "x64",
                    InstallerType = " ",
                    InstallerUrl = "https://example.test/tool.zip",
                    InstallerSha256 = "ABC123"
                }
            });

        Assert.Contains("InstallerType: zip" + Environment.NewLine, yaml);
        Assert.StartsWith("# yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.1.12.0.schema.json" + Environment.NewLine, yaml);
        Assert.Contains("ManifestType: installer" + Environment.NewLine, yaml);
        Assert.DoesNotContain("Publisher:", yaml);
    }

    [Fact]
    public void Build_SeparatesInstallerVersionAndLocaleFields()
    {
        var winget = new PowerForgeReleaseWingetOptions();
        var package = CreatePackage();
        package.PackageLocale = "en-GB";
        package.Platform = new[] { "Windows.Desktop" };

        var version = WingetManifestWriter.BuildVersion(winget, package, "1.2.3");
        var locale = WingetManifestWriter.BuildDefaultLocale(winget, package, "1.2.3");
        var installer = WingetManifestWriter.Build(winget, package, "1.2.3", new[]
        {
            new WingetManifestInstallerEntry
            {
                Architecture = "x64", InstallerType = "msi",
                InstallerUrl = "https://example.test/app.msi", InstallerSha256 = "ABC123"
            }
        });

        Assert.Contains("DefaultLocale: en-GB", version);
        Assert.StartsWith("# yaml-language-server: $schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json" + Environment.NewLine, version);
        Assert.Contains("ManifestType: version", version);
        Assert.Contains("PackageLocale: en-GB", locale);
        Assert.Contains("Publisher: Evotec", locale);
        Assert.StartsWith("# yaml-language-server: $schema=https://aka.ms/winget-manifest.defaultLocale.1.12.0.schema.json" + Environment.NewLine, locale);
        Assert.Contains("ManifestType: defaultLocale", locale);
        Assert.Contains("Platform:\n- Windows.Desktop".Replace("\n", Environment.NewLine), installer);
        Assert.Contains("ManifestType: installer", installer);
        Assert.DoesNotContain("PackageLocale:", installer);
    }

    [Theory]
    [InlineData("x64", "1.2.3", false)]
    [InlineData("arm64", "1.2.3", false)]
    [InlineData("x64", "1.2.0", true)]
    [InlineData("arm64", "1.2.0", true)]
    public void Build_EmitsDisplayVersionOnlyWhenMsiVersionDiffers(
        string architecture, string msiVersion, bool expectDisplayVersion)
    {
        var yaml = WingetManifestWriter.Build(
            new PowerForgeReleaseWingetOptions(),
            CreatePackage(),
            "1.2.3",
            new[]
            {
                new WingetManifestInstallerEntry
                {
                    Architecture = architecture,
                    InstallerType = "msi",
                    InstallerUrl = $"https://example.test/app-{architecture}.msi",
                    InstallerSha256 = "ABC123",
                    Scope = "machine",
                    MsiMetadata = new DotNetPublishMsiPackageMetadata
                    {
                        ProductName = "Test",
                        Manufacturer = "Evotec",
                        ProductVersion = msiVersion,
                        ProductCode = "{BF9CE84B-12E1-4BD5-B60C-90C57716BDB2}",
                        UpgradeCode = "{C54BB967-362B-48C3-937E-CCBCDD1EA390}"
                    }
                }
            });

        Assert.Contains("PackageVersion: 1.2.3" + Environment.NewLine, yaml);
        Assert.Contains("Architecture: " + architecture + Environment.NewLine, yaml);
        Assert.Contains("AppsAndFeaturesEntries:", yaml);
        Assert.Contains("DisplayName: Test" + Environment.NewLine, yaml);
        Assert.Contains("Publisher: Evotec" + Environment.NewLine, yaml);
        Assert.Contains("ProductCode: \"{BF9CE84B-12E1-4BD5-B60C-90C57716BDB2}\"", yaml);
        Assert.Contains("UpgradeCode: \"{C54BB967-362B-48C3-937E-CCBCDD1EA390}\"", yaml);
        if (expectDisplayVersion)
            Assert.Contains("DisplayVersion: " + msiVersion + Environment.NewLine, yaml);
        else
            Assert.DoesNotContain("DisplayVersion:", yaml);
    }

    private static PowerForgeReleaseWingetPackage CreatePackage()
        => new()
        {
            PackageIdentifier = "Evotec.Test",
            PackageName = "Test",
            Publisher = "Evotec",
            License = "MIT",
            ShortDescription = "Test package"
        };
}
