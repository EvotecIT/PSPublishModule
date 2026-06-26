using System;
using System.Reflection;
using System.Management.Automation;
using System.Collections;
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
}
