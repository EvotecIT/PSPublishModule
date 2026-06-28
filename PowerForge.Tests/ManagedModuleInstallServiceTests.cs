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
        Assert.True(result.Elapsed > TimeSpan.Zero);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Public", "Get-CompanyTool.ps1")));
        Assert.False(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.nuspec")));
        Assert.Equal(2, result.FileCount);
        Assert.True(result.ExtractedBytes > 0);
        Assert.True(result.VersionResolutionElapsed >= TimeSpan.Zero);
        Assert.True(result.DownloadElapsed > TimeSpan.Zero);
        Assert.True(result.ExtractionElapsed > TimeSpan.Zero);
        Assert.True(result.DependencyElapsed >= TimeSpan.Zero);
        Assert.True(result.PromotionElapsed > TimeSpan.Zero);
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
        Assert.True(result.Elapsed > TimeSpan.Zero);
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
        Assert.Equal(feed.Path, result.RepositorySource);
        Assert.Equal("1.1.0", result.MinimumVersion);
        Assert.Equal("1.9.9", result.MaximumVersion);
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
        Assert.Equal("(1.0.0,2.0.0)", result.VersionPolicy);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.5.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_infers_prerelease_from_version_policy()
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
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            VersionPolicy = "[1.1.0-preview.1,1.1.0)",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal("1.1.0-preview.10", result.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0-preview.10", "Company.Tools.psd1")));
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
    public async Task InstallAsync_rejects_package_when_expected_sha256_does_not_match()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var exception = await Assert.ThrowsAsync<ManagedModulePackageIntegrityException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            ExpectedPackageSha256 = new string('0', 64),
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        }));

        Assert.Equal("Company.Tools", exception.ModuleName);
        Assert.Equal("1.0.0", exception.Version);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools")));
    }

    [Fact]
    public async Task PlanInstallAsync_rejects_untrusted_repository_when_policy_requires_trust()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var service = new ManagedModuleInstallService(new NullLogger());

        var exception = await Assert.ThrowsAsync<ManagedModuleTrustException>(() => service.PlanInstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path, ManagedModuleRepositoryKind.Auto, trusted: false),
            Name = "Company.Tools",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            TrustPolicy = new ManagedModuleTrustPolicy
            {
                RequireTrustedRepository = true
            }
        }));

        Assert.Equal("RepositoryNotTrusted", exception.Reason);
        Assert.Equal("Local", exception.RepositoryName);
    }

    [Fact]
    public async Task InstallAsync_rejects_package_when_author_is_not_allowed()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"),
            authors: "OtherPublisher");
        var service = new ManagedModuleInstallService(new NullLogger());

        var exception = await Assert.ThrowsAsync<ManagedModuleTrustException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            TrustPolicy = new ManagedModuleTrustPolicy
            {
                AllowedAuthors = new[] { "Evotec" }
            }
        }));

        Assert.Equal("PackageAuthorNotAllowed", exception.Reason);
        Assert.Equal("Company.Tools", exception.ModuleName);
        Assert.Equal("1.0.0", exception.Version);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools")));
    }

    [Fact]
    public async Task InstallAsync_applies_author_policy_to_dependency_packages()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateDependencyFiles("1.0.0"),
            authors: "OtherPublisher");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateModuleFiles("1.0.0"),
            authors: "Evotec");
        var service = new ManagedModuleInstallService(new NullLogger());

        var exception = await Assert.ThrowsAsync<ManagedModuleTrustException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            TrustPolicy = new ManagedModuleTrustPolicy
            {
                AllowedAuthors = new[] { "Evotec" }
            }
        }));

        Assert.Equal("Company.Core", exception.ModuleName);
        Assert.Equal("PackageAuthorNotAllowed", exception.Reason);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0")));
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
    public async Task InstallAsync_installs_nested_dependency_closure()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Base.1.0.0.nupkg"),
            "Company.Base",
            "1.0.0",
            files: CreateBaseFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Base", "[1.0.0]", null) },
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
            ModuleRoot = moduleRoot.Path
        });

        var directDependency = Assert.Single(result.DependencyResults);
        var nestedDependency = Assert.Single(directDependency.DependencyResults);
        Assert.Equal("Company.Core", directDependency.Name);
        Assert.Equal("Company.Base", nestedDependency.Name);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Base", "1.0.0", "Company.Base.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0", "Company.Core.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_installs_shared_nested_dependency_once()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Shared.1.0.0.nupkg"),
            "Company.Shared",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Shared.psd1"] = "@{ ModuleVersion = '1.0.0' }"
            });
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.A.1.0.0.nupkg"),
            "Company.A",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Shared", "[1.0.0]", null) },
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.A.psd1"] = "@{ ModuleVersion = '1.0.0' }"
            });
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.B.1.0.0.nupkg"),
            "Company.B",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Shared", "[1.0.0]", null) },
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.B.psd1"] = "@{ ModuleVersion = '1.0.0' }"
            });
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[]
            {
                new TestDependency("Company.A", "[1.0.0]", null),
                new TestDependency("Company.B", "[1.0.0]", null)
            },
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

        Assert.Equal(new[] { "Company.A", "Company.B" }, result.DependencyResults.Select(dependency => dependency.Name).ToArray());
        var sharedResults = result.DependencyResults
            .SelectMany(dependency => dependency.DependencyResults)
            .Where(dependency => dependency.Name == "Company.Shared")
            .ToArray();
        Assert.Equal(2, sharedResults.Length);
        Assert.Single(sharedResults, dependency => dependency.Status == ManagedModuleInstallStatus.Installed);
        Assert.Single(sharedResults, dependency => dependency.Status == ManagedModuleInstallStatus.AlreadyInstalled);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Shared", "1.0.0", "Company.Shared.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.A", "1.0.0", "Company.A.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.B", "1.0.0", "Company.B.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_infers_prerelease_from_dependency_range()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.2.0.0-preview.2.nupkg"),
            "Company.Core",
            "2.0.0-preview.2",
            files: CreateDependencyFiles("2.0.0-preview.2"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.2.0.0-preview.10.nupkg"),
            "Company.Core",
            "2.0.0-preview.10",
            files: CreateDependencyFiles("2.0.0-preview.10"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[2.0.0-preview.1,2.0.0)", null) },
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

        var dependency = Assert.Single(result.DependencyResults);
        Assert.Equal("Company.Core", dependency.Name);
        Assert.Equal("2.0.0-preview.10", dependency.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "2.0.0-preview.10", "Company.Core.psd1")));
    }

    [Fact]
    public async Task InstallAsync_installs_manifest_required_modules()
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
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = """
                    @{
                        ModuleVersion = '1.0.0'
                        RequiredModules = @(
                            @{ ModuleName = 'Company.Core'; RequiredVersion = '1.0.0' }
                        )
                    }
                    """
            });
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        var dependency = Assert.Single(result.DependencyResults);
        Assert.Equal("Company.Core", dependency.Name);
        Assert.Equal("1.0.0", dependency.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0", "Company.Core.psd1")));
    }

    [Fact]
    public async Task InstallAsync_rejects_dependency_cycles()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Tools", "[1.0.0]", null) },
            files: CreateDependencyFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        }));

        Assert.Contains("dependency cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
        var modulePath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Assert.False(Directory.Exists(modulePath));
        Assert.False(File.Exists(Path.Combine(modulePath, ".powerforge", "managed-module-receipt.json")));
    }

    [Fact]
    public async Task InstallAsync_rejects_cross_dependency_cycles()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.A.1.0.0.nupkg"),
            "Company.A",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.B", "[1.0.0]", null) },
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.A.psd1"] = "@{ ModuleVersion = '1.0.0' }"
            });
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.B.1.0.0.nupkg"),
            "Company.B",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.A", "[1.0.0]", null) },
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.B.psd1"] = "@{ ModuleVersion = '1.0.0' }"
            });
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[]
            {
                new TestDependency("Company.A", "[1.0.0]", null),
                new TestDependency("Company.B", "[1.0.0]", null)
            },
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        }));

        Assert.Contains("dependency cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public async Task InstallAsync_rejects_export_conflicts_without_allow_clobber()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var existingPath = Path.Combine(moduleRoot.Path, "Company.Existing", "1.0.0");
        Directory.CreateDirectory(existingPath);
        File.WriteAllText(
            Path.Combine(existingPath, "Company.Existing.psd1"),
            CreateManifest("1.0.0", "Get-CompanyTool"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = CreateManifest("1.0.0", "Get-CompanyTool")
            });
        var service = new ManagedModuleInstallService(new NullLogger());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        }));

        Assert.Contains("export conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public async Task InstallAsync_allows_export_conflicts_with_allow_clobber()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var existingPath = Path.Combine(moduleRoot.Path, "Company.Existing", "1.0.0");
        Directory.CreateDirectory(existingPath);
        File.WriteAllText(
            Path.Combine(existingPath, "Company.Existing.psd1"),
            CreateManifest("1.0.0", "Get-CompanyTool"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = CreateManifest("1.0.0", "Get-CompanyTool")
            });
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            AllowClobber = true
        });

        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
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

    private static IReadOnlyDictionary<string, string> CreateBaseFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Base.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static string CreateManifest(string version, string functionName)
        => "@{" + Environment.NewLine +
           "    ModuleVersion = '" + version + "'" + Environment.NewLine +
           "    FunctionsToExport = @('" + functionName + "')" + Environment.NewLine +
           "    CmdletsToExport = @()" + Environment.NewLine +
           "    AliasesToExport = @()" + Environment.NewLine +
           "}";

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
