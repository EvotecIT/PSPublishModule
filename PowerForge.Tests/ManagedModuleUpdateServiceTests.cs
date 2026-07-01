using System.Security.Cryptography;
using System.Text.Json;
using System.Net;
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
    public async Task UpdateAsync_validates_allowed_author_before_same_version_noop()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"),
            authors: "OtherPublisher");
        var installedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(installedPath);
        File.WriteAllText(Path.Combine(installedPath, "Company.Tools.psd1"), "@{ ModuleVersion = '1.0.0' }");
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.TrustPolicy = new ManagedModuleTrustPolicy
        {
            AllowedAuthors = new[] { "Evotec" }
        };

        var exception = await Assert.ThrowsAsync<ManagedModuleTrustException>(() => service.UpdateAsync(request));

        Assert.Equal("PackageAuthorNotAllowed", exception.Reason);
        Assert.Equal("Company.Tools", exception.ModuleName);
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
    public async Task PlanUpdateAsync_ignores_unlisted_repository_versions()
    {
        var requests = new List<string>();
        using var moduleRoot = new TemporaryDirectory();
        var installedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(installedPath);
        File.WriteAllText(Path.Combine(installedPath, "Company.Tools.psd1"), "@{ ModuleVersion = '1.0.0' }");
        using var client = new HttpClient(new UnlistedUpdateHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var service = new ManagedModuleUpdateService(new NullLogger(), repositoryClient);

        var plan = await service.PlanUpdateAsync(new ManagedModuleUpdateRequest
        {
            Repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json"),
            Name = "Company.Tools",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal("1.5.0", plan.TargetVersion);
        Assert.DoesNotContain("2.0.0", plan.TargetVersion);
        Assert.Contains(requests, request => request == "https://example.test/registration/company.tools/index.json");
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
    public async Task UpdateAsync_normalizes_requested_exact_target_version()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "0.9.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.Version = "1.0";
        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.Equal("1.0.0", result.TargetVersion);
        Assert.Equal(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"), result.ModulePath);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0")));
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
    public async Task UpdateAsync_reinstalls_current_version_when_expected_sha256_is_supplied()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var packagePath = Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var installedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(installedPath);
        File.WriteAllText(Path.Combine(installedPath, "marker.txt"), "old");
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.Version = "1.0.0";
        request.ExpectedPackageSha256 = ComputeSha256(packagePath);

        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.NotNull(result.InstallResult?.Download);
        Assert.False(File.Exists(Path.Combine(installedPath, "marker.txt")));
        Assert.True(File.Exists(Path.Combine(installedPath, "Company.Tools.psd1")));
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
    public async Task UpdateAsync_honors_comparator_version_policy()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.5.0.nupkg"),
            "Company.Tools",
            "1.5.0",
            files: CreateModuleFiles("1.5.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.2.1.0.nupkg"),
            "Company.Tools",
            "2.1.0",
            files: CreateModuleFiles("2.1.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.3.0.0.nupkg"),
            "Company.Tools",
            "3.0.0",
            files: CreateModuleFiles("3.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.VersionPolicy = ">=2.0.0 <3.0.0";
        var result = await service.UpdateAsync(request);

        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.Equal("2.1.0", result.TargetVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "2.1.0", "Company.Tools.psd1")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.5.0")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "3.0.0")));
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
    public async Task UpdateAsync_reports_updated_when_only_family_member_changes()
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
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "2.0.0"));
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
        Assert.Null(result.InstallResult);
        var familyResult = Assert.Single(result.FamilyResults);
        Assert.Equal("Company.Cloud.Groups", familyResult.Name);
        Assert.Equal(ManagedModuleFamilyUpdatePlanAction.Update, familyResult.Action);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Cloud.Groups", "2.0.0", "Company.Cloud.Groups.psd1")));
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
    public async Task UpdateAsync_blocks_family_update_before_writing_when_target_version_is_unlisted_for_member()
    {
        var requests = new List<string>();
        using var moduleRoot = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Groups", "1.5.0"));
        using var client = new HttpClient(new FamilyUnlistedUpdateHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var service = new ManagedModuleUpdateService(new NullLogger(), repositoryClient);
        var request = new ManagedModuleUpdateRequest
        {
            Repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json"),
            Name = "Company.Cloud.Users",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            FamilyPolicy = new ManagedModuleFamilyPolicy
            {
                ModuleNamePrefix = "Company.Cloud."
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(request));

        Assert.Contains("family update cannot be applied", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Company.Cloud.Groups", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "2.0.0")));
        Assert.Contains("https://example.test/registration/company.cloud.groups/index.json", requests);
    }

    [Fact]
    public async Task UpdateAsync_blocks_family_author_policy_before_writing_requested_module()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Cloud.Users.2.0.0.nupkg"),
            "Company.Cloud.Users",
            "2.0.0",
            files: CreateModuleFiles("Company.Cloud.Users", "2.0.0"),
            authors: "Evotec");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Cloud.Groups.2.0.0.nupkg"),
            "Company.Cloud.Groups",
            "2.0.0",
            files: CreateModuleFiles("Company.Cloud.Groups", "2.0.0"),
            authors: "OtherPublisher");
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Groups", "1.5.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path, "Company.Cloud.Users");
        request.TrustPolicy = new ManagedModuleTrustPolicy
        {
            AllowedAuthors = new[] { "Evotec" }
        };
        request.FamilyPolicy = new ManagedModuleFamilyPolicy
        {
            ModuleNamePrefix = "Company.Cloud."
        };

        var exception = await Assert.ThrowsAsync<ManagedModuleTrustException>(() => service.UpdateAsync(request));

        Assert.Equal("PackageAuthorNotAllowed", exception.Reason);
        Assert.Equal("Company.Cloud.Groups", exception.ModuleName);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "2.0.0")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Cloud.Groups", "2.0.0")));
    }

    [Fact]
    public async Task UpdateAsync_blocks_family_license_acceptance_before_writing_requested_module()
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
            files: CreateModuleFiles("Company.Cloud.Groups", "2.0.0"),
            requireLicenseAcceptance: true);
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Cloud.Groups", "1.5.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path, "Company.Cloud.Users");
        request.FamilyPolicy = new ManagedModuleFamilyPolicy
        {
            ModuleNamePrefix = "Company.Cloud."
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(request));

        Assert.Contains("requires license acceptance", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Company.Cloud.Groups", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Cloud.Users", "2.0.0")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Cloud.Groups", "2.0.0")));
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

    private static string ComputeSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed class UnlistedUpdateHandler : HttpMessageHandler
    {
        private readonly List<string> _requests;

        internal UnlistedUpdateHandler(List<string> requests)
            => _requests = requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            _requests.Add(uri);

            if (uri == "https://example.test/v3/index.json")
                return Json("{\"resources\":[" +
                            "{\"@id\":\"https://example.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}," +
                            "{\"@id\":\"https://example.test/search/\",\"@type\":\"SearchQueryService/3.5.0\"}," +
                            "{\"@id\":\"https://example.test/registration/\",\"@type\":\"RegistrationsBaseUrl/3.6.0\"}" +
                            "]}");

            if (uri == "https://example.test/packages/company.tools/index.json")
                return Json("{\"versions\":[\"1.0.0\",\"1.5.0\",\"2.0.0\"]}");

            if (uri == "https://example.test/registration/company.tools/index.json")
                return Json("{\"items\":[{\"items\":[" +
                            "{\"catalogEntry\":{\"version\":\"1.0.0\",\"listed\":true}}," +
                            "{\"catalogEntry\":{\"version\":\"1.5.0\",\"listed\":true}}," +
                            "{\"catalogEntry\":{\"version\":\"2.0.0\",\"listed\":false}}" +
                            "]}]}");

            if (uri == "https://example.test/packages/company.tools/1.5.0/company.tools.1.5.0.nupkg")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(TestPackageFactory.CreateBytes("Company.Tools", "1.5.0"))
                });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string content)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private sealed class FamilyUnlistedUpdateHandler : HttpMessageHandler
    {
        private readonly List<string> _requests;

        internal FamilyUnlistedUpdateHandler(List<string> requests)
            => _requests = requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            _requests.Add(uri);

            if (uri == "https://example.test/v3/index.json")
                return Json("{\"resources\":[" +
                            "{\"@id\":\"https://example.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}," +
                            "{\"@id\":\"https://example.test/registration/\",\"@type\":\"RegistrationsBaseUrl/3.6.0\"}" +
                            "]}");

            if (uri == "https://example.test/packages/company.cloud.users/index.json")
                return Json("{\"versions\":[\"2.0.0\"]}");

            if (uri == "https://example.test/packages/company.cloud.groups/index.json")
                return Json("{\"versions\":[\"2.0.0\"]}");

            if (uri == "https://example.test/registration/company.cloud.users/index.json")
                return Json("{\"items\":[{\"items\":[" +
                            "{\"catalogEntry\":{\"version\":\"2.0.0\",\"listed\":true}}" +
                            "]}]}");

            if (uri == "https://example.test/registration/company.cloud.groups/index.json")
                return Json("{\"items\":[{\"items\":[" +
                            "{\"catalogEntry\":{\"version\":\"2.0.0\",\"listed\":false}}" +
                            "]}]}");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string content)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
