using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleDependencyInstallServiceTests
{
    [Fact]
    public async Task InstallAsync_uses_installed_dependency_when_it_satisfies_declared_range()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var existingDependencyPath = Path.Combine(moduleRoot.Path, "Company.Core", "1.1.0");
        Directory.CreateDirectory(existingDependencyPath);
        File.WriteAllText(Path.Combine(existingDependencyPath, "Company.Core.psd1"), "@{ ModuleVersion = '1.1.0' }");
        File.WriteAllText(Path.Combine(existingDependencyPath, "marker.txt"), "keep");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0,2.0.0)", null) },
            files: CreateToolFiles("1.0.0"));
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
        Assert.Equal(ManagedModuleInstallStatus.AlreadyInstalled, dependency.Status);
        Assert.Equal("Company.Core", dependency.Name);
        Assert.Equal("1.1.0", dependency.Version);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(existingDependencyPath, "marker.txt")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_validates_dependency_package_when_dependency_trust_policy_is_active()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var existingDependencyPath = Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0");
        Directory.CreateDirectory(existingDependencyPath);
        File.WriteAllText(Path.Combine(existingDependencyPath, "Company.Core.psd1"), "@{ ModuleVersion = '1.0.0' }");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.1.0.nupkg"),
            "Company.Core",
            "1.1.0",
            files: CreateCoreFiles("1.1.0"),
            authors: "OtherPublisher");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0,2.0.0)", null) },
            files: CreateToolFiles("1.0.0"),
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
    }

    private static IReadOnlyDictionary<string, string> CreateToolFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static IReadOnlyDictionary<string, string> CreateCoreFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Core.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };
}
