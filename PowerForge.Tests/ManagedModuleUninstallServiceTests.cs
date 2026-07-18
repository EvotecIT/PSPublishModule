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
    public void PlanUninstall_exact_stable_version_with_prerelease_switch_keeps_stable_match()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0-beta1");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.Tools");
        request.Version = "1.0.0";
        request.Prerelease = true;

        var plan = service.PlanUninstall(request);

        Assert.Equal("1.0.0", Assert.Single(plan.Targets).Version);
    }

    [Theory]
    [InlineData(">oops")]
    [InlineData(">=1.0.0 <oops")]
    [InlineData("[oops,2.0.0)")]
    [InlineData("1.0.0,2.0.0")]
    [InlineData(">=1.0.0 >1.1.0")]
    [InlineData("<=2.0.0 <3.0.0")]
    [InlineData("[1.0.0,2.0.0,3.0.0]")]
    public void PlanUninstall_rejects_non_version_range_operands(string versionRange)
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.Tools");
        request.Version = versionRange;

        var exception = Assert.Throws<ArgumentException>(() => service.PlanUninstall(request));

        Assert.Contains("Invalid version range syntax", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public void PlanUninstall_exact_bracket_range_selects_requested_version()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.Tools");
        request.Version = "[1.0.0]";

        var plan = service.PlanUninstall(request);

        Assert.Equal("1.0.0", Assert.Single(plan.Targets).Version);
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
    public void PlanUninstall_blocks_removal_when_module_in_another_visible_root_requires_target()
    {
        using var targetRoot = new TemporaryDirectory();
        using var dependentRoot = new TemporaryDirectory();
        var targetPath = CreateInstalledModule(targetRoot.Path, "Company.Core", "1.0.0");
        var dependentPath = CreateInstalledModule(
            dependentRoot.Path,
            "Company.Tools",
            "1.0.0",
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '1.0.0' })");
        var request = CreateRequest(targetRoot.Path, "Company.Core");
        request.DependencyModuleRoots = new[] { targetRoot.Path, dependentRoot.Path };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ManagedModuleUninstallService().PlanUninstall(request));

        Assert.Contains("Company.Core 1.0.0 is required by Company.Tools 1.0.0", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(targetPath));
        Assert.True(Directory.Exists(dependentPath));
    }

    [Fact]
    public void Uninstall_fails_closed_when_preflight_dependency_root_disappears()
    {
        using var targetRoot = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var dependencyRoot = Path.Combine(workspace.Path, "visible-dependencies");
        Directory.CreateDirectory(dependencyRoot);
        var targetPath = CreateInstalledModule(targetRoot.Path, "Company.Core", "1.0.0");
        var request = CreateRequest(targetRoot.Path, "Company.Core");
        request.DependencyModuleRoots = new[] { targetRoot.Path, dependencyRoot };
        request.DeferDependencyCheck = true;
        var service = new ManagedModuleUninstallService();
        var plan = service.PlanUninstall(request);
        Directory.Delete(dependencyRoot);

        var exception = Assert.Throws<InvalidOperationException>(() => service.Uninstall(plan));

        Assert.Contains("no longer available", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(targetPath));
    }

    [Fact]
    public void Uninstall_validates_dependency_alternatives_within_each_visible_root_group()
    {
        using var globalRoot = new TemporaryDirectory();
        using var profileARoot = new TemporaryDirectory();
        using var profileBRoot = new TemporaryDirectory();
        var targetPath = CreateInstalledModule(globalRoot.Path, "Company.Core", "1.0.0");
        CreateInstalledModule(
            profileARoot.Path,
            "Company.ProfileTools",
            "1.0.0",
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '1.0.0' })");
        CreateInstalledModule(profileBRoot.Path, "Company.Core", "1.0.0");
        var request = CreateRequest(globalRoot.Path, "Company.Core");
        request.Version = "1.0.0";
        request.DependencyModuleRoots = new[] { globalRoot.Path, profileARoot.Path, profileBRoot.Path };
        request.DependencyModuleRootGroups = new IReadOnlyList<string>[]
        {
            new[] { globalRoot.Path, profileARoot.Path },
            new[] { globalRoot.Path, profileBRoot.Path }
        };
        var service = new ManagedModuleUninstallService();

        var exception = Assert.Throws<InvalidOperationException>(() => service.PlanUninstall(request));

        Assert.Contains("required by Company.ProfileTools", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(targetPath));
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
    public void Uninstall_deferred_dependency_check_allows_safe_confirmed_subset()
    {
        using var moduleRoot = new TemporaryDirectory();
        var blockedPath = CreateInstalledModule(moduleRoot.Path, "Company.Core", "1.0.0");
        var safePath = CreateInstalledModule(moduleRoot.Path, "Company.Safe", "1.0.0");
        var dependentPath = CreateInstalledModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '1.0.0' })");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.*");
        request.DeferDependencyCheck = true;
        var plan = service.PlanUninstall(request);
        var selectedPlan = new ManagedModuleUninstallPlan
        {
            Name = plan.Name,
            Version = plan.Version,
            ModuleRoot = plan.ModuleRoot,
            SkipDependencyCheck = plan.SkipDependencyCheck,
            AllowLoadedModuleUninstall = plan.AllowLoadedModuleUninstall,
            Targets = plan.Targets.Where(static target => target.Name == "Company.Safe").ToArray()
        };

        var result = Assert.Single(service.Uninstall(selectedPlan));

        Assert.Equal("Company.Safe", result.Name);
        Assert.False(Directory.Exists(safePath));
        Assert.True(Directory.Exists(blockedPath));
        Assert.True(Directory.Exists(dependentPath));
    }

    [Fact]
    public void Uninstall_deferred_dependency_check_still_blocks_confirmed_dependency_target()
    {
        using var moduleRoot = new TemporaryDirectory();
        var blockedPath = CreateInstalledModule(moduleRoot.Path, "Company.Core", "1.0.0");
        var dependentPath = CreateInstalledModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '1.0.0' })");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.*");
        request.DeferDependencyCheck = true;
        var plan = service.PlanUninstall(request);
        var selectedPlan = new ManagedModuleUninstallPlan
        {
            Name = plan.Name,
            Version = plan.Version,
            ModuleRoot = plan.ModuleRoot,
            SkipDependencyCheck = plan.SkipDependencyCheck,
            AllowLoadedModuleUninstall = plan.AllowLoadedModuleUninstall,
            Targets = plan.Targets.Where(static target => target.Name == "Company.Core").ToArray()
        };

        var exception = Assert.Throws<InvalidOperationException>(() => service.Uninstall(selectedPlan));

        Assert.Contains("Company.Core 1.0.0 is required by Company.Tools 1.0.0", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(blockedPath));
        Assert.True(Directory.Exists(dependentPath));
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
    public void RefreshLoadedState_recomputes_stale_uninstall_targets()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.Tools");
        request.DeferLoadedModuleCheck = true;
        var plan = service.PlanUninstall(request);
        var target = Assert.Single(plan.Targets);
        Assert.False(target.IsLoaded);

        ManagedModuleUninstallService.RefreshLoadedState(plan.Targets, new[]
        {
            new ManagedModuleLoadedModule
            {
                Name = "Company.Tools",
                Version = "1.0.0",
                ModuleBase = modulePath
            }
        });

        Assert.True(target.IsLoaded);
        var exception = Assert.Throws<InvalidOperationException>(() => service.ValidateUninstallPlan(plan));
        Assert.Contains("AllowLoadedModuleUninstall", exception.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(modulePath));
    }

    [Fact]
    public void Uninstall_deferred_loaded_check_allows_unloaded_confirmed_subset()
    {
        using var moduleRoot = new TemporaryDirectory();
        var loadedPath = CreateInstalledModule(moduleRoot.Path, "Company.Loaded", "1.0.0");
        var safePath = CreateInstalledModule(moduleRoot.Path, "Company.Safe", "1.0.0");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.*");
        request.DeferLoadedModuleCheck = true;
        request.LoadedModules = new[]
        {
            new ManagedModuleLoadedModule
            {
                Name = "Company.Loaded",
                Version = "1.0.0",
                ModuleBase = loadedPath
            }
        };
        var plan = service.PlanUninstall(request);
        var selectedPlan = new ManagedModuleUninstallPlan
        {
            Name = plan.Name,
            Version = plan.Version,
            ModuleRoot = plan.ModuleRoot,
            SkipDependencyCheck = plan.SkipDependencyCheck,
            AllowLoadedModuleUninstall = plan.AllowLoadedModuleUninstall,
            Targets = plan.Targets.Where(static target => target.Name == "Company.Safe").ToArray()
        };

        var result = Assert.Single(service.Uninstall(selectedPlan));

        Assert.Equal("Company.Safe", result.Name);
        Assert.False(Directory.Exists(safePath));
        Assert.True(Directory.Exists(loadedPath));
    }

    [Fact]
    public void Uninstall_deferred_loaded_check_still_blocks_confirmed_loaded_target()
    {
        using var moduleRoot = new TemporaryDirectory();
        var loadedPath = CreateInstalledModule(moduleRoot.Path, "Company.Loaded", "1.0.0");
        var service = new ManagedModuleUninstallService();
        var request = CreateRequest(moduleRoot.Path, "Company.Loaded");
        request.DeferLoadedModuleCheck = true;
        request.LoadedModules = new[]
        {
            new ManagedModuleLoadedModule
            {
                Name = "Company.Loaded",
                Version = "1.0.0",
                ModuleBase = loadedPath
            }
        };
        var plan = service.PlanUninstall(request);

        var exception = Assert.Throws<InvalidOperationException>(() => service.Uninstall(plan));

        Assert.Contains("AllowLoadedModuleUninstall", exception.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(loadedPath));
    }

    [Fact]
    public void Uninstall_rejects_target_equal_to_module_root()
    {
        using var moduleRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(moduleRoot.Path, "sentinel.txt"), "keep");
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
                    ModuleRoot = moduleRoot.Path,
                    ModulePath = moduleRoot.Path
                }
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => service.Uninstall(plan));

        Assert.Contains("module root", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "sentinel.txt")));
    }

    [Fact]
    public void Uninstall_rejects_empty_module_root_before_target_cleanup()
    {
        using var moduleRoot = new TemporaryDirectory();
        var targetPath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var service = new ManagedModuleUninstallService();
        var plan = new ManagedModuleUninstallPlan
        {
            Name = new[] { "Company.Tools" },
            ModuleRoot = string.Empty,
            SkipDependencyCheck = true,
            Targets = new[]
            {
                new ManagedModuleUninstallTarget
                {
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    ModuleRoot = moduleRoot.Path,
                    ModulePath = targetPath
                }
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => service.Uninstall(plan));

        Assert.Contains("module root is empty", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(targetPath));
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
