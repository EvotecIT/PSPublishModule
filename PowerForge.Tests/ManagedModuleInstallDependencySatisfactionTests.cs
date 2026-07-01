using PowerForge;

namespace PowerForge.Tests;

public sealed partial class ManagedModuleInstallServiceTests
{
    [Fact]
    public async Task InstallAsync_unbounded_dependency_uses_installed_stable_copy_without_repository_lookup()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", null, null) },
            files: CreateModuleFiles("1.0.0"));
        var dependencyPath = Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0");
        Directory.CreateDirectory(dependencyPath);
        File.WriteAllText(Path.Combine(dependencyPath, "Company.Core.psd1"), "@{ ModuleVersion = '1.0.0' }");
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
        Assert.Equal(ManagedModuleInstallStatus.AlreadyInstalled, dependency.Status);
        Assert.Null(dependency.Download);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_unbounded_dependency_does_not_accept_prerelease_without_prerelease_intent()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", null, null) },
            files: CreateModuleFiles("1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Core", "2.0.0-preview.1"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        }));

        Assert.Contains("No dependency versions of 'Company.Core'", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }
}
