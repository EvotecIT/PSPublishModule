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

    [Fact]
    public async Task PlanUpdateAsync_includes_family_actions_for_installed_prefix_modules()
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

        var plan = await service.PlanUpdateAsync(request);

        Assert.Equal(ManagedModuleUpdatePlanAction.Update, plan.Action);
        Assert.True(plan.WouldWriteFiles);
        var familyAction = Assert.Single(plan.FamilyActions);
        Assert.Equal("Company.Cloud.Groups", familyAction.Name);
        Assert.Equal("CompanyCloud", familyAction.FamilyName);
        Assert.Equal("2.0.0", familyAction.TargetVersion);
        Assert.Equal("1.5.0", familyAction.PreviousVersion);
        Assert.Equal(ManagedModuleFamilyUpdatePlanAction.Update, familyAction.Action);
        Assert.True(familyAction.RepositoryVersionAvailable);
        Assert.True(familyAction.WouldWriteFiles);
    }

    [Fact]
    public async Task PlanUpdateAsync_reports_family_member_missing_target_repository_version()
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

        var plan = await service.PlanUpdateAsync(request);

        var familyAction = Assert.Single(plan.FamilyActions);
        Assert.Equal(ManagedModuleFamilyUpdatePlanAction.MissingRepositoryVersion, familyAction.Action);
        Assert.False(familyAction.RepositoryVersionAvailable);
        Assert.False(familyAction.WouldWriteFiles);
        Assert.Contains("Repository does not contain", familyAction.ConflictReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanUpdateAsync_repairs_source_mismatch_for_current_version()
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
        WriteReceipt(installedPath, "OtherRepository", "C:\\OtherFeed");
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.SourcePolicy = new ManagedModuleSourcePolicy();

        var plan = await service.PlanUpdateAsync(request);

        Assert.Equal(ManagedModuleUpdatePlanAction.RepairSource, plan.Action);
        Assert.True(plan.WouldWriteFiles);
        Assert.False(plan.SourcePolicySatisfied);
        Assert.NotNull(plan.InstalledReceipt);
        Assert.Contains("repository name", plan.SourcePolicyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanUpdateAsync_blocks_source_repair_when_installed_version_is_newer_than_target()
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
        WriteReceipt(installedPath, "OtherRepository", "C:\\OtherFeed");
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = CreateRequest(feed.Path, moduleRoot.Path);
        request.SourcePolicy = new ManagedModuleSourcePolicy();

        var plan = await service.PlanUpdateAsync(request);

        Assert.Equal(ManagedModuleUpdatePlanAction.SourceMismatchBlocked, plan.Action);
        Assert.False(plan.WouldWriteFiles);
        Assert.False(plan.SourcePolicySatisfied);
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

    private static void WriteReceipt(string modulePath, string repositoryName, string repositorySource)
    {
        var receiptDirectory = Path.Combine(modulePath, ".powerforge");
        Directory.CreateDirectory(receiptDirectory);
        File.WriteAllText(
            Path.Combine(receiptDirectory, "managed-module-receipt.json"),
            "{\"RepositoryName\":\"" + repositoryName + "\",\"RepositorySource\":\"" + repositorySource.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"}");
    }
}
