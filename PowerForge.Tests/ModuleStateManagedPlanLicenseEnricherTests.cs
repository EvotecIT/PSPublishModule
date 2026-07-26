using System.Reflection;
using PowerForge;
using PSPublishModule;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleStateManagedPlanLicenseEnricherTests
{
    [Fact]
    public void CreateUpdateRequest_UsesActionModuleRootForLicensePreflight()
    {
        const string selectedRoot = @"C:\SelectedRoot";
        var action = new ModuleStatePlanActionResult
        {
            Kind = "Update",
            ModuleName = "Company.Tools",
            VersionPolicy = "=2.0.0",
            TargetScope = "CurrentUser",
            TargetPath = @"C:\SelectedRoot\Company.Tools\1.0.0",
            TargetModuleRoot = selectedRoot
        };
        var method = typeof(ModuleStateManagedPlanLicenseEnricher).GetMethod(
            "CreateUpdateRequest",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var request = Assert.IsType<ManagedModuleUpdateRequest>(method!.Invoke(
            null,
            new object[]
            {
                new ManagedModuleRepository("Company", @"C:\Feed"),
                action,
                new ModuleStateManagedDeliveryOptions()
            }));

        Assert.Equal(ManagedModuleInstallScope.Custom, request.Scope);
        Assert.Equal(selectedRoot, request.ModuleRoot);
    }
}
