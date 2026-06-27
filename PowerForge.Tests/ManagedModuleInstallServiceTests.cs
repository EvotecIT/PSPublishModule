using System.IO.Compression;
using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleInstallServiceTests
{
    [Fact]
    public async Task InstallAsync_installs_latest_stable_package_to_versioned_module_path()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0-beta1.nupkg"),
            "Company.Tools",
            "1.1.0-beta1",
            files: CreateModuleFiles("1.1.0-beta1"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("1.1.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.Equal("1.1.0", result.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Public", "Get-CompanyTool.ps1")));
        Assert.False(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.nuspec")));
        Assert.Equal(2, result.FileCount);
        Assert.True(result.ExtractedBytes > 0);
        Assert.False(string.IsNullOrWhiteSpace(result.ReceiptPath));
        Assert.True(File.Exists(result.ReceiptPath));
        Assert.NotNull(result.Receipt);
        Assert.Equal("Install", result.Receipt.Operation);
        Assert.Equal("Company.Tools", result.Receipt.Name);
        Assert.Equal("1.1.0", result.Receipt.Version);
        Assert.Equal(64, result.Download?.PackageSha256.Length);
        Assert.Equal(result.Download?.PackageSha256, result.Receipt.PackageSha256);
    }

    [Fact]
    public async Task InstallAsync_skips_existing_version_without_force()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var existingPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(existingPath);
        File.WriteAllText(Path.Combine(existingPath, "marker.txt"), "keep");
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal(ManagedModuleInstallStatus.AlreadyInstalled, result.Status);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(existingPath, "marker.txt")));
        Assert.Null(result.Download);
        Assert.Null(result.Receipt);
        Assert.Null(result.ReceiptPath);
    }

    [Fact]
    public async Task InstallAsync_selects_highest_version_within_bounds()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.5.0.nupkg"),
            "Company.Tools",
            "1.5.0",
            files: CreateModuleFiles("1.5.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.2.0.0.nupkg"),
            "Company.Tools",
            "2.0.0",
            files: CreateModuleFiles("2.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            MinimumVersion = "1.1.0",
            MaximumVersion = "1.9.9",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal("1.5.0", result.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.5.0", "Company.Tools.psd1")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "2.0.0")));
    }

    [Fact]
    public async Task InstallAsync_selects_highest_version_within_version_policy()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.5.0.nupkg"),
            "Company.Tools",
            "1.5.0",
            files: CreateModuleFiles("1.5.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.2.0.0.nupkg"),
            "Company.Tools",
            "2.0.0",
            files: CreateModuleFiles("2.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            VersionPolicy = "(1.0.0,2.0.0)",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal("1.5.0", result.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.5.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_rejects_exact_version_with_bounds()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var service = new ManagedModuleInstallService(new NullLogger());

        await Assert.ThrowsAsync<ArgumentException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            MinimumVersion = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        }));
    }

    [Fact]
    public async Task InstallAsync_rejects_version_policy_with_bounds()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var service = new ManagedModuleInstallService(new NullLogger());

        await Assert.ThrowsAsync<ArgumentException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            VersionPolicy = "[1.0.0,2.0.0)",
            MinimumVersion = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        }));
    }

    [Fact]
    public async Task InstallAsync_force_replaces_existing_version()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var existingPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(existingPath);
        File.WriteAllText(Path.Combine(existingPath, "marker.txt"), "replace");
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            Force = true
        });

        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.False(File.Exists(Path.Combine(existingPath, "marker.txt")));
        Assert.True(File.Exists(Path.Combine(existingPath, "Company.Tools.psd1")));
        AssertReceipt(result.ReceiptPath, "Install", "Company.Tools", "1.0.0", previousVersion: null);
    }

    [Fact]
    public async Task InstallAsync_installs_dependencies_before_parent()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateDependencyFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.1.0.nupkg"),
            "Company.Core",
            "1.1.0",
            files: CreateDependencyFiles("1.1.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0,2.0.0)", null) },
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        var dependency = Assert.Single(result.DependencyResults);
        Assert.Equal("Company.Core", dependency.Name);
        Assert.Equal("1.1.0", dependency.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.1.0", "Company.Core.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_skip_dependency_check_installs_only_parent()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateDependencyFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            SkipDependencyCheck = true
        });

        Assert.Empty(result.DependencyResults);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Core")));
    }

    [Fact]
    public async Task InstallAsync_failed_force_extraction_keeps_existing_version_without_receipt()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateDuplicateEntryPackage(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"), "Company.Tools", "1.0.0");
        var existingPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(existingPath);
        File.WriteAllText(Path.Combine(existingPath, "marker.txt"), "keep");
        var service = new ManagedModuleInstallService(new NullLogger());

        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            Force = true
        }));

        Assert.Equal("keep", File.ReadAllText(Path.Combine(existingPath, "marker.txt")));
        Assert.False(File.Exists(Path.Combine(existingPath, ".powerforge", "managed-module-receipt.json")));
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }",
            ["Public/Get-CompanyTool.ps1"] = "function Get-CompanyTool { 'ok' }"
        };

    private static IReadOnlyDictionary<string, string> CreateDependencyFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Core.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static void AssertReceipt(
        string? receiptPath,
        string operation,
        string name,
        string version,
        string? previousVersion)
    {
        Assert.False(string.IsNullOrWhiteSpace(receiptPath));
        var receipt = JsonSerializer.Deserialize<ManagedModuleReceipt>(File.ReadAllText(receiptPath!));
        Assert.NotNull(receipt);
        Assert.Equal(operation, receipt.Operation);
        Assert.Equal(name, receipt.Name);
        Assert.Equal(version, receipt.Version);
        Assert.Equal(previousVersion, receipt.PreviousVersion);
        Assert.Equal(64, receipt.PackageSha256.Length);
        Assert.True(receipt.FileCount > 0);
        Assert.True(receipt.ExtractedBytes > 0);
    }

    private static void CreateDuplicateEntryPackage(string packagePath, string id, string version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var nuspec = archive.CreateEntry(id + ".nuspec");
        using (var writer = new StreamWriter(nuspec.Open()))
        {
            writer.Write(TestPackageFactory.CreateNuspec(id, version));
        }

        var firstEntry = archive.CreateEntry("Company.Tools.psd1");
        using (var entryWriter = new StreamWriter(firstEntry.Open()))
        {
            entryWriter.Write("@{ ModuleVersion = '" + version + "' }");
        }

        var duplicateEntry = archive.CreateEntry("Company.Tools.psd1");
        using var duplicateWriter = new StreamWriter(duplicateEntry.Open());
        duplicateWriter.Write("@{ ModuleVersion = '" + version + "' }");
    }
}
