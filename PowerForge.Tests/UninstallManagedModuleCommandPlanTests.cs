using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed partial class UninstallManagedModuleCommandTests
{
    [Fact]
    public void UninstallManagedModule_plan_reports_targets_without_removing_files()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleUninstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal("Company.Tools", Assert.Single(plan.Targets).Name);
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public void UninstallManagedModule_plan_blocks_dependency_target_before_returning_plan()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Core", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Core", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("Name", "Company.Core")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("Plan");

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("Company.Core 1.0.0 is required by Company.Tools 1.0.0", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public void UninstallManagedModule_plan_validates_selected_dependency_batch_together()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Core", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Core", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("Name", "Company.*")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleUninstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(
            new[] { "Company.Core", "Company.Tools" },
            plan.Targets.Select(static target => target.Name).OrderBy(static name => name).ToArray());
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public void UninstallManagedModule_plan_does_not_persist_sibling_plan_removal_assumptions()
    {
        using var workspace = new TemporaryDirectory();
        var coreRoot = Path.Combine(workspace.Path, "core");
        var toolsRoot = Path.Combine(workspace.Path, "tools");
        CreateInstalledModule(coreRoot, "Company.Core", "1.0.0");
        CreateInstalledModule(toolsRoot, "Company.Tools", "1.0.0", "Company.Core", "1.0.0");
        var corePath = Path.Combine(coreRoot, "Company.Core", "1.0.0");
        var toolsPath = Path.Combine(toolsRoot, "Company.Tools", "1.0.0");
        var inventoryPaths = new[]
        {
            CreateInventoryPath(coreRoot, "Core", "Custom", profileName: null),
            CreateInventoryPath(toolsRoot, "Core", "Custom", profileName: null)
        };
        var rows = new[]
        {
            CreateInventoryRow("Company.Core", "1.0.0", corePath, coreRoot, "Core", "Custom", null, inventoryPaths),
            CreateInventoryRow("Company.Tools", "1.0.0", toolsPath, toolsRoot, "Core", "Custom", null, inventoryPaths)
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("InputObject", rows)
            .AddParameter("Plan");
        var plans = ps.Invoke()
            .Select(static result => Assert.IsType<ManagedModuleUninstallPlan>(result.BaseObject))
            .ToArray();

        AssertNoPowerShellErrors(ps);
        Assert.Equal(2, plans.Length);
        var corePlan = Assert.Single(plans, static plan =>
            plan.Targets.Any(static target => target.Name == "Company.Core"));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ManagedModuleUninstallService().Uninstall(corePlan));
        Assert.Contains("required by Company.Tools", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(corePath));
        Assert.True(Directory.Exists(toolsPath));
    }
}
