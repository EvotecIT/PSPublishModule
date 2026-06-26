using System;
using System.Reflection;
using System.Management.Automation;
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
}
