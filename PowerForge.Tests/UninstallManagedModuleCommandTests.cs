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
    public void UninstallManagedModule_reports_not_installed_for_missing_name_in_mixed_request()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("Name", new[] { "Company.Tools", "Company.Missing" })
            .AddParameter("Path", moduleRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var typed = results.Select(item => Assert.IsType<ManagedModuleUninstallResult>(item.BaseObject)).ToArray();
        Assert.Contains(typed, result => result.Name == "Company.Tools" && result.Status == ManagedModuleUninstallStatus.Uninstalled);
        Assert.Contains(typed, result => result.Name == "Company.Missing" && result.Status == ManagedModuleUninstallStatus.NotInstalled);
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
    public void UninstallManagedModule_rejects_malformed_comparator_range()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var service = new ManagedModuleUninstallService();

        foreach (var version in new[] { ">=1.0.0 < 2.0.0", ">=1.0.0 <" })
        {
            var exception = Assert.Throws<ArgumentException>(() => service.PlanUninstall(new ManagedModuleUninstallRequest
            {
                Name = new[] { "Company.Tools" },
                Version = version,
                Scope = ManagedModuleInstallScope.Custom,
                ModuleRoot = moduleRoot.Path
            }));

            Assert.Contains("Invalid version range syntax", exception.Message);
        }

        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public void UninstallManagedModule_flat_layout_uninstall_preserves_versioned_siblings()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateFlatInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "2.0.0");
        var flatManifest = Path.Combine(moduleRoot.Path, "Company.Tools", "Company.Tools.psd1");
        var siblingManifest = Path.Combine(moduleRoot.Path, "Company.Tools", "2.0.0", "Company.Tools.psd1");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Version", "1.0.0")
            .AddParameter("Path", moduleRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleUninstallStatus.Uninstalled, result.Status);
        Assert.False(File.Exists(flatManifest));
        Assert.True(File.Exists(siblingManifest));
    }

    [Fact]
    public void UninstallManagedModule_dependency_check_honors_required_module_guid()
    {
        using var moduleRoot = new TemporaryDirectory();
        const string requiredGuid = "11111111-1111-1111-1111-111111111111";
        const string otherGuid = "22222222-2222-2222-2222-222222222222";
        CreateInstalledModule(moduleRoot.Path, "Company.Core", "1.0.0", moduleGuid: requiredGuid);
        CreateInstalledModule(moduleRoot.Path, "Company.Core", "1.1.0", moduleGuid: otherGuid);
        CreateInstalledModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            requiredModuleName: "Company.Core",
            requiredModuleVersion: "1.0.0",
            requiredModuleGuid: requiredGuid);
        var service = new ManagedModuleUninstallService();

        var exception = Assert.Throws<InvalidOperationException>(() => service.PlanUninstall(new ManagedModuleUninstallRequest
        {
            Name = new[] { "Company.Core" },
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        }));

        Assert.Contains("Company.Core 1.0.0 is required by Company.Tools 1.0.0", exception.Message);
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

    [Fact]
    public void UninstallManagedModule_binds_piped_inventory_path_and_version()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0");
        var installedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddScript("$row = [pscustomobject]@{ Name = 'Company.Tools'; Version = '1.0.0'; Path = '" + EscapePowerShellSingleQuoted(installedPath) + "' }; $row | Uninstall-ManagedModule");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal("1.0.0", result.Version);
        Assert.False(Directory.Exists(installedPath));
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0")));
    }

    [Fact]
    public void UninstallManagedModule_reports_not_installed_when_requested_version_is_missing()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Version", "2.0.0")
            .AddParameter("Path", moduleRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleUninstallStatus.NotInstalled, result.Status);
        Assert.Equal("Company.Tools", result.Name);
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public void UninstallManagedModule_removes_dependents_before_dependencies()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Core", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Lib", "1.0.0", "Company.Core", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Lib", "1.0.0");
        var service = new ManagedModuleUninstallService();
        var plan = service.PlanUninstall(new ManagedModuleUninstallRequest
        {
            Name = new[] { "Company.*" },
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        var results = service.Uninstall(plan);

        Assert.Equal(new[] { "Company.Tools", "Company.Lib", "Company.Core" }, results.Select(static result => result.Name).ToArray());
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Lib", "1.0.0")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0")));
    }

    [Fact]
    public void UninstallManagedModule_rejects_case_variant_root_escape_on_case_sensitive_filesystems()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var plan = new ManagedModuleUninstallPlan
        {
            Name = new[] { "Company.Tools" },
            ModuleRoot = "/tmp/modules",
            SkipDependencyCheck = true,
            Targets = new[]
            {
                new ManagedModuleUninstallTarget
                {
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    ModuleRoot = "/tmp/modules",
                    ModulePath = "/tmp/Modules/Company.Tools/1.0.0"
                }
            }
        };
        var service = new ManagedModuleUninstallService();

        var exception = Assert.Throws<InvalidOperationException>(() => service.Uninstall(plan));

        Assert.Contains("outside module root", exception.Message);
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
        string? requiredVersion = null,
        string? requiredModuleVersion = null,
        string? requiredModuleGuid = null,
        string? moduleGuid = null)
    {
        var modulePath = Path.Combine(root, name, version);
        Directory.CreateDirectory(modulePath);
        WriteModuleFiles(modulePath, name, version, requiredModuleName, requiredVersion, requiredModuleVersion, requiredModuleGuid, moduleGuid);
    }

    private static void CreateFlatInstalledModule(
        string root,
        string name,
        string version,
        string? moduleGuid = null)
    {
        var modulePath = Path.Combine(root, name);
        Directory.CreateDirectory(modulePath);
        WriteModuleFiles(modulePath, name, version, requiredModuleName: null, requiredVersion: null, requiredModuleVersion: null, requiredModuleGuid: null, moduleGuid);
        Directory.CreateDirectory(Path.Combine(modulePath, "en-US"));
        File.WriteAllText(Path.Combine(modulePath, "en-US", "about_Company.Tools.help.txt"), string.Empty);
    }

    private static void WriteModuleFiles(
        string modulePath,
        string name,
        string version,
        string? requiredModuleName,
        string? requiredVersion,
        string? requiredModuleVersion,
        string? requiredModuleGuid,
        string? moduleGuid)
    {
        var prereleaseIndex = version.IndexOf('-');
        var moduleVersion = prereleaseIndex >= 0 ? version.Substring(0, prereleaseIndex) : version;
        var prerelease = prereleaseIndex >= 0 ? version.Substring(prereleaseIndex + 1) : null;
        var guid = string.IsNullOrWhiteSpace(moduleGuid)
            ? string.Empty
            : $@"
    GUID = '{moduleGuid}'";
        var requiredModules = string.IsNullOrWhiteSpace(requiredModuleName)
            ? string.Empty
            : $@"
    RequiredModules = @(
        @{{
            ModuleName = '{requiredModuleName}'
            {FormatRequiredModuleVersion(requiredVersion, requiredModuleVersion)}
            {FormatRequiredModuleGuid(requiredModuleGuid)}
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
    ModuleVersion = '{{moduleVersion}}'{{guid}}{{requiredModules}}{{privateData}}
}
""");
        File.WriteAllText(Path.Combine(modulePath, name + ".psm1"), string.Empty);
    }

    private static string FormatRequiredModuleVersion(string? requiredVersion, string? requiredModuleVersion)
        => !string.IsNullOrWhiteSpace(requiredModuleVersion)
            ? "ModuleVersion = '" + requiredModuleVersion + "'"
            : "RequiredVersion = '" + requiredVersion + "'";

    private static string FormatRequiredModuleGuid(string? requiredModuleGuid)
        => string.IsNullOrWhiteSpace(requiredModuleGuid)
            ? string.Empty
            : "Guid = '" + requiredModuleGuid + "'";

    private static string EscapePowerShellSingleQuoted(string value)
        => value.Replace("'", "''");

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
