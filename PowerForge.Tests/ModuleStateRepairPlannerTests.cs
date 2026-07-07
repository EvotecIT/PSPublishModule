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
    public void CreateRepairActions_RequiresReceiptSourceAndScopeOnSameCopy()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.2.0", scope: "CurrentUser", sourceRepository: "CompanyModules"),
            new ModuleStateInstalledModule("Company.Tools", "1.2.0", scope: "AllUsers", sourceRepository: "PublicGallery")
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
        Assert.Equal("AllUsers", action.TargetScope);
        Assert.Equal("CompanyModules", action.TargetRepository);
        Assert.True(action.Force);
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
    public void CreateRepairActions_PreservesConflictingReceiptVersionsForSameScope()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", scope: "CurrentUser")
        });
        var receipts = new[]
        {
            new ModuleStateMaintenanceReceipt(
                "First",
                new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.1.0", scope: "CurrentUser") }),
            new ModuleStateMaintenanceReceipt(
                "Second",
                new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0", scope: "CurrentUser") })
        };

        var actions = new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            receipts,
            Array.Empty<ModuleStatePlanAction>());

        Assert.Equal(2, actions.Length);
        Assert.Contains(actions, static action => action.VersionPolicy == "=1.1.0");
        Assert.Contains(actions, static action => action.VersionPolicy == "=1.2.0");
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
    public void CreateRepairActions_RepairsFamilyMismatchWhenTargetVersionExistsOnlyInNonEffectiveCopy()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "1.0.0", isEffectiveImportCandidate: true),
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.0.0"),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.0.0", isEffectiveImportCandidate: true)
        });
        var familyPolicies = new[]
        {
            new ModuleStateFamilyPolicy(
                "MicrosoftGraph",
                new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" })
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            Array.Empty<ModuleStateMaintenanceReceipt>(),
            Array.Empty<ModuleStatePlanAction>(),
            familyPolicies));

        Assert.Equal(ModuleStatePlanActionKind.Update, action.Kind);
        Assert.Equal("Microsoft.Graph.Authentication", action.ModuleName);
        Assert.Equal("1.0.0", action.InstalledVersion);
        Assert.Equal("=2.0.0", action.VersionPolicy);
        Assert.True(action.IsRepair);
    }

    [Fact]
    public void CreateRepairActions_TargetsFamilyRepairAtMismatchedScope()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.36.0", scope: "CurrentUser", isEffectiveImportCandidate: true),
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.38.0", scope: "AllUsers"),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.38.0", scope: "AllUsers", isEffectiveImportCandidate: true)
        });
        var familyPolicies = new[]
        {
            new ModuleStateFamilyPolicy(
                "MicrosoftGraph",
                new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" })
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
        Assert.Equal("CurrentUser", action.TargetScope);
        Assert.True(action.IsRepair);
    }

    [Fact]
    public void CreateRepairActions_UsesHighestInstalledFamilyVersionAsRepairTarget()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.36.0", isLoaded: true, isEffectiveImportCandidate: true),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.38.0")
        });
        var familyPolicies = new[]
        {
            new ModuleStateFamilyPolicy(
                "MicrosoftGraph",
                new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" })
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
    public void CreateRepairActions_PreservesCoveredActionRepositoryForFamilyRepair()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.36.0", scope: "CurrentUser", isEffectiveImportCandidate: true),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.38.0", scope: "CurrentUser", isEffectiveImportCandidate: true)
        });
        var existingActions = new[]
        {
            new ModuleStatePlanAction(
                ModuleStatePlanActionKind.Install,
                "Microsoft.Graph.Authentication",
                "2.36.0",
                "=2.36.0",
                "receipt repair",
                isRepair: true,
                targetScope: "CurrentUser",
                targetRepository: "CompanyModules")
        };
        var familyPolicies = new[]
        {
            new ModuleStateFamilyPolicy(
                "MicrosoftGraph",
                new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" })
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            Array.Empty<ModuleStateMaintenanceReceipt>(),
            existingActions,
            familyPolicies));

        Assert.Equal("Microsoft.Graph.Authentication", action.ModuleName);
        Assert.Equal("CurrentUser", action.TargetScope);
        Assert.Equal("CompanyModules", action.TargetRepository);
    }

    [Fact]
    public void CreateRepairActions_DoesNotOverwriteExplicitDesiredActionWithFamilyRepair()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.36.0", scope: "CurrentUser", isEffectiveImportCandidate: true),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "3.0.0", scope: "CurrentUser", isEffectiveImportCandidate: true)
        });
        var existingActions = new[]
        {
            new ModuleStatePlanAction(
                ModuleStatePlanActionKind.NoAction,
                "Microsoft.Graph.Authentication",
                "2.36.0",
                "=2.36.0",
                "explicit desired state is already satisfied",
                targetScope: "CurrentUser")
        };
        var familyPolicies = new[]
        {
            new ModuleStateFamilyPolicy(
                "MicrosoftGraph",
                new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" })
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            Array.Empty<ModuleStateMaintenanceReceipt>(),
            existingActions,
            familyPolicies));

        Assert.False(action.IsRepair);
        Assert.Equal(ModuleStatePlanActionKind.NoAction, action.Kind);
        Assert.Equal("Microsoft.Graph.Authentication", action.ModuleName);
        Assert.Equal("=2.36.0", action.VersionPolicy);
    }

    [Fact]
    public void CreateRepairActions_DoesNotOverwriteUnscopedExplicitDesiredActionWithFamilyRepair()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.36.0", scope: "CurrentUser", isEffectiveImportCandidate: true),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "3.0.0", scope: "CurrentUser", isEffectiveImportCandidate: true)
        });
        var existingActions = new[]
        {
            new ModuleStatePlanAction(
                ModuleStatePlanActionKind.NoAction,
                "Microsoft.Graph.Authentication",
                "2.36.0",
                "=2.36.0",
                "explicit desired state is already satisfied")
        };
        var familyPolicies = new[]
        {
            new ModuleStateFamilyPolicy(
                "MicrosoftGraph",
                new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" })
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            Array.Empty<ModuleStateMaintenanceReceipt>(),
            existingActions,
            familyPolicies));

        Assert.False(action.IsRepair);
        Assert.Equal(ModuleStatePlanActionKind.NoAction, action.Kind);
        Assert.Equal("Microsoft.Graph.Authentication", action.ModuleName);
        Assert.Equal("=2.36.0", action.VersionPolicy);
        Assert.Null(action.TargetScope);
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

    [Fact]
    public void CreateRepairActions_RepairsInstalledGraphPrefixFamilyModules()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.36.0"),
            new ModuleStateInstalledModule("Microsoft.Graph.Applications", "2.38.0")
        });
        var familyPolicies = new ModuleStateFamilyCatalog().Resolve(new[] { "Graph" });

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
    public void CreateRepairActions_PlansInstallRepairForBrokenManifestDependencyGraph()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = WriteInstalledModuleManifest(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            ("Company.Core", "1.0.0"));
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule(
                "Company.Tools",
                "1.0.0",
                scope: "CurrentUser",
                path: modulePath,
                isEffectiveImportCandidate: true)
        });
        var existingActions = new[]
        {
            new ModuleStatePlanAction(
                ModuleStatePlanActionKind.NoAction,
                "Company.Tools",
                "1.0.0",
                "=1.0.0",
                "Installed module version satisfies desired policy.",
                targetScope: "CurrentUser",
                targetRepository: "Local")
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            Array.Empty<ModuleStateMaintenanceReceipt>(),
            existingActions));

        Assert.Equal(ModuleStatePlanActionKind.Install, action.Kind);
        Assert.Equal("Company.Tools", action.ModuleName);
        Assert.Equal("1.0.0", action.InstalledVersion);
        Assert.Equal("=1.0.0", action.VersionPolicy);
        Assert.Equal("CurrentUser", action.TargetScope);
        Assert.Equal(moduleRoot.Path, action.TargetPath);
        Assert.Equal("Local", action.TargetRepository);
        Assert.True(action.IsRepair);
        Assert.False(action.Force);
    }

    [Fact]
    public void CreateRepairActions_DoesNotPlanManifestDependencyRepairWhenDependenciesAreSkipped()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = WriteInstalledModuleManifest(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            ("Company.Core", "1.0.0"));
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule(
                "Company.Tools",
                "1.0.0",
                scope: "CurrentUser",
                path: modulePath,
                isEffectiveImportCandidate: true)
        });
        var existingActions = new[]
        {
            new ModuleStatePlanAction(
                ModuleStatePlanActionKind.NoAction,
                "Company.Tools",
                "1.0.0",
                "=1.0.0",
                "Installed module version satisfies desired policy.",
                targetScope: "CurrentUser",
                targetRepository: "Local",
                skipDependencyCheck: true)
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            Array.Empty<ModuleStateMaintenanceReceipt>(),
            existingActions));

        Assert.Equal(ModuleStatePlanActionKind.NoAction, action.Kind);
        Assert.True(action.SkipDependencyCheck);
        Assert.False(action.IsRepair);
    }

    [Fact]
    public void CreateRepairActions_DoesNotPlanManifestRepairForExternalDependency()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = WriteInstalledModuleManifest(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            ("External.Tools", "9.0.0"));
        File.WriteAllText(
            Path.Combine(modulePath, "Company.Tools.psd1"),
            "@{ ModuleVersion = '1.0.0'; RequiredModules = @(@{ ModuleName = 'External.Tools'; RequiredVersion = '9.0.0'; }); PrivateData = @{ PSData = @{ ExternalModuleDependencies = @('External.Tools') } } }");
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", path: modulePath)
        });
        var existingActions = new[]
        {
            new ModuleStatePlanAction(
                ModuleStatePlanActionKind.NoAction,
                "Company.Tools",
                "1.0.0",
                "=1.0.0",
                "Installed module version satisfies desired policy.")
        };

        var action = Assert.Single(new ModuleStateRepairPlanner().CreateRepairActions(
            inventory,
            Array.Empty<ModuleStateMaintenanceReceipt>(),
            existingActions));

        Assert.Equal(ModuleStatePlanActionKind.NoAction, action.Kind);
        Assert.False(action.IsRepair);
    }

    private static string WriteInstalledModuleManifest(
        string moduleRoot,
        string moduleName,
        string version,
        params (string Name, string Version)[] dependencies)
    {
        var modulePath = Path.Combine(moduleRoot, moduleName, version);
        Directory.CreateDirectory(modulePath);
        var requiredModules = dependencies.Length == 0
            ? string.Empty
            : "; RequiredModules = @(" + string.Join(
                ", ",
                dependencies.Select(static dependency =>
                    "@{ ModuleName = '" + dependency.Name + "'; RequiredVersion = '" + dependency.Version + "'; }")) + ")";
        File.WriteAllText(
            Path.Combine(modulePath, moduleName + ".psd1"),
            "@{ ModuleVersion = '" + version + "'" + requiredModules + " }");
        return modulePath;
    }
}
