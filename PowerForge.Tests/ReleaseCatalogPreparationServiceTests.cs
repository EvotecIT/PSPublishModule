using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseCatalogPreparationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "powerforge-catalog-" + Guid.NewGuid().ToString("N"));
    private readonly PowerForgeReleaseSpec _spec;
    private readonly string _manifest;
    private readonly string _checksums;
    private readonly string _output;

    public ReleaseCatalogPreparationServiceTests()
    {
        Directory.CreateDirectory(_root);
        _manifest = Path.Combine(_root, "release.json");
        _checksums = Path.Combine(_root, "SHA256SUMS.txt");
        _output = Path.Combine(_root, "catalog");
        var assets = new[] { "x64", "arm64" }.Select(architecture => new PowerForgeReleaseAssetEntry
        {
            Path = @"C:\old-builder\output\app-" + architecture + ".msi",
            RelativeStagePath = "GitHub/app-" + architecture + ".msi",
            Category = PowerForgeReleaseAssetCategory.Installer, Target = "App.Windows",
            Runtime = "win-" + architecture, Version = "1.2.3"
        }).ToArray();
        foreach (var architecture in new[] { "x64", "arm64" })
            File.WriteAllText(Path.Combine(_root, "app-" + architecture + ".msi"), "fixture " + architecture);
        File.WriteAllText(_manifest, "{\"schemaVersion\":1,\"assetEntries\":" +
            JsonSerializer.Serialize(assets, ReleaseCatalogJsonContext.Default.PowerForgeReleaseAssetEntryArray) + "}");
        File.WriteAllLines(_checksums, Directory.GetFiles(_root).Select(path => Hash(path) + " *" + Path.GetFileName(path)));
        _spec = new PowerForgeReleaseSpec
        {
            Winget = new PowerForgeReleaseWingetOptions
            {
                Enabled = true, InstallerUrlTemplate = "https://example.test/releases/v{PackageVersion}/{FileName}",
                Packages = new[] { new PowerForgeReleaseWingetPackage
                {
                    PackageIdentifier = "Fixture.App", Publisher = "Fixture", PackageName = "Fixture App",
                    License = "MIT", ShortDescription = "Local document workspace",
                    Installers = new[] { "x64", "arm64" }.Select(architecture => new PowerForgeReleaseWingetInstaller
                    {
                        Category = PowerForgeReleaseAssetCategory.Installer, Target = "App.Windows",
                        Runtime = "win-" + architecture, InstallerType = "msi", NestedInstallerType = null
                    }).ToArray()
                } }
            }
        };
    }

    [Fact]
    public void Prepare_UsesDownloadedBytesAndProducesBothCatalogsWithMsiIdentity()
    {
        var result = Prepare();
        Assert.Equal(3, result.WingetManifestPaths.Length);
        var yaml = File.ReadAllText(result.WingetManifestPaths[0]);
        Assert.Contains(Hash(Path.Combine(_root, "app-x64.msi")).ToLowerInvariant(), yaml);
        Assert.Contains("https://example.test/releases/v1.2.3/app-arm64.msi", yaml);
        Assert.Contains("Scope: machine", yaml);
        Assert.Contains("AppsAndFeaturesEntries:", yaml);
        Assert.Contains("UpgradeCode:", yaml);
        Assert.DoesNotContain("old-builder", yaml);
        var store = new StoreSubmissionSpec
        {
            Authentication = new StoreSubmissionAuthenticationOptions { SellerId = "123" },
            Targets = new[] { new StoreSubmissionTarget
            {
                Name = "Fixture", Provider = StoreSubmissionProviderKind.DesktopInstaller,
                ApplicationId = "fixture-product", DesktopPackagesPath = "catalog/desktop-packages.json"
            } }
        };
        using var submission = new StoreSubmissionService();
        var plan = submission.Plan(store, Path.Combine(_root, "store.json"));
        Assert.Equal(2, plan.DesktopPackages.Length);
        Assert.All(plan.DesktopPackages, package =>
        {
            Assert.Equal("msi", package.PackageType);
            Assert.Equal("/qn /norestart", package.InstallerParameters);
            Assert.False(package.IsSilentInstall);
        });
        Assert.Equal(new[] { "X64", "Arm64" }, plan.DesktopPackages.SelectMany(package => package.Architectures));
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("manifest")]
    [InlineData("signature")]
    [InlineData("identity")]
    [InlineData("duplicate")]
    [InlineData("url")]
    [InlineData("version")]
    [InlineData("architecture")]
    public void Prepare_RejectsUnqualifiedInputsBeforeWritingCatalogs(string failure)
    {
        if (failure == "bytes") File.AppendAllText(Path.Combine(_root, "app-x64.msi"), "changed");
        if (failure == "manifest") File.AppendAllText(_manifest, " ");
        if (failure == "duplicate") File.AppendAllText(_checksums, Environment.NewLine + Hash(_manifest) + " *release.json");
        if (failure == "url") _spec.Winget!.InstallerUrlTemplate = "http://example.test/{FileName}";
        if (failure == "version") _spec.Winget!.Packages[0].PackageVersion = "1.2.4";
        Assert.Throws<InvalidOperationException>(() => Prepare(failure));
        Assert.False(Directory.Exists(_output));
    }

    [Fact]
    public void StorePlan_RejectsAmbiguousInlineAndExternalPackages()
    {
        Prepare();
        var spec = new StoreSubmissionSpec
        {
            Authentication = new StoreSubmissionAuthenticationOptions { SellerId = "123" },
            Targets = new[] { new StoreSubmissionTarget
            {
                Name = "Fixture", Provider = StoreSubmissionProviderKind.DesktopInstaller, ApplicationId = "fixture",
                DesktopPackagesPath = "catalog/desktop-packages.json",
                DesktopPackages = new[] { new StoreSubmissionDesktopPackage() }
            } }
        };
        using var service = new StoreSubmissionService();
        Assert.Throws<InvalidOperationException>(() => service.Plan(spec, Path.Combine(_root, "store.json")));
        Assert.Equal(spec.Targets[0].DesktopPackagesPath, StoreSubmissionSpecSanitizer.RedactSecrets(spec).Targets[0].DesktopPackagesPath);
    }

    private ReleaseCatalogPreparationResult Prepare(string? failure = null)
    {
        var service = new ReleaseCatalogPreparationService(path => new DotNetPublishMsiPackageMetadata
        {
            Path = path, ProductName = failure == "identity" ? "Other App" : "Fixture App", Manufacturer = "Fixture",
            ProductVersion = "1.2.3", Scope = "machine",
            Architecture = failure == "architecture" ? "x86" : path.EndsWith("x64.msi", StringComparison.Ordinal) ? "x64" : "arm64",
            ProductCode = path.EndsWith("x64.msi", StringComparison.Ordinal) ? "{11111111-1111-1111-1111-111111111111}" : "{22222222-2222-2222-2222-222222222222}",
            UpgradeCode = "{33333333-3333-3333-3333-333333333333}"
        }, _ => failure != "signature");
        return service.Prepare(_spec, _manifest, _checksums, _root, _output);
    }

    private static string Hash(string path)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
