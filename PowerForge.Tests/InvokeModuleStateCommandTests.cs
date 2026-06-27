using System;
using System.Reflection;
using System.Management.Automation;
using System.Collections;
using System.IO;
using System.Linq;
using PSPublishModule;
using Xunit;

namespace PowerForge.Tests;

public sealed class InvokeModuleStateCommandTests
{
    [Fact]
    public void ResolveDesiredState_RejectsLatestWithExplicitVersionPolicy()
    {
        var command = new InvokeModuleStateCommand
        {
            ModuleName = new[] { "Company.Tools" },
            Latest = new SwitchParameter(true),
            RequiredVersion = "1.2.0"
        };
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Test",
            InstalledModules = Array.Empty<ModuleStateInstalledModuleResult>()
        };

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeResolveDesiredState(command, inventory));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Latest cannot be combined", exception.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDesiredState_UsesIdempotentLatestPolicy()
    {
        var command = new InvokeModuleStateCommand
        {
            ModuleName = new[] { "Company.Tools" },
            Latest = new SwitchParameter(true)
        };
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Test",
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "1.2.0",
                    IsEffectiveImportCandidate = true
                }
            }
        };

        var desired = Assert.IsType<Hashtable>(InvokeResolveDesiredState(command, inventory));
        var modules = Assert.IsAssignableFrom<IEnumerable>(desired["Modules"]);
        var module = Assert.IsType<Hashtable>(modules.Cast<object>().Single());

        Assert.Equal("*", module["VersionPolicy"]);
    }

    [Fact]
    public void ResolveDesiredState_IncludesSavePathForConvenienceModules()
    {
        var command = new InvokeModuleStateCommand
        {
            ModuleName = new[] { "Company.Tools" },
            SavePath = @"C:\OfflineModules"
        };
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Test",
            InstalledModules = Array.Empty<ModuleStateInstalledModuleResult>()
        };

        var desired = Assert.IsType<Hashtable>(InvokeResolveDesiredState(command, inventory));
        var modules = Assert.IsAssignableFrom<IEnumerable>(desired["Modules"]);
        var module = Assert.IsType<Hashtable>(modules.Cast<object>().Single());

        Assert.Equal(@"C:\OfflineModules", module["Path"]);
    }


    [Fact]
    public void ApplyLatestUpdateIntent_ConvertsNoActionToUpdate()
    {
        var command = new InvokeModuleStateCommand
        {
            Latest = new SwitchParameter(true)
        };
        var plan = new ModuleStatePlanResult
        {
            Actions = new[]
            {
                new ModuleStatePlanActionResult
                {
                    Kind = "NoAction",
                    ModuleName = "Company.Tools",
                    InstalledVersion = "1.2.0",
                    VersionPolicy = "*",
                    Reason = "satisfied"
                }
            }
        };

        InvokeApplyLatestUpdateIntent(command, plan);

        var action = Assert.Single(plan.Actions);
        Assert.Equal("Update", action.Kind);
        Assert.Equal("*", action.VersionPolicy);
        Assert.Contains("Latest requested", action.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HasFailedExecutionResult_DetectsFailedDependency()
    {
        var executionResults = new[]
        {
            new ModuleStateDeliveryExecutionResult
            {
                DependencyResults = new[]
                {
                    new ModuleStateDependencyResult
                    {
                        Name = "Company.Tools",
                        Status = "Failed"
                    }
                }
            }
        };

        Assert.True(InvokeHasFailedExecutionResult(executionResults));
    }

    [Fact]
    public void HasSkippedExecutionResult_DetectsUnperformedDelivery()
    {
        var executionResults = new[]
        {
            new ModuleStateDeliveryExecutionResult
            {
                Operation = "Install",
                OperationPerformed = false
            }
        };

        Assert.True(InvokeHasSkippedExecutionResult(executionResults));
    }

    [Fact]
    public void PlanCommandHasSkippedExecutionResult_DetectsUnperformedDelivery()
    {
        var executionResults = new[]
        {
            new ModuleStateDeliveryExecutionResult
            {
                Operation = "Install",
                OperationPerformed = false
            }
        };

        Assert.True(InvokePlanCommandHasSkippedExecutionResult(executionResults));
    }

    [Fact]
    public void ResolveDesiredState_UsesProfileRepositoryForConvenienceModules()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var userPath = Path.Combine(root, "profiles.json");
        var previousUserPath = Environment.GetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH");
        var previousMachinePath = Environment.GetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH");
        try
        {
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH", userPath);
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH", Path.Combine(root, "machine.json"));
            new ModuleRepositoryProfileStore(userPath).SaveProfile(new ModuleRepositoryProfile
            {
                Name = "Company",
                AzureDevOpsOrganization = "contoso",
                AzureArtifactsFeed = "Modules",
                RepositoryName = "CompanyModules"
            });
            var command = new InvokeModuleStateCommand
            {
                ModuleName = new[] { "Company.Tools" },
                RequiredVersion = "1.2.0",
                ProfileName = "Company"
            };
            var inventory = new ModuleStateInventoryResult
            {
                Source = "Test",
                InstalledModules = Array.Empty<ModuleStateInstalledModuleResult>()
            };

            var desired = Assert.IsType<Hashtable>(InvokeResolveDesiredState(command, inventory));
            var modules = Assert.IsAssignableFrom<IEnumerable>(desired["Modules"]);
            var module = Assert.IsType<Hashtable>(modules.Cast<object>().Single());

            Assert.Equal("CompanyModules", module["Repository"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH", previousUserPath);
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH", previousMachinePath);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveDesiredState_UsesProfileRepositoryForInstalledBaseline()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var userPath = Path.Combine(root, "profiles.json");
        var previousUserPath = Environment.GetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH");
        var previousMachinePath = Environment.GetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH");
        try
        {
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH", userPath);
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH", Path.Combine(root, "machine.json"));
            new ModuleRepositoryProfileStore(userPath).SaveProfile(new ModuleRepositoryProfile
            {
                Name = "Company",
                AzureDevOpsOrganization = "contoso",
                AzureArtifactsFeed = "Modules",
                RepositoryName = "CompanyModules"
            });
            var command = new InvokeModuleStateCommand
            {
                Installed = new SwitchParameter(true),
                ProfileName = "Company"
            };
            var inventory = new ModuleStateInventoryResult
            {
                Source = "Test",
                InstalledModules = new[]
                {
                    new ModuleStateInstalledModuleResult
                    {
                        Name = "Company.Tools",
                        Version = "1.2.0",
                        IsEffectiveImportCandidate = true
                    }
                }
            };

            var desired = Assert.IsType<Hashtable>(InvokeCreateDesiredStateForInstalledModules(command, inventory));
            var modules = Assert.IsAssignableFrom<IEnumerable>(desired["Modules"]);
            var module = Assert.IsType<Hashtable>(modules.Cast<object>().Single());

            Assert.Equal("CompanyModules", module["Repository"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH", previousUserPath);
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH", previousMachinePath);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateDesiredStateForInstalledModules_PreservesScopedCopiesWhenScopeIsNotSpecified()
    {
        var command = new InvokeModuleStateCommand();
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Test",
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "1.2.0",
                    Scope = "CurrentUser",
                    IsEffectiveImportCandidate = true
                },
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "1.1.0",
                    Scope = "AllUsers",
                    IsEffectiveImportCandidate = true
                }
            }
        };

        var desired = Assert.IsType<Hashtable>(InvokeCreateDesiredStateForInstalledModules(command, inventory));
        var modules = Assert.IsAssignableFrom<IEnumerable>(desired["Modules"]).Cast<Hashtable>().ToArray();

        Assert.Equal(2, modules.Length);
        Assert.Contains(modules, static module => (string)module["Name"]! == "Company.Tools" && (string)module["Scope"]! == "CurrentUser");
        Assert.Contains(modules, static module => (string)module["Name"]! == "Company.Tools" && (string)module["Scope"]! == "AllUsers");
    }

    [Fact]
    public void InvokeModuleStatePlan_ManagedTransport_InstallsToCustomModuleRoot()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var plan = new ModuleStatePlanResult
        {
            Actions = new[]
            {
                new ModuleStatePlanActionResult
                {
                    Kind = "Install",
                    ModuleName = "Company.Tools",
                    VersionPolicy = "=1.0.0",
                    Reason = "missing"
                }
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Invoke-ModuleStatePlan")
            .AddParameter("Plan", plan)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Transport", ModuleStateDeliveryTransport.ManagedModule)
            .AddParameter("ModuleRoot", moduleRoot.Path)
            .AddParameter("Execute");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<PSPublishModule.ModuleStateApplyResult>(Assert.Single(results).BaseObject);
        Assert.True(result.ExecutionRequested);
        var execution = Assert.Single(result.ExecutionResults);
        Assert.Equal("Install", execution.Operation);
        Assert.True(execution.OperationPerformed);
        var dependency = Assert.Single(execution.DependencyResults);
        Assert.Equal("ManagedModule", dependency.Installer);
        Assert.Equal("Installed", dependency.Status);
        Assert.Equal("1.0.0", dependency.ResolvedVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InvokeModuleStatePlan_ManagedTransport_UpdatesToCustomModuleRoot()
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
        File.WriteAllText(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1"), "@{ ModuleVersion = '1.0.0' }");
        var plan = new ModuleStatePlanResult
        {
            Actions = new[]
            {
                new ModuleStatePlanActionResult
                {
                    Kind = "Update",
                    ModuleName = "Company.Tools",
                    InstalledVersion = "1.0.0",
                    VersionPolicy = "*",
                    Reason = "latest requested"
                }
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Invoke-ModuleStatePlan")
            .AddParameter("Plan", plan)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Transport", ModuleStateDeliveryTransport.ManagedModule)
            .AddParameter("ModuleRoot", moduleRoot.Path)
            .AddParameter("Execute");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<PSPublishModule.ModuleStateApplyResult>(Assert.Single(results).BaseObject);
        var execution = Assert.Single(result.ExecutionResults);
        Assert.Equal("Update", execution.Operation);
        Assert.True(execution.OperationPerformed);
        var dependency = Assert.Single(execution.DependencyResults);
        Assert.Equal("ManagedModule", dependency.Installer);
        Assert.Equal("Updated", dependency.Status);
        Assert.Equal("1.0.0", dependency.InstalledVersion);
        Assert.Equal("1.1.0", dependency.ResolvedVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InvokeModuleStatePlan_ManagedTransport_SavesToTargetPath()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var plan = new ModuleStatePlanResult
        {
            Actions = new[]
            {
                new ModuleStatePlanActionResult
                {
                    Kind = "Save",
                    ModuleName = "Company.Tools",
                    VersionPolicy = "=1.0.0",
                    Reason = "missing saved copy",
                    TargetPath = moduleRoot.Path
                }
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Invoke-ModuleStatePlan")
            .AddParameter("Plan", plan)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Transport", ModuleStateDeliveryTransport.ManagedModule)
            .AddParameter("Execute");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<PSPublishModule.ModuleStateApplyResult>(Assert.Single(results).BaseObject);
        var execution = Assert.Single(result.ExecutionResults);
        Assert.Equal("Save", execution.Operation);
        Assert.True(execution.OperationPerformed);
        var dependency = Assert.Single(execution.DependencyResults);
        Assert.Equal("ManagedModule", dependency.Installer);
        Assert.Equal("Installed", dependency.Status);
        Assert.Equal("1.0.0", dependency.ResolvedVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InvokeModuleStatePlan_ManagedTransport_RepairsSourceMismatch()
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
        File.WriteAllText(Path.Combine(installedPath, "Company.Tools.psd1"), "@{ ModuleVersion = '1.0.0' }");
        WriteManagedReceipt(installedPath, "OtherRepository", "C:\\OtherFeed");
        var plan = new ModuleStatePlanResult
        {
            Actions = new[]
            {
                new ModuleStatePlanActionResult
                {
                    Kind = "Update",
                    ModuleName = "Company.Tools",
                    InstalledVersion = "1.0.0",
                    VersionPolicy = "=1.0.0",
                    Reason = "source repair",
                    IsRepair = true
                }
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Invoke-ModuleStatePlan")
            .AddParameter("Plan", plan)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Transport", ModuleStateDeliveryTransport.ManagedModule)
            .AddParameter("ModuleRoot", moduleRoot.Path)
            .AddParameter("Execute");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<PSPublishModule.ModuleStateApplyResult>(Assert.Single(results).BaseObject);
        var execution = Assert.Single(result.ExecutionResults);
        Assert.Equal("Update", execution.Operation);
        Assert.True(execution.OperationPerformed);
        var dependency = Assert.Single(execution.DependencyResults);
        Assert.Equal("ManagedModule", dependency.Installer);
        Assert.Equal("SourceRepaired", dependency.Status);
        Assert.Equal("1.0.0", dependency.InstalledVersion);
        Assert.Equal("1.0.0", dependency.ResolvedVersion);
    }

    [Fact]
    public void InvokeModuleStatePlan_ManagedTransport_RepairsScopedMissingCopy()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var plan = new ModuleStatePlanResult
        {
            Actions = new[]
            {
                new ModuleStatePlanActionResult
                {
                    Kind = "Install",
                    ModuleName = "Company.Tools",
                    InstalledVersion = "1.0.0",
                    VersionPolicy = "=1.0.0",
                    Reason = "scope repair",
                    IsRepair = true,
                    TargetScope = "AllUsers",
                    TargetPath = moduleRoot.Path
                }
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Invoke-ModuleStatePlan")
            .AddParameter("Plan", plan)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Transport", ModuleStateDeliveryTransport.ManagedModule)
            .AddParameter("Execute");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<PSPublishModule.ModuleStateApplyResult>(Assert.Single(results).BaseObject);
        var execution = Assert.Single(result.ExecutionResults);
        Assert.Equal("Install", execution.Operation);
        Assert.True(execution.OperationPerformed);
        var dependency = Assert.Single(execution.DependencyResults);
        Assert.Equal("ManagedModule", dependency.Installer);
        Assert.Equal("Installed", dependency.Status);
        Assert.Equal("1.0.0", dependency.ResolvedVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InvokeModuleStatePlan_ManagedTransport_RepairsFamilyVersionMismatch()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Microsoft.Graph.Authentication.2.38.0.nupkg"),
            "Microsoft.Graph.Authentication",
            "2.38.0",
            files: CreateModuleFiles("Microsoft.Graph.Authentication", "2.38.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Microsoft.Graph.Authentication", "2.36.0"));
        File.WriteAllText(
            Path.Combine(moduleRoot.Path, "Microsoft.Graph.Authentication", "2.36.0", "Microsoft.Graph.Authentication.psd1"),
            "@{ ModuleVersion = '2.36.0' }");
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Microsoft.Graph.Users", "2.38.0"));
        File.WriteAllText(
            Path.Combine(moduleRoot.Path, "Microsoft.Graph.Users", "2.38.0", "Microsoft.Graph.Users.psd1"),
            "@{ ModuleVersion = '2.38.0' }");
        var plan = new ModuleStatePlanResult
        {
            Actions = new[]
            {
                new ModuleStatePlanActionResult
                {
                    Kind = "Update",
                    ModuleName = "Microsoft.Graph.Authentication",
                    InstalledVersion = "2.36.0",
                    VersionPolicy = "=2.38.0",
                    Reason = "family repair",
                    IsRepair = true,
                    TargetScope = "CurrentUser",
                    TargetPath = moduleRoot.Path
                }
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Invoke-ModuleStatePlan")
            .AddParameter("Plan", plan)
            .AddParameter("Repository", feed.Path)
            .AddParameter("Transport", ModuleStateDeliveryTransport.ManagedModule)
            .AddParameter("Execute");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<PSPublishModule.ModuleStateApplyResult>(Assert.Single(results).BaseObject);
        var execution = Assert.Single(result.ExecutionResults);
        Assert.Equal("Update", execution.Operation);
        Assert.True(execution.OperationPerformed);
        var dependency = Assert.Single(execution.DependencyResults);
        Assert.Equal("ManagedModule", dependency.Installer);
        Assert.Equal("Updated", dependency.Status);
        Assert.Equal("2.36.0", dependency.InstalledVersion);
        Assert.Equal("2.38.0", dependency.ResolvedVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Microsoft.Graph.Authentication", "2.38.0", "Microsoft.Graph.Authentication.psd1")));
    }

    private static object? InvokeResolveDesiredState(
        InvokeModuleStateCommand command,
        ModuleStateInventoryResult inventory)
    {
        var method = typeof(InvokeModuleStateCommand).GetMethod(
            "ResolveDesiredState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return method!.Invoke(command, new object[] { inventory });
    }

    private static object? InvokeCreateDesiredStateForInstalledModules(
        InvokeModuleStateCommand command,
        ModuleStateInventoryResult inventory)
    {
        var method = typeof(InvokeModuleStateCommand).GetMethod(
            "CreateDesiredStateForInstalledModules",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return method!.Invoke(command, new object[] { inventory });
    }

    private static void InvokeApplyLatestUpdateIntent(
        InvokeModuleStateCommand command,
        ModuleStatePlanResult plan)
    {
        var method = typeof(InvokeModuleStateCommand).GetMethod(
            "ApplyLatestUpdateIntent",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(command, new object[] { plan });
    }

    private static bool InvokeHasFailedExecutionResult(ModuleStateDeliveryExecutionResult[] executionResults)
    {
        var method = typeof(InvokeModuleStateCommand).GetMethod(
            "HasFailedExecutionResult",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return Assert.IsType<bool>(method!.Invoke(null, new object[] { executionResults }));
    }

    private static bool InvokeHasSkippedExecutionResult(ModuleStateDeliveryExecutionResult[] executionResults)
    {
        var method = typeof(InvokeModuleStateCommand).GetMethod(
            "HasSkippedExecutionResult",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return Assert.IsType<bool>(method!.Invoke(null, new object[] { executionResults }));
    }

    private static bool InvokePlanCommandHasSkippedExecutionResult(ModuleStateDeliveryExecutionResult[] executionResults)
    {
        var method = typeof(InvokeModuleStatePlanCommand).GetMethod(
            "HasSkippedExecutionResult",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return Assert.IsType<bool>(method!.Invoke(null, new object[] { executionResults }));
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
        => CreateModuleFiles("Company.Tools", version);

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string moduleName, string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [moduleName + ".psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static void WriteManagedReceipt(string modulePath, string repositoryName, string repositorySource)
    {
        var receiptDirectory = Path.Combine(modulePath, ".powerforge");
        Directory.CreateDirectory(receiptDirectory);
        File.WriteAllText(
            Path.Combine(receiptDirectory, "managed-module-receipt.json"),
            "{\"RepositoryName\":\"" + repositoryName + "\",\"RepositorySource\":\"" + repositorySource.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"}");
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
