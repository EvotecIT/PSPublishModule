using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleUpdateServiceTests
{
    [Fact]
    public async Task UpdateAsync_skips_when_scope_has_latest_stable_version()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var installedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(installedPath);
        File.WriteAllText(Path.Combine(installedPath, "Company.Tools.psd1"), "@{ ModuleVersion = '1.0.0' }");
        var service = new ManagedModuleUpdateService(new NullLogger());

        var result = await service.UpdateAsync(CreateRequest(feed.Path, moduleRoot.Path));

        Assert.Equal(ManagedModuleUpdateStatus.UpToDate, result.Status);
        Assert.Equal("1.0.0", result.PreviousVersion);
        Assert.True(result.Elapsed > TimeSpan.Zero);
        Assert.Null(result.InstallResult);
    }

    [Fact]
    public async Task UpdateAsync_installs_newer_stable_version()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("1.1.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var result = await service.UpdateAsync(CreateRequest(feed.Path, moduleRoot.Path));

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.Equal("1.0.0", result.PreviousVersion);
        Assert.Equal("1.1.0", result.TargetVersion);
        Assert.True(result.Elapsed > TimeSpan.Zero);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
        Assert.False(string.IsNullOrWhiteSpace(result.ReceiptPath));
        Assert.Equal(result.ReceiptPath, result.InstallResult?.ReceiptPath);
        AssertReceipt(result.ReceiptPath, "Update", "Company.Tools", "1.1.0", "1.0.0");
    }

    [Fact]
    public async Task UpdateAsync_installs_when_selected_scope_has_no_copy()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var result = await service.UpdateAsync(CreateRequest(feed.Path, moduleRoot.Path));

        Assert.Equal(ManagedModuleUpdateStatus.InstalledMissing, result.Status);
        Assert.Null(result.PreviousVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
        AssertReceipt(result.ReceiptPath, "Update", "Company.Tools", "1.0.0", previousVersion: null);
    }

    [Fact]
    public async Task UpdateAsync_can_select_prerelease_latest()
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
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.IncludePrerelease = true;
        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.Equal("1.1.0-beta1", result.TargetVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0-beta1", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task UpdateAsync_honors_requested_target_version()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("1.1.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.2.0.nupkg"),
            "Company.Tools",
            "1.2.0",
            files: CreateModuleFiles("1.2.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.Version = "1.1.0";
        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.Equal("1.1.0", result.TargetVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.2.0")));
    }

    [Fact]
    public async Task UpdateAsync_honors_minimum_and_maximum_version()
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
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.MinimumVersion = "1.1.0";
        request.MaximumVersion = "1.9.9";
        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.Equal("1.5.0", result.TargetVersion);
        Assert.Equal(feed.Path, result.RepositorySource);
        Assert.Equal("1.1.0", result.MinimumVersion);
        Assert.Equal("1.9.9", result.MaximumVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.5.0", "Company.Tools.psd1")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "2.0.0")));
    }

    [Fact]
    public async Task UpdateAsync_honors_version_policy()
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
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.VersionPolicy = "(1.0.0,2.0.0)";
        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.Equal("1.5.0", result.TargetVersion);
        Assert.Equal("(1.0.0,2.0.0)", result.VersionPolicy);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.5.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task UpdateAsync_infers_prerelease_from_version_policy()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0-preview.2.nupkg"),
            "Company.Tools",
            "1.1.0-preview.2",
            files: CreateModuleFiles("1.1.0-preview.2"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0-preview.10.nupkg"),
            "Company.Tools",
            "1.1.0-preview.10",
            files: CreateModuleFiles("1.1.0-preview.10"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.VersionPolicy = "[1.1.0-preview.1,1.1.0)";
        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.Equal("1.1.0-preview.10", result.TargetVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0-preview.10", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task UpdateAsync_installs_dependencies_for_selected_update()
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
            files: CreateModuleFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateModuleFiles("1.1.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var result = await service.UpdateAsync(CreateRequest(feed.Path, moduleRoot.Path));

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        var dependency = Assert.Single(result.InstallResult?.DependencyResults ?? Array.Empty<ManagedModuleInstallResult>());
        Assert.Equal("Company.Core", dependency.Name);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0", "Company.Core.psd1")));
    }

    private static ManagedModuleUpdateRequest CreateRequest(string feedPath, string moduleRoot)
        => new()
        {
            Repository = new ManagedModuleRepository("Local", feedPath),
            Name = "Company.Tools",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot
        };

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
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
    }
}
