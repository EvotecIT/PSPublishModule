using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed partial class UninstallManagedModuleCommandTests
{
    [Fact]
    public void UninstallManagedModule_blocks_module_loaded_in_current_runspace()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var installedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        var manifestPath = Path.Combine(installedPath, "Company.Tools.psd1");
        File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.0.0' }");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", manifestPath)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();

        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("Confirm", false);

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("AllowLoadedModuleUninstall", exception.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(installedPath));
    }

    [Fact]
    public void ExecuteSelectedPlans_refreshes_stale_loaded_state_before_batch_validation()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var installedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        var service = new ManagedModuleUninstallService();
        var plan = service.PlanUninstall(new ManagedModuleUninstallRequest
        {
            Name = new[] { "Company.Tools" },
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            SkipDependencyCheck = true,
            DeferLoadedModuleCheck = true
        });
        Assert.False(Assert.Single(plan.Targets).IsLoaded);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            UninstallManagedModuleCommand.ExecuteSelectedPlans(
                service,
                new[] { plan },
                () => new[]
                {
                    new ManagedModuleLoadedModule
                    {
                        Name = "Company.Tools",
                        Version = "1.0.0",
                        ModuleBase = installedPath
                    }
                },
                static _ => { }));

        Assert.Contains("AllowLoadedModuleUninstall", exception.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(installedPath));
    }

    [Fact]
    public void ExecuteSelectedPlans_refreshes_loaded_state_before_each_mutation()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(
            moduleRoot.Path,
            "Company.First",
            "1.0.0",
            requiredModuleName: "Company.Second",
            requiredVersion: "1.0.0");
        CreateInstalledModule(
            moduleRoot.Path,
            "Company.Second",
            "1.0.0",
            requiredModuleName: "Company.First",
            requiredVersion: "1.0.0");
        var service = new ManagedModuleUninstallService();
        var plan = service.PlanUninstall(new ManagedModuleUninstallRequest
        {
            Name = new[] { "Company.*" },
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            DeferLoadedModuleCheck = true,
            DeferDependencyCheck = true
        });
        var orderedTargets = ManagedModuleUninstallService.OrderTargetsForRemoval(plan.Targets).ToArray();
        Assert.Equal(2, orderedTargets.Length);
        var firstTarget = orderedTargets[0];
        var secondTarget = orderedTargets[1];
        var refreshCount = 0;
        var results = new List<ManagedModuleUninstallResult>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            UninstallManagedModuleCommand.ExecuteSelectedPlans(
                service,
                new[] { plan },
                () => ++refreshCount < 3
                    ? Array.Empty<ManagedModuleLoadedModule>()
                    : new[]
                    {
                        new ManagedModuleLoadedModule
                        {
                            Name = secondTarget.Name,
                            Version = secondTarget.Version,
                            ModuleBase = secondTarget.ModulePath
                        }
                    },
                batch => results.AddRange(batch)));

        Assert.Contains("AllowLoadedModuleUninstall", exception.Message, StringComparison.Ordinal);
        Assert.Equal(3, refreshCount);
        Assert.Equal(firstTarget.Name, Assert.Single(results).Name);
        Assert.False(Directory.Exists(firstTarget.ModulePath));
        Assert.True(Directory.Exists(secondTarget.ModulePath));
    }
}
