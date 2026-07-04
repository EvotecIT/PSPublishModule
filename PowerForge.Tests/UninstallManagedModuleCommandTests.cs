using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class UninstallManagedModuleCommandTests
{
    [Fact]
    public void UninstallManagedModule_removes_selected_version_from_custom_root()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Version", "1.0.0")
            .AddParameter("Path", moduleRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleUninstallStatus.Uninstalled, result.Status);
        Assert.Equal("1.0.0", result.Version);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0")));
    }

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
    public void UninstallManagedModule_whatif_does_not_remove_files()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("WhatIf");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        Assert.Empty(results);
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public void UninstallManagedModule_reports_not_installed_for_missing_module()
    {
        using var moduleRoot = new TemporaryDirectory();

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("Name", "Company.Missing")
            .AddParameter("Path", moduleRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleUninstallStatus.NotInstalled, result.Status);
        Assert.Equal("Company.Missing", result.Name);
    }

    [Fact]
    public void UninstallManagedModule_rechecks_dependencies_after_target_filtering()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Core", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Core", "1.0.0");
        var service = new ManagedModuleUninstallService();
        var plan = service.PlanUninstall(new ManagedModuleUninstallRequest
        {
            Name = new[] { "Company.*" },
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });
        var selectedPlan = new ManagedModuleUninstallPlan
        {
            Name = plan.Name,
            Version = plan.Version,
            ModuleRoot = plan.ModuleRoot,
            SkipDependencyCheck = plan.SkipDependencyCheck,
            Targets = plan.Targets.Where(static target => target.Name == "Company.Core").ToArray()
        };

        var exception = Assert.Throws<InvalidOperationException>(() => service.Uninstall(selectedPlan));

        Assert.Contains("Company.Core 1.0.0 is required by Company.Tools 1.0.0", exception.Message);
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public void UninstallManagedModule_range_excludes_prerelease_versions_by_default()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0-beta1");
        var service = new ManagedModuleUninstallService();

        var stablePlan = service.PlanUninstall(new ManagedModuleUninstallRequest
        {
            Name = new[] { "Company.Tools" },
            Version = "[1.0.0,2.0.0)",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });
        var prereleasePlan = service.PlanUninstall(new ManagedModuleUninstallRequest
        {
            Name = new[] { "Company.Tools" },
            Version = "[1.0.0,2.0.0)",
            Prerelease = true,
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal("1.0.0", Assert.Single(stablePlan.Targets).Version);
        Assert.Contains(prereleasePlan.Targets, target => target.Version == "1.0.0");
        Assert.Contains(prereleasePlan.Targets, target => target.Version == "1.1.0-beta1");
    }

    [Fact]
    public void UninstallManagedModule_supports_powershell_wildcard_patterns()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.A1", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.B2", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.C3", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.A10", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("Name", "Company.[AB]?")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleUninstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(new[] { "Company.A1", "Company.B2" }, plan.Targets.Select(static target => target.Name).OrderBy(static name => name).ToArray());
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(UninstallManagedModuleCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static void CreateInstalledModule(
        string root,
        string name,
        string version,
        string? requiredModuleName = null,
        string? requiredVersion = null)
    {
        var modulePath = Path.Combine(root, name, version);
        Directory.CreateDirectory(modulePath);
        var prereleaseIndex = version.IndexOf('-');
        var moduleVersion = prereleaseIndex >= 0 ? version.Substring(0, prereleaseIndex) : version;
        var prerelease = prereleaseIndex >= 0 ? version.Substring(prereleaseIndex + 1) : null;
        var requiredModules = string.IsNullOrWhiteSpace(requiredModuleName)
            ? string.Empty
            : $@"
    RequiredModules = @(
        @{{
            ModuleName = '{requiredModuleName}'
            RequiredVersion = '{requiredVersion}'
        }}
    )";
        var privateData = string.IsNullOrWhiteSpace(prerelease)
            ? string.Empty
            : $@"
    PrivateData = @{{
        PSData = @{{
            Prerelease = '{prerelease}'
        }}
    }}";
        File.WriteAllText(Path.Combine(modulePath, name + ".psd1"), $$"""
@{
    RootModule = '{{name}}.psm1'
    ModuleVersion = '{{moduleVersion}}'{{requiredModules}}{{privateData}}
}
""");
        File.WriteAllText(Path.Combine(modulePath, name + ".psm1"), string.Empty);
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
