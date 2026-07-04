using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleUninstallServiceTests
{
    [Fact]
    public void Uninstall_without_version_removes_latest_stable_version()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.2.0-beta1");
        var service = new ManagedModuleUninstallService();

        var results = service.Uninstall(service.PlanUninstall(CreateRequest(moduleRoot.Path, "Company.Tools")));

        var result = Assert.Single(results);
        Assert.Equal(ManagedModuleUninstallStatus.Uninstalled, result.Status);
        Assert.Equal("1.1.0", result.Version);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0")));
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.2.0-beta1")));
    }

    [Fact]
    public void PlanUninstall_without_version_and_prerelease_selects_latest_prerelease_version()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0-beta1");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.2.0-beta1");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.Tools");
        request.Prerelease = true;

        var plan = service.PlanUninstall(request);

        Assert.Equal("1.2.0-beta1", Assert.Single(plan.Targets).Version);
    }

    [Fact]
    public void Uninstall_exact_version_removes_only_requested_version()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.Tools");
        request.Version = "1.0";

        var results = service.Uninstall(service.PlanUninstall(request));

        Assert.Equal("1.0.0", Assert.Single(results).Version);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0")));
    }

    [Fact]
    public void PlanUninstall_wildcard_and_range_selects_matching_versions()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Core", "1.1.0");
        CreateInstalledModule(moduleRoot.Path, "Other.Tools", "1.1.0");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.*");
        request.Version = "[1.1.0,2.0.0)";

        var plan = service.PlanUninstall(request);

        Assert.Equal(new[] { "Company.Core:1.1.0", "Company.Tools:1.1.0" },
            plan.Targets
                .Select(static target => target.Name + ":" + target.Version)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanUninstall_blocks_removal_when_remaining_module_requires_target()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Core", "1.0.0");
        CreateInstalledModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '1.0.0' })");
        var service = new ManagedModuleUninstallService();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.PlanUninstall(CreateRequest(moduleRoot.Path, "Company.Core")));

        Assert.Contains("Company.Core 1.0.0 is required by Company.Tools 1.0.0", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SkipDependencyCheck", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Uninstall_skip_dependency_check_permits_required_module_removal()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Core", "1.0.0");
        CreateInstalledModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '1.0.0' })");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.Core");
        request.SkipDependencyCheck = true;

        var result = Assert.Single(service.Uninstall(service.PlanUninstall(request)));

        Assert.Equal(ManagedModuleUninstallStatus.Uninstalled, result.Status);
        Assert.True(result.DependencyCheckSkipped);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Core")));
    }

    [Fact]
    public void PlanUninstall_blocks_loaded_module_uninstall_by_default()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.Tools");
        request.LoadedModules = new[]
        {
            new ManagedModuleLoadedModule
            {
                Name = "Company.Tools",
                Version = "1.0.0",
                ModuleBase = modulePath
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => service.PlanUninstall(request));

        Assert.Contains("AllowLoadedModuleUninstall", exception.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(modulePath));
    }

    [Fact]
    public void PlanUninstall_allows_loaded_module_uninstall_when_explicitly_enabled()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.Tools");
        request.AllowLoadedModuleUninstall = true;
        request.LoadedModules = new[]
        {
            new ManagedModuleLoadedModule
            {
                Name = "Company.Tools",
                Version = "1.0.0",
                ModuleBase = modulePath
            }
        };

        var target = Assert.Single(service.PlanUninstall(request).Targets);

        Assert.True(target.IsLoaded);
    }

    [Fact]
    public void Uninstall_empty_directory_cleanup_stays_inside_plan_root()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        var modulePath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var outsideModuleDirectory = Path.Combine(outsideRoot.Path, "Company.Tools");
        Directory.CreateDirectory(outsideModuleDirectory);
        var service = new ManagedModuleUninstallService();
        var plan = new ManagedModuleUninstallPlan
        {
            Name = new[] { "Company.Tools" },
            ModuleRoot = moduleRoot.Path,
            SkipDependencyCheck = true,
            Targets = new[]
            {
                new ManagedModuleUninstallTarget
                {
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    ModuleRoot = outsideRoot.Path,
                    ModulePath = modulePath
                }
            }
        };

        var result = Assert.Single(service.Uninstall(plan));

        Assert.Equal(moduleRoot.Path, result.ModuleRoot);
        Assert.False(Directory.Exists(modulePath));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools")));
        Assert.True(Directory.Exists(outsideModuleDirectory));
    }

    private static ManagedModuleUninstallRequest CreateRequest(string moduleRoot, string name)
        => new()
        {
            Name = new[] { name },
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot
        };

    private static string CreateInstalledModule(
        string root,
        string name,
        string version,
        string? requiredModules = null)
    {
        var modulePath = Path.Combine(root, name, version);
        Directory.CreateDirectory(modulePath);
        var versionParts = version.Split('-', 2);
        var prerelease = versionParts.Length == 2
            ? "    PrivateData = @{" + Environment.NewLine +
              "        PSData = @{" + Environment.NewLine +
              $"            Prerelease = '{versionParts[1]}'" + Environment.NewLine +
              "        }" + Environment.NewLine +
              "    }"
            : string.Empty;
        File.WriteAllText(Path.Combine(modulePath, name + ".psd1"), $$"""
@{
    RootModule = '{{name}}.psm1'
    ModuleVersion = '{{versionParts[0]}}'
{{requiredModules}}
{{prerelease}}
}
""");
        File.WriteAllText(Path.Combine(modulePath, name + ".psm1"), string.Empty);
        return modulePath;
    }
}
