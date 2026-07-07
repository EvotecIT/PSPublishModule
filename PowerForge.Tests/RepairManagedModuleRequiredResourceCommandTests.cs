using System.Collections;
using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class RepairManagedModuleRequiredResourceCommandTests
{
    [Fact]
    public void RepairManagedModule_RequiredResourcePlanInstallsMissingModule()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0"
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("Install", action.Kind);
        Assert.Equal("Company.Tools", action.ModuleName);
        Assert.Equal("=1.1.0", action.VersionPolicy);
        Assert.Equal("Local", action.TargetRepository);
        var command = Assert.Single(result.Apply.Commands);
        Assert.Equal("Install-ManagedModule", command.CommandName);
        Assert.Contains("-RequiredVersion", command.Arguments);
        Assert.Contains("1.1.0", command.Arguments);
        Assert.Contains("-ModuleRoot", command.Arguments);
        Assert.Contains(moduleRoot.Path, command.Arguments);
        Assert.False(result.Apply.ExecutionRequested);
        Assert.Empty(result.Apply.ExecutionResults);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourcePreservesPerResourceDeliveryOptions()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0",
                ["Prerelease"] = true,
                ["Reinstall"] = true,
                ["AcceptLicense"] = true,
                ["AllowClobber"] = true,
                ["SkipDependencyCheck"] = true
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("Install", action.Kind);
        Assert.True(action.IncludePrerelease);
        Assert.True(action.Force);
        Assert.True(action.AcceptLicense);
        Assert.True(action.AllowClobber);
        Assert.True(action.SkipDependencyCheck);
        var command = Assert.Single(result.Apply.Commands);
        Assert.Contains("-Prerelease", command.Arguments);
        Assert.Contains("-Force", command.Arguments);
        Assert.Contains("-AllowClobber", command.Arguments);
        Assert.Contains("-SkipDependencyCheck", command.Arguments);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceHonorsExplicitCurrentUserScope()
    {
        using var feed = new TemporaryDirectory();
        var inventory = new ModuleStateInventoryResult
        {
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "1.1.0",
                    Scope = "AllUsers",
                    Path = @"C:\Program Files\WindowsPowerShell\Modules\Company.Tools\1.1.0",
                    IsEffectiveImportCandidate = true
                }
            }
        };
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0",
                ["Scope"] = "CurrentUser"
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("Inventory", inventory)
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("Install", action.Kind);
        Assert.Equal("CurrentUser", action.TargetScope);
        Assert.Null(action.TargetPath);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceNormalizesPerResourceRepository()
    {
        using var feed = new TemporaryDirectory();
        var inventory = new ModuleStateInventoryResult
        {
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "1.1.0",
                    Scope = "CurrentUser",
                    SourceRepository = "Local",
                    Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Modules", "Company.Tools", "1.1.0"),
                    IsEffectiveImportCandidate = true
                }
            }
        };
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0",
                ["Repository"] = feed.Path
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("Inventory", inventory)
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("NoAction", action.Kind);
        Assert.Equal("Local", action.TargetRepository);
        Assert.Equal(feed.Path, action.TargetRepositorySource);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceNormalizesBareRelativeRepositoryPath()
    {
        using var root = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var feed = Path.Combine(root.Path, "feed");
        Directory.CreateDirectory(feed);
        var inventory = new ModuleStateInventoryResult
        {
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "1.1.0",
                    Scope = "CurrentUser",
                    SourceRepository = "Local",
                    Path = Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0"),
                    IsEffectiveImportCandidate = true
                }
            }
        };
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0",
                ["Repository"] = "feed"
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Set-Location")
            .AddParameter("LiteralPath", root.Path);
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();

        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("Inventory", inventory)
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("NoAction", action.Kind);
        Assert.Equal("Local", action.TargetRepository);
        Assert.Equal(feed, action.TargetRepositorySource);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceUsesPerResourceRepositorySourceForDelivery()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0",
                ["Repository"] = feed.Path
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("Install", action.Kind);
        Assert.Equal("Local", action.TargetRepository);
        Assert.Equal(feed.Path, action.TargetRepositorySource);
        var command = Assert.Single(result.Apply.Commands);
        Assert.Contains("-Repository", command.Arguments);
        Assert.Contains(feed.Path, command.Arguments);
    }

    [Fact]
    public void RepairManagedModule_EmptyRequiredResourceDoesNotFallBackToInstalledModules()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", new Hashtable(StringComparer.OrdinalIgnoreCase))
            .AddParameter("Latest")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Empty(result.Plan.Actions);
        Assert.Empty(result.Apply.Commands);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceLatestDoesNotUpdateExactPinnedResource()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0");
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0"
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Latest")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("NoAction", action.Kind);
        Assert.Equal("=1.1.0", action.VersionPolicy);
        Assert.Empty(result.Apply.Commands);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceUsesTopLevelConstraintWhenEntryOmitsVersion()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("MinimumVersion", "2.0.0")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("Update", action.Kind);
        Assert.Equal(">=2.0.0", action.VersionPolicy);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceWildcardVersionUsesAnyPolicy()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "*"
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("Install", action.Kind);
        Assert.Equal("*", action.VersionPolicy);
        var command = Assert.Single(result.Apply.Commands);
        Assert.DoesNotContain("-RequiredVersion", command.Arguments);
        Assert.DoesNotContain("-VersionPolicy", command.Arguments);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceTrailingWildcardVersionUsesRangePolicy()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.*"
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("Install", action.Kind);
        Assert.Equal("[1.0.0,2.0.0)", action.VersionPolicy);
        var command = Assert.Single(result.Apply.Commands);
        Assert.Contains("-VersionPolicy", command.Arguments);
        Assert.Contains("[1.0.0,2.0.0)", command.Arguments);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceNoClobberOverridesGlobalAllowClobber()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0",
                ["NoClobber"] = true
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Repository", feed.Path)
            .AddParameter("AllowClobber")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.False(action.AllowClobber);
        var command = Assert.Single(result.Apply.Commands);
        Assert.DoesNotContain("-AllowClobber", command.Arguments);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceFiltersBaselineByName()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0"
            },
            ["Company.Other"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "2.0.0"
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("Company.Tools", action.ModuleName);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceOptOutsOverrideGlobalDefaults()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0");
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0",
                ["Reinstall"] = false,
                ["SkipDependencyCheck"] = false
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Force")
            .AddParameter("SkipDependencyCheck")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("NoAction", action.Kind);
        Assert.False(action.Force);
        Assert.False(action.SkipDependencyCheck);
        Assert.Empty(result.Apply.Commands);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourcePrivateTransportUsesRepositorySourceAndOptions()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0",
                ["Repository"] = feed.Path,
                ["Prerelease"] = true,
                ["Reinstall"] = true,
                ["AllowClobber"] = true,
                ["SkipDependencyCheck"] = true
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Transport", "PrivateModule")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var command = Assert.Single(result.Apply.Commands);
        Assert.Equal("Repair-ManagedModule", command.CommandName);
        Assert.Contains("-Repository", command.Arguments);
        Assert.Contains(feed.Path, command.Arguments);
        Assert.Contains("-Prerelease", command.Arguments);
        Assert.Contains("-Force", command.Arguments);
        Assert.Contains("-AllowClobber", command.Arguments);
        Assert.Contains("-SkipDependencyCheck", command.Arguments);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceAppliesMissingInstallToInventoriedRoot()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("Company.Tools", "1.1.0"));
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0"
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Repository", feed.Path);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(result.Apply.ExecutionRequested);
        var execution = Assert.Single(result.Apply.ExecutionResults);
        Assert.Equal("Install", execution.Operation);
        Assert.True(execution.OperationPerformed);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceLatestPlansUpdateForInstalledBaseline()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("Company.Tools", "1.1.0"));
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Latest")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("Update", action.Kind);
        Assert.Equal("*", action.VersionPolicy);
        var command = Assert.Single(result.Apply.Commands);
        Assert.Equal("Update-ManagedModule", command.CommandName);
        Assert.Contains("-ModuleRoot", command.Arguments);
        Assert.Contains(moduleRoot.Path, command.Arguments);
        Assert.False(result.Apply.ExecutionRequested);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceFileCanPlanMissingInstallAndCleanup()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var oldPath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var currentPath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.1.0");
        var resourceFile = Path.Combine(moduleRoot.Path, "required-resources.psd1");
        File.WriteAllText(
            resourceFile,
            "@{ 'Company.Tools' = @{ Version = '1.1.0' }; 'Company.Missing' = @{ Version = '2.0.0' } }");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResourceFile", resourceFile)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Contains(result.Plan.Actions, static action =>
            string.Equals(action.ModuleName, "Company.Missing", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Kind, "Install", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Plan.Actions, static action =>
            string.Equals(action.ModuleName, "Company.Tools", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Kind, "Remove", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.InstalledVersion, "1.0.0", StringComparison.OrdinalIgnoreCase));
        Assert.True(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.False(result.Apply.ExecutionRequested);
        Assert.Empty(result.Apply.ExecutionResults);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceJsonFileCanPlanMissingInstall()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var resourceFile = Path.Combine(moduleRoot.Path, "required-resources.json");
        File.WriteAllText(
            resourceFile,
            "{ \"Company.Tools\": { \"Version\": \"1.1.0\", \"Prerelease\": true } }");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResourceFile", resourceFile)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("Install", action.Kind);
        Assert.Equal("Company.Tools", action.ModuleName);
        Assert.Equal("=1.1.0", action.VersionPolicy);
        Assert.True(action.IncludePrerelease);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceJsonStringCanPlanMissingInstall()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var requiredResource = "{ \"Company.Tools\": { \"Version\": \"1.1.0\", \"Prerelease\": true } }";

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("Install", action.Kind);
        Assert.Equal("Company.Tools", action.ModuleName);
        Assert.Equal("=1.1.0", action.VersionPolicy);
        Assert.True(action.IncludePrerelease);
    }

    private static string CreateInstalledModule(string moduleRoot, string name, string version)
    {
        var modulePath = Path.Combine(moduleRoot, name, version);
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(
            Path.Combine(modulePath, name + ".psd1"),
            "@{ RootModule = '" + name + ".psm1'; ModuleVersion = '" + version + "' }");
        File.WriteAllText(Path.Combine(modulePath, name + ".psm1"), string.Empty);
        return modulePath;
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string name, string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [name + ".psd1"] = "@{ RootModule = '" + name + ".psm1'; ModuleVersion = '" + version + "' }",
            [name + ".psm1"] = string.Empty
        };

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(RepairManagedModuleCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
