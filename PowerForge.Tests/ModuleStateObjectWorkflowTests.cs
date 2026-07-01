using System.Collections;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ModuleStateObjectWorkflowTests
{
    [Fact]
    public void CreatePlanResult_AcceptsInventoryObjectAndDesiredStateObject()
    {
        var inventory = new ModuleStateInventoryResult
        {
            Source = "ModulePath",
            InstalledModules = Array.Empty<ModuleStateInstalledModuleResult>()
        };
        var desired = new Hashtable
        {
            ["Modules"] = new object[]
            {
                new Hashtable
                {
                    ["Name"] = "Company.Tools",
                    ["Version"] = "=1.2.0",
                    ["Repository"] = "CompanyModules",
                    ["Scope"] = "CurrentUser"
                }
            }
        };

        var plan = ModuleStatePlanCommandSupport.CreatePlanResult(inventory, desired);

        var action = Assert.Single(plan.Actions);
        Assert.Equal("Install", action.Kind);
        Assert.Equal("Company.Tools", action.ModuleName);
        Assert.Equal("=1.2.0", action.VersionPolicy);
        Assert.Equal("CurrentUser", action.TargetScope);
        Assert.Equal("CompanyModules", action.TargetRepository);
        Assert.Equal("ModulePath", plan.InventoryPath);
        Assert.Equal("Object", plan.DesiredStatePath);
    }

    [Fact]
    public void ApplyPreparation_AcceptsPlanObjectWithoutArtifactRoundTrip()
    {
        var planResult = new ModuleStatePlanResult
        {
            Actions = new[]
            {
                new ModuleStatePlanActionResult
                {
                    Kind = "Install",
                    ModuleName = "Company.Tools",
                    VersionPolicy = "=1.2.0",
                    Reason = "Missing module.",
                    TargetScope = "CurrentUser",
                    TargetRepository = "CompanyModules"
                }
            }
        };

        var corePlan = ModuleStatePlanResultMapper.ToCorePlan(planResult);
        var result = new ModuleStateApplyService().Prepare(
            corePlan,
            new ModuleStateDeliveryOptions(transport: ModuleStateDeliveryTransport.ManagedModule));

        Assert.True(result.Receipt.CanApply);
        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal("Install-ManagedModule", command.CommandName);
        Assert.Contains("-Repository 'CompanyModules'", command.CommandText);
        Assert.Contains("-Scope 'CurrentUser'", command.CommandText);
    }
}
