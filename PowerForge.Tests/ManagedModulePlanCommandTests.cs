using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ManagedModulePlanCommandTests
{
    [Fact]
    public void InstallManagedModule_plan_outputs_install_plan_without_writing_files()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleInstallPlanAction.Install, plan.Action);
        Assert.Equal("1.0.0", plan.Version);
        Assert.True(plan.WouldWriteFiles);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools")));
    }

    [Fact]
    public void UpdateManagedModule_plan_outputs_update_plan_without_writing_files()
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

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Update-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleUpdatePlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleUpdatePlanAction.Update, plan.Action);
        Assert.Equal("1.1.0", plan.TargetVersion);
        Assert.Equal("1.0.0", plan.PreviousVersion);
        Assert.True(plan.WouldWriteFiles);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0")));
    }

    [Fact]
    public void InstallManagedModule_plan_skips_existing_exact_version_without_force()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleInstallPlanAction.SkipExisting, plan.Action);
        Assert.False(plan.WouldWriteFiles);
        Assert.True(plan.ExistingVersionFound);
    }

    [Fact]
    public void InstallManagedModule_plan_reinstalls_existing_exact_version_with_force()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan")
            .AddParameter("Force");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleInstallPlanAction.Reinstall, plan.Action);
        Assert.True(plan.WouldWriteFiles);
        Assert.True(plan.ExistingVersionFound);
    }

    [Fact]
    public void InstallManagedModule_plan_reinstalls_existing_exact_version_with_reinstall()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan")
            .AddParameter("Reinstall");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleInstallPlanAction.Reinstall, plan.Action);
        Assert.True(plan.WouldWriteFiles);
        Assert.True(plan.ExistingVersionFound);
    }

    [Fact]
    public void UpdateManagedModule_plan_reinstalls_selected_version_with_force_when_already_current()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Update-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan")
            .AddParameter("Force");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleUpdatePlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleUpdatePlanAction.Reinstall, plan.Action);
        Assert.Equal("1.0.0", plan.TargetVersion);
        Assert.Equal("1.0.0", plan.PreviousVersion);
        Assert.True(plan.WouldWriteFiles);
    }

    [Fact]
    public void UpdateManagedModule_plan_does_not_downgrade_newer_installed_version_with_force()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Update-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan")
            .AddParameter("Force");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleUpdatePlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleUpdatePlanAction.DowngradeBlocked, plan.Action);
        Assert.Equal("1.0.0", plan.TargetVersion);
        Assert.Equal("1.1.0", plan.PreviousVersion);
        Assert.False(plan.WouldWriteFiles);
        Assert.Equal(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0"), plan.ModulePath);
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(InstallManagedModuleCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
