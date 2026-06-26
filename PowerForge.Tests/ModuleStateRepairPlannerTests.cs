namespace PowerForge.Tests;

public sealed class ModuleStateRepairPlannerTests
{
    [Fact]
    public void CreateRepairActions_PlansInstallForMissingReceiptModule()
    {
        var inventory = new ModuleStateInventory(Array.Empty<ModuleStateInstalledModule>());
        var receipts = new[]
        {
            new ModuleStateMaintenanceReceipt(
                "Company baseline",
                new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0") })
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            receipts,
            Array.Empty<ModuleStatePlanAction>()));

        Assert.Equal(ModuleStatePlanActionKind.Install, action.Kind);
        Assert.Equal("Company.Tools", action.ModuleName);
        Assert.Null(action.InstalledVersion);
        Assert.Equal("=1.2.0", action.VersionPolicy);
        Assert.True(action.IsRepair);
    }

    [Fact]
    public void CreateRepairActions_PlansUpdateForReceiptVersionDrift()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.3.0")
        });
        var receipts = new[]
        {
            new ModuleStateMaintenanceReceipt(
                null,
                new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0") })
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            receipts,
            Array.Empty<ModuleStatePlanAction>()));

        Assert.Equal(ModuleStatePlanActionKind.Update, action.Kind);
        Assert.Equal("1.3.0", action.InstalledVersion);
        Assert.Equal("=1.2.0", action.VersionPolicy);
        Assert.True(action.IsRepair);
    }

    [Fact]
    public void CreateRepairActions_PlansReinstallForReceiptSourceDrift()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.2.0", sourceRepository: "PublicGallery")
        });
        var receipts = new[]
        {
            new ModuleStateMaintenanceReceipt(
                null,
                new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0", sourceRepository: "CompanyModules") })
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            receipts,
            Array.Empty<ModuleStatePlanAction>()));

        Assert.Equal(ModuleStatePlanActionKind.Install, action.Kind);
        Assert.Equal("1.2.0", action.InstalledVersion);
        Assert.Equal("=1.2.0", action.VersionPolicy);
        Assert.Equal("CompanyModules", action.TargetRepository);
        Assert.True(action.Force);
        Assert.True(action.IsRepair);
    }

    [Fact]
    public void CreateRepairActions_PlansScopedInstallForReceiptScopeDrift()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.2.0", scope: "CurrentUser", sourceRepository: "CompanyModules")
        });
        var receipts = new[]
        {
            new ModuleStateMaintenanceReceipt(
                null,
            new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0", sourceRepository: "CompanyModules", scope: "AllUsers") })
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            receipts,
            Array.Empty<ModuleStatePlanAction>()));

        Assert.Equal(ModuleStatePlanActionKind.Install, action.Kind);
        Assert.Equal("Company.Tools", action.ModuleName);
        Assert.Equal("1.2.0", action.InstalledVersion);
        Assert.Equal("=1.2.0", action.VersionPolicy);
        Assert.Equal("AllUsers", action.TargetScope);
        Assert.Equal("CompanyModules", action.TargetRepository);
        Assert.True(action.IsRepair);
    }

    [Fact]
    public void CreateRepairActions_PreservesSameModuleRepairsAcrossScopes()
    {
        var inventory = new ModuleStateInventory(Array.Empty<ModuleStateInstalledModule>());
        var receipts = new[]
        {
            new ModuleStateMaintenanceReceipt(
                null,
                new[]
                {
                    new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0", sourceRepository: "CompanyModules", scope: "CurrentUser"),
                    new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0", sourceRepository: "CompanyModules", scope: "AllUsers")
                })
        };

        var actions = new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            receipts,
            Array.Empty<ModuleStatePlanAction>());

        Assert.Equal(2, actions.Length);
        Assert.Contains(actions, static action => action.TargetScope == "CurrentUser");
        Assert.Contains(actions, static action => action.TargetScope == "AllUsers");
    }

    [Fact]
    public void CreateRepairActions_PlansExactUpdateForSameVersionFamilyMismatch()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.36.0"),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.38.0")
        });
        var familyPolicies = new[]
        {
            new ModuleStateFamilyPolicy(
                "MicrosoftGraph",
                new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users", "Microsoft.Graph.Groups" })
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            Array.Empty<ModuleStateMaintenanceReceipt>(),
            Array.Empty<ModuleStatePlanAction>(),
            familyPolicies));

        Assert.Equal(ModuleStatePlanActionKind.Update, action.Kind);
        Assert.Equal("Microsoft.Graph.Authentication", action.ModuleName);
        Assert.Equal("2.36.0", action.InstalledVersion);
        Assert.Equal("=2.38.0", action.VersionPolicy);
        Assert.True(action.IsRepair);
    }

    [Fact]
    public void CreateRepairActions_DoesNotInstallMissingFamilyPresetModules()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.38.0"),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.38.0")
        });
        var familyPolicies = new[]
        {
            new ModuleStateFamilyPolicy(
                "MicrosoftGraph",
                new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users", "Microsoft.Graph.Groups" })
        };

        var actions = new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            Array.Empty<ModuleStateMaintenanceReceipt>(),
            Array.Empty<ModuleStatePlanAction>(),
            familyPolicies);

        Assert.Empty(actions);
    }
}
