using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSPublishModule;

namespace PowerForge.Tests;

[Collection("ModuleRepositoryProfileEnvironment")]
public sealed class RepairManagedModuleEstateTests
{
    [Fact]
    public void Plan_KeepsPowerShellEditionsAndPhysicalRootsIndependent()
    {
        using var workspace = new TemporaryDirectory();
        var coreRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var desktopRoot = Path.Combine(workspace.Path, "WindowsPowerShell", "Modules");
        CreateInstalledModule(coreRoot, "Company.Tools", "1.0.0");
        CreateInstalledModule(desktopRoot, "Company.Tools", "1.5.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { coreRoot, desktopRoot })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("MinimumVersion", "2.0.0")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var actions = result.Plan.Actions.Where(static action => action.Kind == "Update").ToArray();
        Assert.Equal(2, actions.Length);
        Assert.Contains(actions, action => action.TargetPowerShellEdition == "Core" && PathsEqual(action.TargetModuleRoot, coreRoot));
        Assert.Contains(actions, action => action.TargetPowerShellEdition == "Desktop" && PathsEqual(action.TargetModuleRoot, desktopRoot));
    }

    [Fact]
    public void Plan_ModuleRootNarrowsRepairToOnePhysicalEstate()
    {
        using var workspace = new TemporaryDirectory();
        var selectedRoot = Path.Combine(workspace.Path, "selected", "Modules");
        var otherRoot = Path.Combine(workspace.Path, "other", "Modules");
        CreateInstalledModule(selectedRoot, "Company.Tools", "1.0.0");
        CreateInstalledModule(otherRoot, "Company.Tools", "1.5.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { selectedRoot, otherRoot })
            .AddParameter("ModuleRoot", selectedRoot)
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("MinimumVersion", "2.0.0")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, static action => action.Kind == "Update");
        Assert.True(PathsEqual(action.TargetModuleRoot, selectedRoot));
        Assert.Equal("1.0.0", action.InstalledVersion);
    }

    [Fact]
    public void Plan_BlocksMissingModuleWhenMultipleRootsAreEligible()
    {
        using var workspace = new TemporaryDirectory();
        var coreRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var desktopRoot = Path.Combine(workspace.Path, "WindowsPowerShell", "Modules");
        Directory.CreateDirectory(coreRoot);
        Directory.CreateDirectory(desktopRoot);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { coreRoot, desktopRoot })
            .AddParameter("Name", new[] { "Company.Missing" })
            .AddParameter("InstallMissing")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var finding = Assert.Single(result.Plan.Findings, static finding => finding.Code == "ModuleState.AmbiguousRepairTarget");
        Assert.Equal("Error", finding.Severity);
        Assert.False(result.Apply.CanApply);
        var action = Assert.Single(result.Plan.Actions, static action => action.ModuleName == "Company.Missing");
        Assert.Null(action.TargetModuleRoot);
        Assert.Null(action.TargetPath);
    }

    [Fact]
    public void Plan_AssignsMissingModuleToTheOnlyEligibleRoot()
    {
        using var workspace = new TemporaryDirectory();
        var moduleRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        Directory.CreateDirectory(moduleRoot);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot })
            .AddParameter("Name", new[] { "Company.Missing" })
            .AddParameter("InstallMissing")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, static action => action.ModuleName == "Company.Missing");
        Assert.Equal("Install", action.Kind);
        Assert.True(PathsEqual(action.TargetModuleRoot, moduleRoot));
        Assert.DoesNotContain(result.Plan.Findings, static finding => finding.Code == "ModuleState.AmbiguousRepairTarget");
    }

    [Fact]
    public void Plan_BlocksMissingModuleWhenInventoryHasNoEligibleRoot()
    {
        var inventory = new ModuleStateInventoryResult { Source = "Empty" };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("Inventory", inventory)
            .AddParameter("Name", new[] { "Company.Missing" })
            .AddParameter("InstallMissing")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var finding = Assert.Single(result.Plan.Findings, static finding => finding.Code == "ModuleState.RepairTargetMissing");
        Assert.Equal("Error", finding.Severity);
        Assert.False(result.Apply.CanApply);
    }

    [Fact]
    public void Plan_MergesExplicitDestinationIntoSuppliedInventory()
    {
        using var workspace = new TemporaryDirectory();
        var artifactRoot = Path.Combine(workspace.Path, "artifact", "Modules");
        var destinationRoot = Path.Combine(workspace.Path, "destination", "Modules");
        Directory.CreateDirectory(artifactRoot);
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Artifact",
            ModulePaths = new[] { artifactRoot },
            ScannedPaths = new[] { new ModuleStateInventoryPathResult { Path = artifactRoot, IsRequired = true } }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("Inventory", inventory)
            .AddParameter("ModuleRoot", destinationRoot)
            .AddParameter("Name", new[] { "Company.Missing" })
            .AddParameter("InstallMissing")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Contains(result.Inventory.ScannedPaths, path => PathsEqual(path.Path, artifactRoot));
        Assert.Contains(result.Inventory.ScannedPaths, path => PathsEqual(path.Path, destinationRoot));
        var action = Assert.Single(result.Plan.Actions, static action => action.ModuleName == "Company.Missing");
        Assert.True(PathsEqual(action.TargetModuleRoot, destinationRoot));
        Assert.DoesNotContain(result.Plan.Findings, static finding => finding.Code is "ModuleState.AmbiguousRepairTarget" or "ModuleState.RepairTargetMissing");
    }

    [Fact]
    public void Apply_ReplacesStaleArtifactRowsWithEmptyLiveRootAndReturnsNoActionProof()
    {
        using var workspace = new TemporaryDirectory();
        var moduleRoot = Path.Combine(workspace.Path, "modules");
        Directory.CreateDirectory(moduleRoot);
        var stalePath = Path.Combine(moduleRoot, "Company.Stale", "1.0.0");
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Artifact",
            ModulePaths = new[] { moduleRoot },
            ScannedPaths = new[] { new ModuleStateInventoryPathResult { Path = moduleRoot, IsRequired = true, WasAvailable = true } },
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Stale",
                    Version = "1.0.0",
                    Path = stalePath,
                    ModuleRoot = moduleRoot,
                    IsEffectiveImportCandidate = true
                }
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("Inventory", inventory)
            .AddParameter("ModuleRoot", moduleRoot)
            .AddParameter("Name", new[] { "Company.Stale" })
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Empty(result.Inventory.InstalledModules);
        Assert.Empty(result.Apply.ExecutionResults);
        Assert.True(result.Apply.ExecutionSucceeded);
        Assert.True(result.Apply.Converged);
        Assert.NotNull(result.Apply.PostApplyInventory);
        Assert.NotNull(result.Apply.PostApplyPlan);
        Assert.True(result.Apply.PostApplyTest?.IsCompliant);
        Assert.Empty(result.Apply.PostApplyInventory!.InstalledModules);
    }

    [Fact]
    public void Plan_BlocksMissingReceiptModuleWhenMultipleRootsAreEligible()
    {
        using var workspace = new TemporaryDirectory();
        var coreRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var desktopRoot = Path.Combine(workspace.Path, "WindowsPowerShell", "Modules");
        Directory.CreateDirectory(coreRoot);
        Directory.CreateDirectory(desktopRoot);
        var receiptPath = CreateMaintenanceReceipt(workspace.Path, "Company.Receipt.Missing");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { coreRoot, desktopRoot })
            .AddParameter("MaintenanceReceiptPath", new[] { receiptPath })
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Contains(result.Plan.Findings, static finding => finding.Code == "ModuleState.AmbiguousRepairTarget");
        var action = Assert.Single(result.Plan.Actions, static action => action.ModuleName == "Company.Receipt.Missing");
        Assert.Null(action.TargetModuleRoot);
        Assert.False(result.Apply.CanApply);
    }

    [Fact]
    public void Plan_AssignsMissingReceiptModuleToTheOnlyEligibleRoot()
    {
        using var workspace = new TemporaryDirectory();
        var moduleRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        Directory.CreateDirectory(moduleRoot);
        var receiptPath = CreateMaintenanceReceipt(workspace.Path, "Company.Receipt.Missing");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot })
            .AddParameter("MaintenanceReceiptPath", new[] { receiptPath })
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, static action => action.ModuleName == "Company.Receipt.Missing");
        Assert.True(PathsEqual(action.TargetModuleRoot, moduleRoot));
        Assert.DoesNotContain(result.Plan.Findings, static finding => finding.Code is "ModuleState.AmbiguousRepairTarget" or "ModuleState.RepairTargetMissing");
    }

    [Fact]
    public void Plan_KeepsCaseDistinctPosixRootsIndependent()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var workspace = new TemporaryDirectory();
        var lowerRoot = Path.Combine(workspace.Path, "profiles", "alice", "Modules");
        var upperRoot = Path.Combine(workspace.Path, "profiles", "Alice", "Modules");
        CreateInstalledModule(lowerRoot, "Company.Tools", "1.0.0");
        CreateInstalledModule(upperRoot, "Company.Tools", "1.1.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { lowerRoot, upperRoot })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("MinimumVersion", "2.0.0")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var actions = result.Plan.Actions.Where(static action => action.Kind == "Update").ToArray();
        Assert.Equal(2, actions.Length);
        Assert.Contains(actions, action => PathsEqual(action.TargetModuleRoot, lowerRoot));
        Assert.Contains(actions, action => PathsEqual(action.TargetModuleRoot, upperRoot));
    }

    [Fact]
    public void Plan_ReportsRequiredMissingInventoryRootAsError()
    {
        using var workspace = new TemporaryDirectory();
        var missingRoot = Path.Combine(workspace.Path, "missing-modules");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { missingRoot })
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var diagnostic = Assert.Single(result.Inventory.Diagnostics);
        Assert.Equal("ModuleState.InventoryPathMissing", diagnostic.Code);
        Assert.Equal("Error", diagnostic.Severity);
        Assert.Contains(result.Plan.Findings, static finding => finding.Code == "ModuleState.InventoryPathMissing");
        Assert.False(result.Test.IsCompliant);
        Assert.False(result.Apply.CanApply);
    }

    [Fact]
    public void Plan_ExpandsExplicitUserProfilesWithoutCollapsingOwners()
    {
        using var workspace = new TemporaryDirectory();
        var aliceProfile = Path.Combine(workspace.Path, "Alice");
        var bobProfile = Path.Combine(workspace.Path, "Bob");
        var aliceRoot = StandardCoreProfileModuleRoot(aliceProfile);
        var bobRoot = StandardCoreProfileModuleRoot(bobProfile);
        CreateInstalledModule(aliceRoot, "Estate.Profile.Tools", "1.0.0");
        CreateInstalledModule(bobRoot, "Estate.Profile.Tools", "1.1.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("UserProfilePath", new[] { aliceProfile, bobProfile })
            .AddParameter("Name", new[] { "Estate.Profile.Tools" })
            .AddParameter("MinimumVersion", "2.0.0")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Contains(result.Inventory.ScannedPaths, path => path.ProfileName == "Alice" && PathsEqual(path.Path, aliceRoot));
        Assert.Contains(result.Inventory.ScannedPaths, path => path.ProfileName == "Bob" && PathsEqual(path.Path, bobRoot));
        var actions = result.Plan.Actions.Where(static action => action.Kind == "Update").ToArray();
        Assert.Equal(2, actions.Length);
        Assert.Contains(actions, static action => action.TargetProfileName == "Alice");
        Assert.Contains(actions, static action => action.TargetProfileName == "Bob");
    }

    [Fact]
    public void Plan_AssignsMissingModuleToSingleExplicitProfileCurrentEditionRoot()
    {
        using var workspace = new TemporaryDirectory();
        var profilePath = Path.Combine(workspace.Path, "EmptyProfile");
        Directory.CreateDirectory(profilePath);
        var expectedRoot = StandardCoreProfileModuleRoot(profilePath);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("UserProfilePath", new[] { profilePath })
            .AddParameter("Name", new[] { "Company.Missing" })
            .AddParameter("InstallMissing")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, static action => action.ModuleName == "Company.Missing");
        Assert.True(PathsEqual(action.TargetModuleRoot, expectedRoot));
        Assert.Equal("Core", action.TargetPowerShellEdition);
        Assert.Equal("EmptyProfile", action.TargetProfileName);
        Assert.DoesNotContain(result.Plan.Findings, static finding => finding.Code is "ModuleState.AmbiguousRepairTarget" or "ModuleState.RepairTargetMissing");
    }

    [Fact]
    public void Plan_DoesNotRedirectAllUsersRequiredResourceIntoExplicitProfile()
    {
        using var workspace = new TemporaryDirectory();
        var profilePath = Path.Combine(workspace.Path, "EmptyProfile");
        Directory.CreateDirectory(profilePath);
        var requiredResource = new System.Collections.Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Missing"] = new System.Collections.Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.0.0",
                ["Scope"] = "AllUsers"
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("Inventory", new ModuleStateInventoryResult { Source = "Empty" })
            .AddParameter("UserProfilePath", new[] { profilePath })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, static action => action.ModuleName == "Company.Missing");
        Assert.Equal("AllUsers", action.TargetScope);
        Assert.Null(action.TargetModuleRoot);
        Assert.Null(action.TargetProfileName);
        Assert.Contains(result.Plan.Findings, static finding => finding.Code == "ModuleState.RepairTargetMissing");
        Assert.False(result.Apply.CanApply);
    }

    [Fact]
    public void Repair_InstallsIntoReceiptOnlyRootAndConverges()
    {
        using var workspace = new TemporaryDirectory();
        using var feed = new TemporaryDirectory();
        var receiptRoot = Path.Combine(workspace.Path, "receipt-only", "Modules");
        var receiptPath = CreateMaintenanceReceipt(
            workspace.Path,
            "Company.Receipt.Root",
            receiptRoot,
            "Core",
            "CurrentUser");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Receipt.Root.1.0.0.nupkg"),
            "Company.Receipt.Root",
            "1.0.0",
            files: new Dictionary<string, string>
            {
                ["Company.Receipt.Root.psd1"] = "@{ ModuleVersion = '1.0.0' }"
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("Inventory", new ModuleStateInventoryResult { Source = "Empty" })
            .AddParameter("MaintenanceReceiptPath", new[] { receiptPath })
            .AddParameter("Repository", feed.Path)
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Contains(result.Inventory.ScannedPaths, path => PathsEqual(path.Path, receiptRoot));
        Assert.True(File.Exists(Path.Combine(receiptRoot, "Company.Receipt.Root", "1.0.0", "Company.Receipt.Root.psd1")));
        Assert.True(result.Apply.ExecutionSucceeded);
        Assert.True(result.Apply.Converged);
        Assert.True(result.Apply.PostApplyTest?.IsCompliant);
        Assert.Contains(result.Apply.PostApplyInventory!.ScannedPaths, path => PathsEqual(path.Path, receiptRoot));
    }

    [Fact]
    public void Cleanup_RemovesOldVersionsWithinEachEditionAndConverges()
    {
        using var workspace = new TemporaryDirectory();
        var coreRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var desktopRoot = Path.Combine(workspace.Path, "WindowsPowerShell", "Modules");
        var coreOld = CreateInstalledModule(coreRoot, "Company.Tools", "1.0.0");
        var coreCurrent = CreateInstalledModule(coreRoot, "Company.Tools", "2.0.0");
        var desktopOld = CreateInstalledModule(desktopRoot, "Company.Tools", "1.5.0");
        var desktopCurrent = CreateInstalledModule(desktopRoot, "Company.Tools", "2.5.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { coreRoot, desktopRoot })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var removals = result.Apply.ExecutionResults.Where(static execution => execution.Operation == "Remove").ToArray();
        Assert.Equal(2, removals.Length);
        Assert.All(removals, static removal => Assert.True(removal.Succeeded));
        Assert.False(Directory.Exists(coreOld));
        Assert.False(Directory.Exists(desktopOld));
        Assert.True(Directory.Exists(coreCurrent));
        Assert.True(Directory.Exists(desktopCurrent));
        Assert.True(result.Apply.ExecutionSucceeded);
        Assert.True(result.Apply.Converged);
        Assert.True(result.Apply.PostApplyTest?.IsCompliant);
    }

    [Fact]
    public void Cleanup_ReplansStaleExactTargetAndReturnsConvergedEvidence()
    {
        using var workspace = new TemporaryDirectory();
        var moduleRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var stalePath = Path.Combine(moduleRoot, "Estate.Stale.Tools", "1.0.0");
        var currentPath = CreateInstalledModule(moduleRoot, "Estate.Stale.Tools", "2.0.0");
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Test",
            ModulePaths = new[] { moduleRoot },
            ScannedPaths = new[]
            {
                new ModuleStateInventoryPathResult
                {
                    Path = moduleRoot,
                    PowerShellEdition = "Core",
                    Scope = "CurrentUser",
                    IsRequired = true
                }
            },
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Estate.Stale.Tools",
                    Version = "1.0.0",
                    PowerShellEdition = "Core",
                    Scope = "CurrentUser",
                    Path = stalePath,
                    ModuleRoot = moduleRoot
                },
                new ModuleStateInstalledModuleResult
                {
                    Name = "Estate.Stale.Tools",
                    Version = "2.0.0",
                    PowerShellEdition = "Core",
                    Scope = "CurrentUser",
                    Path = currentPath,
                    ModuleRoot = moduleRoot,
                    IsEffectiveImportCandidate = true
                }
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("Inventory", inventory)
            .AddParameter("Name", new[] { "Estate.Stale.Tools" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Empty(result.Apply.ExecutionResults);
        Assert.True(result.Apply.ExecutionSucceeded);
        Assert.True(result.Apply.Converged);
        Assert.True(result.Apply.PostApplyTest?.IsCompliant);
        Assert.True(Directory.Exists(currentPath));
    }

    [Fact]
    public void Cleanup_PreservesOldVersionRequiredByAnotherInstalledModule()
    {
        using var workspace = new TemporaryDirectory();
        var moduleRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var oldPath = CreateInstalledModule(moduleRoot, "Company.Core", "1.0.0");
        var currentPath = CreateInstalledModule(moduleRoot, "Company.Core", "2.0.0");
        var dependentPath = CreateInstalledModule(
            moduleRoot,
            "Company.Tools",
            "1.0.0",
            requiredModuleName: "Company.Core",
            requiredModuleVersion: "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot })
            .AddParameter("Name", new[] { "Company.Core" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        Assert.True(ps.HadErrors);
        var execution = Assert.Single(result.Apply.ExecutionResults);
        Assert.False(execution.Succeeded);
        Assert.Equal("Remove", execution.Operation);
        Assert.Contains("required by Company.Tools", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.True(Directory.Exists(dependentPath));
        Assert.False(result.Apply.ExecutionSucceeded);
        Assert.False(result.Apply.Converged);
    }

    [Fact]
    public void Cleanup_PreservesOldVersionRequiredByModuleInAnotherVisibleRoot()
    {
        using var workspace = new TemporaryDirectory();
        var targetRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var dependentRoot = Path.Combine(workspace.Path, "shared", "PowerShell", "Modules");
        var oldPath = CreateInstalledModule(targetRoot, "Company.Core", "1.0.0");
        var currentPath = CreateInstalledModule(targetRoot, "Company.Core", "2.0.0");
        var dependentPath = CreateInstalledModule(
            dependentRoot,
            "Company.Tools",
            "1.0.0",
            requiredModuleName: "Company.Core",
            requiredModuleVersion: "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { targetRoot, dependentRoot })
            .AddParameter("ModuleRoot", targetRoot)
            .AddParameter("Name", new[] { "Company.Core" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        Assert.True(ps.HadErrors);
        var execution = Assert.Single(result.Apply.ExecutionResults);
        Assert.False(execution.Succeeded);
        Assert.Contains("required by Company.Tools", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.True(Directory.Exists(dependentPath));
        Assert.False(result.Apply.ExecutionSucceeded);
        Assert.False(result.Apply.Converged);
    }

    [Fact]
    public void Cleanup_PreservesVersionRequiredByUnknownEditionCustomRoot()
    {
        using var workspace = new TemporaryDirectory();
        var targetRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var dependentRoot = Path.Combine(workspace.Path, "CustomModules");
        var oldPath = CreateInstalledModule(targetRoot, "Company.Core", "1.0.0");
        var currentPath = CreateInstalledModule(targetRoot, "Company.Core", "2.0.0");
        var dependentPath = CreateInstalledModule(
            dependentRoot,
            "Company.CustomTools",
            "1.0.0",
            requiredModuleName: "Company.Core",
            requiredModuleVersion: "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { targetRoot, dependentRoot })
            .AddParameter("ModuleRoot", targetRoot)
            .AddParameter("Name", new[] { "Company.Core" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        Assert.True(ps.HadErrors);
        var execution = Assert.Single(result.Apply.ExecutionResults);
        Assert.False(execution.Succeeded);
        Assert.Contains("required by Company.CustomTools", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.True(Directory.Exists(dependentPath));
        Assert.False(result.Apply.Converged);
    }

    [Fact]
    public void Cleanup_DoesNotUseUnrelatedAnonymousRootToSatisfyDependency()
    {
        using var workspace = new TemporaryDirectory();
        var globalRoot = Path.Combine(workspace.Path, "global");
        var dependentRoot = Path.Combine(workspace.Path, "dependent");
        var unrelatedRoot = Path.Combine(workspace.Path, "unrelated");
        var oldPath = CreateInstalledModule(globalRoot, "Company.Core", "1.0.0");
        var currentPath = CreateInstalledModule(globalRoot, "Company.Core", "2.0.0");
        var dependentPath = CreateInstalledModule(
            dependentRoot,
            "Company.Tools",
            "1.0.0",
            requiredModuleName: "Company.Core",
            requiredModuleVersion: "1.0.0");
        var unrelatedAlternative = CreateInstalledModule(unrelatedRoot, "Company.Core", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { globalRoot, dependentRoot, unrelatedRoot })
            .AddParameter("ModuleRoot", globalRoot)
            .AddParameter("Name", new[] { "Company.Core" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        Assert.True(ps.HadErrors);
        var execution = Assert.Single(result.Apply.ExecutionResults, static execution => execution.Operation == "Remove");
        Assert.False(execution.Succeeded);
        Assert.Contains("required by Company.Tools", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.True(Directory.Exists(dependentPath));
        Assert.True(Directory.Exists(unrelatedAlternative));
        Assert.False(result.Apply.Converged);
    }

    [Fact]
    public void Cleanup_PreservesGlobalVersionRequiredByExplicitProfileModule()
    {
        using var workspace = new TemporaryDirectory();
        var globalRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var profilePath = Path.Combine(workspace.Path, "Alice");
        var profileRoot = StandardCoreProfileModuleRoot(profilePath);
        var oldPath = CreateInstalledModule(globalRoot, "Company.Core", "1.0.0");
        var currentPath = CreateInstalledModule(globalRoot, "Company.Core", "2.0.0");
        var dependentPath = CreateInstalledModule(
            profileRoot,
            "Company.ProfileTools",
            "1.0.0",
            requiredModuleName: "Company.Core",
            requiredModuleVersion: "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { globalRoot })
            .AddParameter("UserProfilePath", new[] { profilePath })
            .AddParameter("ModuleRoot", globalRoot)
            .AddParameter("Name", new[] { "Company.Core" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        Assert.True(ps.HadErrors);
        var execution = Assert.Single(result.Apply.ExecutionResults);
        Assert.False(execution.Succeeded);
        Assert.Contains("required by Company.ProfileTools", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.True(Directory.Exists(dependentPath));
    }

    [Fact]
    public void Cleanup_DoesNotUseAnotherProfilesCopyToSatisfyDependency()
    {
        using var workspace = new TemporaryDirectory();
        var globalRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var profileAPath = Path.Combine(workspace.Path, "Alice");
        var profileBPath = Path.Combine(workspace.Path, "Bob");
        var profileARoot = StandardCoreProfileModuleRoot(profileAPath);
        var profileBRoot = StandardCoreProfileModuleRoot(profileBPath);
        var oldPath = CreateInstalledModule(globalRoot, "Company.Core", "1.0.0");
        var currentPath = CreateInstalledModule(globalRoot, "Company.Core", "2.0.0");
        var dependentPath = CreateInstalledModule(
            profileARoot,
            "Company.ProfileTools",
            "1.0.0",
            requiredModuleName: "Company.Core",
            requiredModuleVersion: "1.0.0");
        var unrelatedAlternative = CreateInstalledModule(profileBRoot, "Company.Core", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { globalRoot })
            .AddParameter("UserProfilePath", new[] { profileAPath, profileBPath })
            .AddParameter("ModuleRoot", globalRoot)
            .AddParameter("Name", new[] { "Company.Core" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        Assert.True(ps.HadErrors);
        var execution = Assert.Single(result.Apply.ExecutionResults);
        Assert.False(execution.Succeeded);
        Assert.Contains("required by Company.ProfileTools", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.True(Directory.Exists(dependentPath));
        Assert.True(Directory.Exists(unrelatedAlternative));
    }

    [Fact]
    public void Cleanup_RemovesSelectedDependentsBeforeTheirSelectedDependencies()
    {
        using var workspace = new TemporaryDirectory();
        var moduleRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var coreOld = CreateInstalledModule(moduleRoot, "A.Core", "1.0.0");
        var coreCurrent = CreateInstalledModule(moduleRoot, "A.Core", "2.0.0");
        var dependentOld = CreateInstalledModule(
            moduleRoot,
            "B.Dependent",
            "1.0.0",
            requiredModuleName: "A.Core",
            requiredModuleVersion: "1.0.0");
        var dependentCurrent = CreateInstalledModule(
            moduleRoot,
            "B.Dependent",
            "2.0.0",
            requiredModuleName: "A.Core",
            requiredModuleVersion: "2.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot })
            .AddParameter("Name", new[] { "A.Core", "B.Dependent" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var removals = result.Apply.ExecutionResults.Where(static execution => execution.Operation == "Remove").ToArray();
        Assert.Equal(2, removals.Length);
        Assert.Equal("B.Dependent", Assert.Single(removals[0].DependencyResults).Name);
        Assert.Equal("A.Core", Assert.Single(removals[1].DependencyResults).Name);
        Assert.False(Directory.Exists(dependentOld));
        Assert.False(Directory.Exists(coreOld));
        Assert.True(Directory.Exists(dependentCurrent));
        Assert.True(Directory.Exists(coreCurrent));
        Assert.True(result.Apply.ExecutionSucceeded);
        Assert.True(result.Apply.Converged);
    }

    [Fact]
    public void Cleanup_ProtectsCurrentRunspaceLoadedModuleWithoutIncludeLoaded()
    {
        using var workspace = new TemporaryDirectory();
        var moduleRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var oldPath = CreateInstalledModule(moduleRoot, "Company.Loaded", "1.0.0");
        var currentPath = CreateInstalledModule(moduleRoot, "Company.Loaded", "2.0.0");

        var initialState = InitialSessionState.CreateDefault2();
        if (OperatingSystem.IsWindows())
            initialState.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
        using var runspace = RunspaceFactory.CreateRunspace(initialState);
        runspace.Open();
        using var ps = PowerShell.Create();
        ps.Runspace = runspace;
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(RepairManagedModuleCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        ps.AddCommand("Import-Module").AddParameter("Name", Path.Combine(oldPath, "Company.Loaded.psd1"));
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot })
            .AddParameter("Name", new[] { "Company.Loaded" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        Assert.True(ps.HadErrors);
        var execution = Assert.Single(result.Apply.ExecutionResults);
        Assert.False(execution.Succeeded);
        Assert.Contains("loaded module", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.False(result.Apply.ExecutionSucceeded);
        Assert.False(result.Apply.Converged);
    }

    private static string CreateInstalledModule(
        string moduleRoot,
        string name,
        string version,
        string? requiredModuleName = null,
        string? requiredModuleVersion = null)
    {
        var modulePath = Path.Combine(moduleRoot, name, version);
        Directory.CreateDirectory(modulePath);
        var requiredModules = string.IsNullOrWhiteSpace(requiredModuleName)
            ? string.Empty
            : "; RequiredModules = @(@{ ModuleName = '" + requiredModuleName + "'; RequiredVersion = '" + requiredModuleVersion + "' })";
        File.WriteAllText(
            Path.Combine(modulePath, name + ".psd1"),
            "@{ RootModule = '" + name + ".psm1'; ModuleVersion = '" + version + "'" + requiredModules + " }");
        File.WriteAllText(Path.Combine(modulePath, name + ".psm1"), string.Empty);
        return modulePath;
    }

    private static string CreateMaintenanceReceipt(string directory, string moduleName)
        => CreateMaintenanceReceipt(directory, moduleName, moduleRoot: null, powerShellEdition: null, scope: null);

    private static string CreateMaintenanceReceipt(
        string directory,
        string moduleName,
        string? moduleRoot,
        string? powerShellEdition,
        string? scope)
    {
        var path = Path.Combine(directory, "module-maintenance.json");
        var optionalProperties = string.IsNullOrWhiteSpace(moduleRoot)
            ? string.Empty
            : ",\"moduleRoot\":\"" + moduleRoot!.Replace("\\", "\\\\") +
              "\",\"powerShellEdition\":\"" + powerShellEdition +
              "\",\"scope\":\"" + scope + "\"";
        File.WriteAllText(path, "{\"maintainedModules\":[{\"name\":\"" + moduleName +
                                "\",\"version\":\"1.0.0\"" + optionalProperties + "}]}");
        return path;
    }

    private static string StandardCoreProfileModuleRoot(string profilePath)
        => OperatingSystem.IsWindows()
            ? Path.Combine(profilePath, "Documents", "PowerShell", "Modules")
            : Path.Combine(profilePath, ".local", "share", "powershell", "Modules");

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

    private static bool PathsEqual(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows()
               ? StringComparison.OrdinalIgnoreCase
               : StringComparison.Ordinal);

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
