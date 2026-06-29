using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleInstallPlanServiceTests
{
    [Fact]
    public async Task PlanInstallAsync_resolves_target_without_creating_module_directory()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.2.0.nupkg"),
            "Company.Tools",
            "1.2.0",
            files: CreateModuleFiles("1.2.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var plan = await service.PlanInstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal(ManagedModuleInstallPlanAction.Install, plan.Action);
        Assert.Equal("1.2.0", plan.Version);
        Assert.True(plan.WouldWriteFiles);
        Assert.False(plan.ExistingVersionFound);
        Assert.False(Directory.Exists(plan.ModulePath));
    }

    [Fact]
    public async Task PlanInstallAsync_classifies_existing_version_without_force()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var existingPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(existingPath);
        var service = new ManagedModuleInstallService(new NullLogger());

        var plan = await service.PlanInstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal(ManagedModuleInstallPlanAction.SkipExisting, plan.Action);
        Assert.False(plan.WouldWriteFiles);
        Assert.True(plan.ExistingVersionFound);
        Assert.Equal(existingPath, plan.ModulePath);
    }

    [Fact]
    public async Task PlanInstallAsync_classifies_existing_version_with_force_as_reinstall()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var existingPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(existingPath);
        var service = new ManagedModuleInstallService(new NullLogger());

        var plan = await service.PlanInstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            Force = true
        });

        Assert.Equal(ManagedModuleInstallPlanAction.Reinstall, plan.Action);
        Assert.True(plan.WouldWriteFiles);
        Assert.True(plan.ExistingVersionFound);
        Assert.Equal(existingPath, plan.ModulePath);
    }

    [Fact]
    public async Task PlanInstallAsync_filters_flat_layout_child_folders_from_installed_versions()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var flatModulePath = Path.Combine(moduleRoot.Path, "Company.Tools");
        Directory.CreateDirectory(Path.Combine(flatModulePath, "en-US"));
        Directory.CreateDirectory(Path.Combine(flatModulePath, "bin"));
        File.WriteAllText(Path.Combine(flatModulePath, "Company.Tools.psd1"), "@{ ModuleVersion = '1.0.0' }");
        var service = new ManagedModuleInstallService(new NullLogger());

        var plan = await service.PlanInstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal(ManagedModuleInstallPlanAction.SkipExisting, plan.Action);
        Assert.Equal("1.0.0", plan.Version);
        Assert.True(plan.ExistingVersionFound);
    }

    [Fact]
    public async Task PlanInstallAsync_reinstalls_existing_version_when_expected_hash_is_supplied()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var packagePath = Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var existingPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(existingPath);
        var service = new ManagedModuleInstallService(new NullLogger());

        var plan = await service.PlanInstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            ExpectedPackageSha256 = ComputeSha256(packagePath),
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal(ManagedModuleInstallPlanAction.Reinstall, plan.Action);
        Assert.True(plan.WouldWriteFiles);
        Assert.True(plan.ExistingVersionFound);
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return string.Concat(sha256.ComputeHash(stream).Select(static value => value.ToString("x2")));
    }
}
