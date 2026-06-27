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
    public async Task UpdateAsync_rejects_package_when_expected_sha256_does_not_match()
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
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.ExpectedPackageSha256 = new string('0', 64);

        var exception = await Assert.ThrowsAsync<ManagedModulePackageIntegrityException>(() => service.UpdateAsync(request));

        Assert.Equal("Company.Tools", exception.ModuleName);
        Assert.Equal("1.1.0", exception.Version);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0")));
    }

    [Fact]
    public async Task UpdateAsync_rejects_untrusted_repository_when_policy_requires_trust()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.Repository = new ManagedModuleRepository("Local", feed.Path, ManagedModuleRepositoryKind.Auto, trusted: false);
        request.TrustPolicy = new ManagedModuleTrustPolicy
        {
            RequireTrustedRepository = true
        };

        var exception = await Assert.ThrowsAsync<ManagedModuleTrustException>(() => service.UpdateAsync(request));

        Assert.Equal("RepositoryNotTrusted", exception.Reason);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools")));
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

    [Fact]
    public async Task UpdateAsync_blocks_when_target_module_is_loaded_and_update_would_write()
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
        var loadedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(loadedPath);
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.LoadedModules = new[]
        {
            new ManagedModuleLoadedModule
            {
                Name = "Company.Tools",
                Version = "1.0.0",
                ModuleBase = loadedPath
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(request));

        Assert.Contains("already loaded", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AllowLoadedModuleUpdate", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0")));
    }

    [Fact]
    public async Task UpdateAsync_allows_loaded_module_evidence_when_update_is_noop()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var loadedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(loadedPath);
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.LoadedModules = new[]
        {
            new ManagedModuleLoadedModule
            {
                Name = "Company.Tools",
                Version = "1.0.0",
                ModuleBase = loadedPath
            }
        };

        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.UpToDate, result.Status);
        Assert.Null(result.InstallResult);
    }

    [Fact]
    public async Task UpdateAsync_can_override_loaded_module_safety()
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
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.AllowLoadedModuleUpdate = true;
        request.LoadedModules = new[]
        {
            new ManagedModuleLoadedModule
            {
                Name = "Company.Tools",
                Version = "1.0.0"
            }
        };

        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task UpdateAsync_aligns_installed_family_members_to_selected_version()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Cloud.Users.2.0.0.nupkg"),
            "Company.Cloud.Users",
            "2.0.0",
            files: CreateModuleFiles("Company.Cloud.Users", "2.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Cloud.Groups.2.0.0.nupkg"),
            "Company.Cloud.Groups",
            "2.0.0",
            files: CreateModuleFiles("Company.Cloud.Groups", "2.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Groups", "1.5.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path, "Company.Cloud.Users");
        request.FamilyPolicy = new ManagedModuleFamilyPolicy
        {
            Name = "CompanyCloud",
            ModuleNamePrefix = "Company.Cloud."
        };

        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "2.0.0", "Company.Cloud.Users.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Cloud.Groups", "2.0.0", "Company.Cloud.Groups.psd1")));
        var familyResult = Assert.Single(result.FamilyResults);
        Assert.Equal("Company.Cloud.Groups", familyResult.Name);
        Assert.Equal(ManagedModuleFamilyUpdatePlanAction.Update, familyResult.Action);
        Assert.Equal("1.5.0", familyResult.PreviousVersion);
        Assert.Equal("2.0.0", familyResult.TargetVersion);
        Assert.NotNull(familyResult.InstallResult);
        AssertReceipt(familyResult.ReceiptPath, "Update", "Company.Cloud.Groups", "2.0.0", "1.5.0");
    }

    [Fact]
    public async Task UpdateAsync_blocks_family_update_before_writing_when_target_version_is_missing_for_member()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Cloud.Users.2.0.0.nupkg"),
            "Company.Cloud.Users",
            "2.0.0",
            files: CreateModuleFiles("Company.Cloud.Users", "2.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Groups", "1.5.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path, "Company.Cloud.Users");
        request.FamilyPolicy = new ManagedModuleFamilyPolicy
        {
            ModuleNamePrefix = "Company.Cloud."
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(request));

        Assert.Contains("family update cannot be applied", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "2.0.0")));
    }

    [Fact]
    public async Task UpdateAsync_blocks_loaded_family_member_when_family_update_would_write()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Cloud.Users.2.0.0.nupkg"),
            "Company.Cloud.Users",
            "2.0.0",
            files: CreateModuleFiles("Company.Cloud.Users", "2.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Cloud.Groups.2.0.0.nupkg"),
            "Company.Cloud.Groups",
            "2.0.0",
            files: CreateModuleFiles("Company.Cloud.Groups", "2.0.0"));
        var loadedPath = Path.Combine(moduleRoot.Path, "Company.Cloud.Groups", "1.5.0");
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "2.0.0"));
        Directory.CreateDirectory(loadedPath);
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path, "Company.Cloud.Users");
        request.FamilyPolicy = new ManagedModuleFamilyPolicy
        {
            ModuleNamePrefix = "Company.Cloud."
        };
        request.LoadedModules = new[]
        {
            new ManagedModuleLoadedModule
            {
                Name = "Company.Cloud.Groups",
                Version = "1.5.0",
                ModuleBase = loadedPath
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(request));

        Assert.Contains("already loaded", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Company.Cloud.Groups", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Cloud.Groups", "2.0.0")));
    }

    [Fact]
    public async Task UpdateAsync_repairs_source_mismatch_by_reinstalling_selected_version()
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
        WriteReceipt(installedPath, "OtherRepository", "C:\\OtherFeed");
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.SourcePolicy = new ManagedModuleSourcePolicy();

        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.SourceRepaired, result.Status);
        Assert.False(result.SourcePolicySatisfied);
        Assert.Equal("OtherRepository", result.InstalledReceipt?.RepositoryName);
        Assert.NotNull(result.InstallResult);
        AssertReceipt(result.ReceiptPath, "Update", "Company.Tools", "1.0.0", "1.0.0");
    }

    [Fact]
    public async Task UpdateAsync_blocks_source_repair_without_downgrading_newer_installed_version()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var installedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "2.0.0");
        Directory.CreateDirectory(installedPath);
        File.WriteAllText(Path.Combine(installedPath, "Company.Tools.psd1"), "@{ ModuleVersion = '2.0.0' }");
        WriteReceipt(installedPath, "OtherRepository", "C:\\OtherFeed");
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.SourcePolicy = new ManagedModuleSourcePolicy();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(request));

        Assert.Contains("without an explicit downgrade", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    private static ManagedModuleUpdateRequest CreateRequest(string feedPath, string moduleRoot)
        => CreateRequest(feedPath, moduleRoot, "Company.Tools");

    private static ManagedModuleUpdateRequest CreateRequest(string feedPath, string moduleRoot, string name)
        => new()
        {
            Repository = new ManagedModuleRepository("Local", feedPath),
            Name = name,
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot
        };

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version)
        => CreateModuleFiles("Company.Tools", version);

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string moduleName, string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [moduleName + ".psd1"] = "@{ ModuleVersion = '" + version + "' }"
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

    private static void WriteReceipt(string modulePath, string repositoryName, string repositorySource)
    {
        var receiptDirectory = Path.Combine(modulePath, ".powerforge");
        Directory.CreateDirectory(receiptDirectory);
        File.WriteAllText(
            Path.Combine(receiptDirectory, "managed-module-receipt.json"),
            "{\"RepositoryName\":\"" + repositoryName + "\",\"RepositorySource\":\"" + repositorySource.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"}");
    }
}
