using System.Linq;

namespace PowerForge.Tests;

public sealed class ModuleStatePlannerTests
{
    [Fact]
    public void CreatePlan_PlansInstallForMissingDesiredModule()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(Array.Empty<ModuleStateInstalledModule>()),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.0.0") });

        var action = Assert.Single(new ModuleStatePlanner().CreatePlan(request).Actions);

        Assert.Equal(ModuleStatePlanActionKind.Install, action.Kind);
        Assert.Equal("Company.Tools", action.ModuleName);
        Assert.Null(action.InstalledVersion);
        Assert.Equal(">=1.0.0", action.VersionPolicy);
    }

    [Fact]
    public void CreatePlan_PlansUpdateWhenInstalledVersionMissesPolicy()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[] { new ModuleStateInstalledModule("Company.Tools", "1.0.0") }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.2.0") });

        var action = Assert.Single(new ModuleStatePlanner().CreatePlan(request).Actions);

        Assert.Equal(ModuleStatePlanActionKind.Update, action.Kind);
        Assert.Equal("1.0.0", action.InstalledVersion);
    }

    [Fact]
    public void CreatePlan_LeavesSatisfiedModuleAlone()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[] { new ModuleStateInstalledModule("Company.Tools", "1.3.0") }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.2.0 <2.0.0") });

        var action = Assert.Single(new ModuleStatePlanner().CreatePlan(request).Actions);

        Assert.Equal(ModuleStatePlanActionKind.NoAction, action.Kind);
        Assert.Equal("1.3.0", action.InstalledVersion);
    }

    [Fact]
    public void CreatePlan_PlansForcedInstallWhenSatisfiedVersionUsesDisallowedSource()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.3.0", sourceRepository: "PublicGallery")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.2.0", new[] { "CompanyModules" }) });

        var plan = new ModuleStatePlanner().CreatePlan(request);
        var action = Assert.Single(plan.Actions);
        var finding = Assert.Single(plan.Findings);

        Assert.Equal(ModuleStatePlanActionKind.Install, action.Kind);
        Assert.Equal("1.3.0", action.InstalledVersion);
        Assert.True(action.Force);
        Assert.Equal("CompanyModules", action.TargetRepository);
        Assert.False(plan.HasErrors);
        Assert.Equal("ModuleState.SourcePreferenceMismatch", finding.Code);
        Assert.Equal(ModuleStateConflictSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void CreatePlan_UsesHighestInstalledVersionForDecision()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.0.0"),
                new ModuleStateInstalledModule("Company.Tools", "1.3.0")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.2.0") });

        var action = Assert.Single(new ModuleStatePlanner().CreatePlan(request).Actions);

        Assert.Equal(ModuleStatePlanActionKind.NoAction, action.Kind);
        Assert.Equal("1.3.0", action.InstalledVersion);
    }

    [Fact]
    public void CreatePlan_UsesEffectiveImportCandidateForDecision()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.0.0", scope: "CurrentUser", isEffectiveImportCandidate: true),
                new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "AllUsers")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.2.0") });

        var action = Assert.Single(new ModuleStatePlanner().CreatePlan(request).Actions);

        Assert.Equal(ModuleStatePlanActionKind.Update, action.Kind);
        Assert.Equal("1.0.0", action.InstalledVersion);
    }

    [Fact]
    public void CreatePlan_TargetsDesiredScopeWhenOnlyOtherScopeIsInstalled()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "CurrentUser")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.2.0", scope: "AllUsers") });

        var action = Assert.Single(new ModuleStatePlanner().CreatePlan(request).Actions);

        Assert.Equal(ModuleStatePlanActionKind.Install, action.Kind);
        Assert.Null(action.InstalledVersion);
        Assert.Equal("AllUsers", action.TargetScope);
        Assert.Equal("Module is not installed in desired scope.", action.Reason);
    }

    [Fact]
    public void CreatePlan_UsesDesiredScopeVersionForDecision()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.4.0", scope: "CurrentUser"),
                new ModuleStateInstalledModule("Company.Tools", "1.1.0", scope: "AllUsers")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.2.0", scope: "AllUsers") });

        var action = Assert.Single(new ModuleStatePlanner().CreatePlan(request).Actions);

        Assert.Equal(ModuleStatePlanActionKind.Update, action.Kind);
        Assert.Equal("1.1.0", action.InstalledVersion);
        Assert.Equal("AllUsers", action.TargetScope);
    }

    [Fact]
    public void CreatePlan_IncludesFamilyCoherenceFindings()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.36.0"),
                new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.38.0")
            }),
            Enumerable.Empty<ModuleStateDesiredModule>(),
            new[]
            {
                new ModuleStateFamilyPolicy("Graph", new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" })
            });

        var plan = new ModuleStatePlanner().CreatePlan(request);

        Assert.Empty(plan.Actions);
        Assert.True(plan.HasErrors);
        Assert.Equal("ModuleState.FamilyVersionMismatch", Assert.Single(plan.Findings).Code);
    }

    [Fact]
    public void CreatePlan_IncludesPolicyConflictFindings()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.0.0", sourceRepository: "PublicGallery", isLoaded: true),
                new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "CurrentUser", sourceRepository: "PublicGallery")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.2.0", new[] { "CompanyModules" }) });

        var plan = new ModuleStatePlanner().CreatePlan(request);

        var action = Assert.Single(plan.Actions);

        Assert.Equal(ModuleStatePlanActionKind.Install, action.Kind);
        Assert.True(action.Force);
        Assert.Equal("CompanyModules", action.TargetRepository);
        Assert.True(plan.HasErrors);
        Assert.Contains(plan.Findings, static finding => finding.Code == "ModuleState.SourcePreferenceMismatch");
        Assert.Contains(plan.Findings, static finding => finding.Code == "ModuleState.LoadedVersionMismatch");
        Assert.Contains(plan.Findings, static finding =>
            finding.Code == "ModuleState.SourcePreferenceMismatch" &&
            finding.Severity == ModuleStateConflictSeverity.Warning);
    }

    [Fact]
    public void CreatePlan_IncludesMaintenanceReceiptDriftFindings()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.3.0", sourceRepository: "CompanyModules")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.0.0") },
            maintenanceReceipts: new[]
            {
                new ModuleStateMaintenanceReceipt(
                    "Company baseline",
                    new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0", sourceRepository: "CompanyModules") })
            });

        var plan = new ModuleStatePlanner().CreatePlan(request);

        Assert.Equal(ModuleStatePlanActionKind.NoAction, Assert.Single(plan.Actions).Kind);
        Assert.True(plan.HasErrors);
        Assert.Equal("ModuleState.ReceiptVersionDrift", Assert.Single(plan.Findings).Code);
    }

    [Fact]
    public void CreatePlan_WithRepair_PlansReceiptVersionRepairAction()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.3.0", sourceRepository: "CompanyModules")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.0.0") },
            maintenanceReceipts: new[]
            {
                new ModuleStateMaintenanceReceipt(
                    "Company baseline",
                    new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0", sourceRepository: "CompanyModules") })
            },
            repair: true);

        var plan = new ModuleStatePlanner().CreatePlan(request);
        var action = Assert.Single(plan.Actions);

        Assert.Equal(ModuleStatePlanActionKind.Update, action.Kind);
        Assert.Equal("=1.2.0", action.VersionPolicy);
        Assert.True(action.IsRepair);
        Assert.False(plan.HasErrors);
        var finding = Assert.Single(plan.Findings);
        Assert.Equal("ModuleState.ReceiptVersionDrift", finding.Code);
        Assert.Equal(ModuleStateConflictSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void CreatePlan_WithRepair_PlansFamilyMismatchRepairAction()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.36.0"),
                new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.38.0")
            }),
            Enumerable.Empty<ModuleStateDesiredModule>(),
            new[]
            {
                new ModuleStateFamilyPolicy("MicrosoftGraph", new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" })
            },
            repair: true);

        var plan = new ModuleStatePlanner().CreatePlan(request);
        var action = Assert.Single(plan.Actions);

        Assert.Equal(ModuleStatePlanActionKind.Update, action.Kind);
        Assert.Equal("Microsoft.Graph.Authentication", action.ModuleName);
        Assert.Equal("=2.38.0", action.VersionPolicy);
        Assert.True(action.IsRepair);
        Assert.False(plan.HasErrors);
        var finding = Assert.Single(plan.Findings);
        Assert.Equal("ModuleState.FamilyVersionMismatch", finding.Code);
        Assert.Equal(ModuleStateConflictSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void CreatePlan_WithCleanup_PlansRemovalForOldUnloadedManagedVersion()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.0.0", scope: "CurrentUser", path: @"C:\Modules\Company.Tools\1.0.0"),
                new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "CurrentUser", path: @"C:\Modules\Company.Tools\1.3.0")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.2.0") },
            cleanupMode: ModuleStateCleanupMode.OldVersions);

        var plan = new ModuleStatePlanner().CreatePlan(request);
        var cleanupAction = Assert.Single(plan.Actions, static action => action.Kind == ModuleStatePlanActionKind.Remove);

        Assert.Equal("Company.Tools", cleanupAction.ModuleName);
        Assert.Equal("1.0.0", cleanupAction.InstalledVersion);
        Assert.Equal("cleanup:old-versions", cleanupAction.VersionPolicy);
        Assert.Equal("CurrentUser", cleanupAction.TargetScope);
        Assert.Equal(@"C:\Modules\Company.Tools\1.0.0", cleanupAction.TargetPath);
    }

    [Fact]
    public void CreatePlan_WithCleanup_NormalizesMaintenanceReceiptVersionBeforeMatching()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.0.0", scope: "CurrentUser", path: @"C:\Modules\Company.Tools\1.0.0"),
                new ModuleStateInstalledModule("Company.Tools", "1.2.0", scope: "CurrentUser", path: @"C:\Modules\Company.Tools\1.2.0")
            }),
            Array.Empty<ModuleStateDesiredModule>(),
            maintenanceReceipts: new[]
            {
                new ModuleStateMaintenanceReceipt(
                    "ModuleState",
                    new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2") })
            },
            cleanupMode: ModuleStateCleanupMode.OldVersions);

        var plan = new ModuleStatePlanner().CreatePlan(request);
        var cleanupAction = Assert.Single(plan.Actions, static action => action.Kind == ModuleStatePlanActionKind.Remove);

        Assert.Equal("1.0.0", cleanupAction.InstalledVersion);
    }

    [Fact]
    public void CreatePlan_WithCleanup_KeepsDesiredScopeVersion()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "2.0.0", scope: "CurrentUser", path: @"C:\User\Company.Tools\2.0.0"),
                new ModuleStateInstalledModule("Company.Tools", "1.2.0", scope: "AllUsers", path: @"C:\Program Files\Company.Tools\1.2.0")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.2.0", scope: "AllUsers") },
            cleanupMode: ModuleStateCleanupMode.OldVersions);

        var plan = new ModuleStatePlanner().CreatePlan(request);
        var cleanupAction = Assert.Single(plan.Actions, static action => action.Kind == ModuleStatePlanActionKind.Remove);

        Assert.Equal("2.0.0", cleanupAction.InstalledVersion);
        Assert.Equal("CurrentUser", cleanupAction.TargetScope);
    }

    [Fact]
    public void CreatePlan_WithCleanupAndLatestPolicy_KeepsCurrentHighestVersion()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.0.0", scope: "CurrentUser", path: @"C:\Modules\Company.Tools\1.0.0"),
                new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "CurrentUser", path: @"C:\Modules\Company.Tools\1.3.0")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">1.3.0") },
            cleanupMode: ModuleStateCleanupMode.OldVersions);

        var plan = new ModuleStatePlanner().CreatePlan(request);
        var updateAction = Assert.Single(plan.Actions, static action => action.Kind == ModuleStatePlanActionKind.Update);
        var cleanupAction = Assert.Single(plan.Actions, static action => action.Kind == ModuleStatePlanActionKind.Remove);

        Assert.Equal("1.3.0", updateAction.InstalledVersion);
        Assert.Equal("1.0.0", cleanupAction.InstalledVersion);
    }

    [Fact]
    public void CreatePlan_WithCleanup_ReportsLoadedOldVersionInsteadOfRemoval()
    {
        var request = new ModuleStatePlanRequest(
            new ModuleStateInventory(new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "1.0.0", isLoaded: true),
                new ModuleStateInstalledModule("Company.Tools", "1.3.0")
            }),
            new[] { new ModuleStateDesiredModule("Company.Tools", ">=1.2.0") },
            cleanupMode: ModuleStateCleanupMode.OldVersions);

        var plan = new ModuleStatePlanner().CreatePlan(request);

        Assert.DoesNotContain(plan.Actions, static action => action.Kind == ModuleStatePlanActionKind.Remove);
        Assert.True(plan.HasErrors);
        Assert.Contains(plan.Findings, static finding => finding.Code == "ModuleState.CleanupLoadedVersion");
    }
}
