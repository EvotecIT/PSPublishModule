using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleUpdatePlanServiceTests
{
    [Fact]
    public async Task PlanUpdateAsync_classifies_missing_module_without_creating_directory()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var plan = await service.PlanUpdateAsync(CreateRequest(feed.Path, moduleRoot.Path));

        Assert.Equal(ManagedModuleUpdatePlanAction.InstallMissing, plan.Action);
        Assert.Equal("1.0.0", plan.TargetVersion);
        Assert.Null(plan.PreviousVersion);
        Assert.Empty(plan.InstalledVersions);
        Assert.True(plan.WouldWriteFiles);
        Assert.False(Directory.Exists(plan.ModulePath));
    }

    [Fact]
    public async Task PlanUpdateAsync_classifies_up_to_date_scope_without_writes()
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
        var service = new ManagedModuleUpdateService(new NullLogger());

        var plan = await service.PlanUpdateAsync(CreateRequest(feed.Path, moduleRoot.Path));

        Assert.Equal(ManagedModuleUpdatePlanAction.SkipUpToDate, plan.Action);
        Assert.Equal("1.0.0", plan.TargetVersion);
        Assert.Equal("1.0.0", plan.PreviousVersion);
        Assert.Equal(new[] { "1.0.0" }, plan.InstalledVersions);
        Assert.False(plan.WouldWriteFiles);
        Assert.Equal(installedPath, plan.ModulePath);
    }

    [Fact]
    public async Task PlanUpdateAsync_classifies_older_installed_version_as_update()
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
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));
        var service = new ManagedModuleUpdateService(new NullLogger());

        var plan = await service.PlanUpdateAsync(CreateRequest(feed.Path, moduleRoot.Path));

        Assert.Equal(ManagedModuleUpdatePlanAction.Update, plan.Action);
        Assert.Equal("1.2.0", plan.TargetVersion);
        Assert.Equal("1.0.0", plan.PreviousVersion);
        Assert.True(plan.WouldWriteFiles);
        Assert.Equal(Path.Combine(moduleRoot.Path, "Company.Tools", "1.2.0"), plan.ModulePath);
        Assert.False(Directory.Exists(plan.ModulePath));
    }

    [Fact]
    public async Task PlanUpdateAsync_classifies_force_on_current_version_as_reinstall()
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
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.Force = true;

        var plan = await service.PlanUpdateAsync(request);

        Assert.Equal(ManagedModuleUpdatePlanAction.Reinstall, plan.Action);
        Assert.Equal("1.0.0", plan.TargetVersion);
        Assert.Equal("1.0.0", plan.PreviousVersion);
        Assert.True(plan.WouldWriteFiles);
        Assert.Equal(installedPath, plan.ModulePath);
    }

    [Fact]
    public async Task PlanUpdateAsync_preserves_requested_version_policy()
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

        var plan = await service.PlanUpdateAsync(request);

        Assert.Equal(ManagedModuleUpdatePlanAction.Update, plan.Action);
        Assert.Equal("1.5.0", plan.TargetVersion);
        Assert.Equal("(1.0.0,2.0.0)", plan.VersionPolicy);
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
}
