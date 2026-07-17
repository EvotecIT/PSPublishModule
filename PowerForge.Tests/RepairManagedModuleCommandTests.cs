using System.Collections;
using System.Management.Automation;
using System.Text.Json;
using PSPublishModule;

namespace PowerForge.Tests;

[Collection("ModuleRepositoryProfileEnvironment")]
public sealed class RepairManagedModuleCommandTests
{
    [Fact]
    public void RepairManagedModule_ExposesManagedMaintenanceParameters()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument("Repair-ManagedModule");

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("Plan"));
        Assert.True(command.Parameters.ContainsKey("Latest"));
        Assert.True(command.Parameters.ContainsKey("InstallMissing"));
        Assert.True(command.Parameters.ContainsKey("RequiredResource"));
        Assert.True(command.Parameters.ContainsKey("RequiredResourceFile"));
        Assert.True(command.Parameters.ContainsKey("Family"));
        Assert.True(command.Parameters.ContainsKey("Cleanup"));
        Assert.True(command.Parameters.ContainsKey("MaintenanceReceiptPath"));
        Assert.True(command.Parameters.ContainsKey("Transport"));
        Assert.True(command.Parameters.ContainsKey("AcceptLicense"));
        Assert.True(command.Parameters.ContainsKey("AllowClobber"));
        Assert.True(command.Parameters.TryGetValue("Version", out var versionParameter));
        Assert.Contains("RequiredVersion", versionParameter.Aliases);
        Assert.True(command.Parameters.TryGetValue("Repository", out var repositoryParameter));
        Assert.Contains("Source", repositoryParameter.Aliases);
        Assert.Contains("RepositoryUri", repositoryParameter.Aliases);
        Assert.True(command.Parameters.TryGetValue("ModuleRoot", out var moduleRootParameter));
        Assert.Contains("Path", moduleRootParameter.Aliases);
    }

    [Fact]
    public void RepairManagedModule_PlanDetectsGraphFamilyVersionMismatch()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Microsoft.Graph.Authentication", "2.36.0");
        CreateInstalledModule(moduleRoot.Path, "Microsoft.Graph.Users", "2.38.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Family", new[] { "Graph" })
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, action =>
            string.Equals(action.ModuleName, "Microsoft.Graph.Authentication", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Kind, "Update", StringComparison.OrdinalIgnoreCase));
        Assert.True(action.IsRepair);
        Assert.Equal("=2.38.0", action.VersionPolicy);
        Assert.False(result.Apply.ExecutionRequested);
        Assert.Empty(result.Apply.ExecutionResults);
    }

    [Fact]
    public void RepairManagedModule_PlanReportsLoadedModuleVersionMismatch()
    {
        using var moduleRoot = new TemporaryDirectory();
        var stalePath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var currentPath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "2.0.0");
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Test",
            ModulePaths = new[] { moduleRoot.Path },
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    Path = stalePath,
                    IsLoaded = true
                },
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "2.0.0",
                    Path = currentPath,
                    IsEffectiveImportCandidate = true
                }
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("Inventory", inventory)
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Version", "2.0.0")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Contains(result.Plan.Findings, static finding =>
            string.Equals(finding.Code, "ModuleState.LoadedVersionMismatch", StringComparison.Ordinal));
        Assert.False(result.Apply.ExecutionRequested);
        Assert.Empty(result.Apply.ExecutionResults);
    }

    [Fact]
    public void RepairManagedModule_PlanCleanupOldVersionsDoesNotDeleteModules()
    {
        using var moduleRoot = new TemporaryDirectory();
        var oldPath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var currentPath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "2.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var cleanupAction = Assert.Single(result.Plan.Actions, action =>
            string.Equals(action.Kind, "Remove", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Company.Tools", cleanupAction.ModuleName);
        Assert.Equal("1.0.0", cleanupAction.InstalledVersion);
        Assert.True(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.False(result.Apply.ExecutionRequested);
        Assert.Empty(result.Apply.ExecutionResults);
    }

    [Fact]
    public void RepairManagedModule_PlanForceMarksPreparedUpdateCommand()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Latest")
            .AddParameter("Repository", "LocalFeed")
            .AddParameter("Force")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var command = Assert.Single(result.Apply.Commands);
        Assert.Equal("Update-ManagedModule", command.CommandName);
        Assert.True(command.Force);
        Assert.Contains("-Force", command.Arguments);
        Assert.False(result.Apply.ExecutionRequested);
        Assert.Empty(result.Apply.ExecutionResults);
    }

    [Fact]
    public void RepairManagedModule_ProfileNameUsesProfileDeliveryTarget()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("Company.Tools", "1.1.0"));
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = feed.Path
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Latest")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("CompanyModules", action.TargetRepository);
        var command = Assert.Single(result.Apply.Commands);
        Assert.Equal("Update-ManagedModule", command.CommandName);
        Assert.Contains("-ProfileName", command.Arguments);
        Assert.Contains("Company", command.Arguments);
        Assert.DoesNotContain("-Repository", command.Arguments);
    }

    [Fact]
    public void RepairManagedModule_RequiredResourceRepositoryNameUsesProfileDeliveryTarget()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = feed.Path
        });
        var requiredResource = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.1.0",
                ["Repository"] = "CompanyModules"
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("ProfileName", "Company")
            .AddParameter("Transport", "PrivateModule")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("CompanyModules", action.TargetRepository);
        Assert.Null(action.TargetRepositorySource);
        var command = Assert.Single(result.Apply.Commands);
        Assert.Equal("Repair-ManagedModule", command.CommandName);
        Assert.Contains("-ProfileName", command.Arguments);
        Assert.Contains("Company", command.Arguments);
        Assert.DoesNotContain("-Repository", command.Arguments);
    }

    [Fact]
    public void RepairManagedModule_ProfileNameUsesProfileSourceForLicensePreflight()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("Company.Tools", "1.1.0"),
            requireLicenseAcceptance: true);
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = feed.Path
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Latest")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, static action =>
            string.Equals(action.ModuleName, "Company.Tools", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Kind, "Update", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("CompanyModules", action.TargetRepository);
        Assert.True(action.LicenseAcceptanceRequired);
        Assert.False(action.LicenseAccepted);
        Assert.False(result.Apply.CanApply);
    }

    [Fact]
    public void RepairManagedModule_ProfileNameResolvesMachineScopeProfileForPlanning()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var machineProfileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStores(profileRoot.Path, machineProfileRoot.Path);
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        new ModuleRepositoryProfileStore(ModuleRepositoryProfileScope.Machine).SaveProfile(new ModuleRepositoryProfile
        {
            Name = "CompanyMachine",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "MachineModules",
            RepositoryUri = feed.Path
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Latest")
            .AddParameter("ProfileName", "CompanyMachine")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("MachineModules", action.TargetRepository);
    }

    [Fact]
    public void RepairManagedModule_ProfileNameAppliesUpdateToInventoriedCustomRoot()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("Company.Tools", "1.1.0"));
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = feed.Path
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Latest")
            .AddParameter("ProfileName", "Company");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(result.Apply.ExecutionRequested);
        Assert.Contains(result.Apply.ExecutionResults, execution =>
            string.Equals(execution.Operation, "Update", StringComparison.OrdinalIgnoreCase) &&
            execution.OperationPerformed);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void RepairManagedModule_ProfileNameAppliesUpdateToInventoriedRootWhenMultipleModulePathsAreScanned()
    {
        using var feed = new TemporaryDirectory();
        using var selectedRoot = new TemporaryDirectory();
        using var otherRoot = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        CreateInstalledModule(selectedRoot.Path, "Company.Tools", "1.0.0");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("Company.Tools", "1.1.0"));
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = feed.Path
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { selectedRoot.Path, otherRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Latest")
            .AddParameter("ProfileName", "Company");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(result.Apply.ExecutionRequested);
        var action = Assert.Single(result.Plan.Actions, static action =>
            string.Equals(action.ModuleName, "Company.Tools", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(selectedRoot.Path, action.TargetPath);
        Assert.True(File.Exists(Path.Combine(selectedRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
        Assert.False(File.Exists(Path.Combine(otherRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void RepairManagedModule_PlanReportsLicenseRequiredPackageAndBlocksApplyWithoutAcceptance()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("Company.Tools", "1.1.0"),
            requireLicenseAcceptance: true);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Latest")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, static action =>
            string.Equals(action.ModuleName, "Company.Tools", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Kind, "Update", StringComparison.OrdinalIgnoreCase));
        Assert.True(action.LicenseAcceptanceRequired);
        Assert.False(action.LicenseAccepted);
        Assert.False(result.Apply.CanApply);
        Assert.Contains("license acceptance", result.Apply.BlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Apply.ExecutionResults);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0")));
    }

    [Fact]
    public void RepairManagedModule_PlanPreservesAcceptedLicenseOnPreparedCommand()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("Company.Tools", "1.1.0"),
            requireLicenseAcceptance: true);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Latest")
            .AddParameter("Repository", feed.Path)
            .AddParameter("AcceptLicense")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, static action =>
            string.Equals(action.ModuleName, "Company.Tools", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Kind, "Update", StringComparison.OrdinalIgnoreCase));
        Assert.True(action.LicenseAcceptanceRequired);
        Assert.True(action.LicenseAccepted);
        Assert.True(result.Apply.CanApply);
        var command = Assert.Single(result.Apply.Commands);
        Assert.Contains("-AcceptLicense", command.Arguments);
        Assert.False(result.Apply.ExecutionRequested);
        Assert.Empty(result.Apply.ExecutionResults);
    }

    [Fact]
    public void RepairManagedModule_AppliesExactCleanupRemovalAndVerifiesConvergence()
    {
        using var moduleRoot = new TemporaryDirectory();
        var oldPath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var currentPath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "2.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Force");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(result.Apply.CanApply);
        Assert.Null(result.Apply.BlockedReason);
        Assert.Empty(result.Apply.Commands);
        Assert.True(result.Apply.ExecutionRequested);
        var execution = Assert.Single(result.Apply.ExecutionResults);
        Assert.True(execution.Succeeded);
        Assert.True(execution.OperationPerformed);
        Assert.Equal("Remove", execution.Operation);
        Assert.Equal(oldPath, execution.TargetPath);
        Assert.False(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.True(result.Apply.ExecutionSucceeded);
        Assert.True(result.Apply.Converged);
        Assert.NotNull(result.Apply.PostApplyInventory);
        Assert.NotNull(result.Apply.PostApplyPlan);
        Assert.True(result.Apply.PostApplyTest?.IsCompliant);
    }

    [Fact]
    public void RepairManagedModule_ForcePlansReinstallForSatisfiedVersion()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Version", "1.0.0")
            .AddParameter("Repository", "LocalFeed")
            .AddParameter("Force")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, static action =>
            string.Equals(action.ModuleName, "Company.Tools", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Update", action.Kind);
        Assert.True(action.IsRepair);
        Assert.True(action.Force);
        Assert.True(result.Apply.CanApply);
        var command = Assert.Single(result.Apply.Commands);
        Assert.Contains("-Force", command.Arguments);
        Assert.False(result.Apply.ExecutionRequested);
        Assert.Empty(result.Apply.ExecutionResults);
    }

    [Fact]
    public void RepairManagedModule_PrivatePlanDoesNotResolveMissingCredentialSecretFile()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var missingSecretPath = Path.Combine(moduleRoot.Path, "missing-feed-secret.txt");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Version", "1.1.0")
            .AddParameter("Repository", "CompanyModules")
            .AddParameter("Transport", ModuleStateDeliveryTransport.PrivateModule)
            .AddParameter("CredentialUserName", "build")
            .AddParameter("CredentialSecretFilePath", missingSecretPath)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.False(result.Apply.ExecutionRequested);
        Assert.Empty(result.Apply.ExecutionResults);
        Assert.Contains(result.Apply.Commands, command =>
            string.Equals(command.CommandName, "Repair-ManagedModule", StringComparison.OrdinalIgnoreCase) &&
            command.Arguments.Contains("-Transport"));
    }

    [Fact]
    public void RepairManagedModule_PathRepositoryPlansAgainstResolvedRepositoryName()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var installedPath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        WriteManagedReceipt(installedPath, "Local", feed.Path);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Version", "1.0.0")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal("NoAction", action.Kind);
        Assert.Equal("Local", action.TargetRepository);
        Assert.DoesNotContain(result.Plan.Findings, finding =>
            string.Equals(finding.Code, "ModuleState.SourcePreferenceMismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RepairManagedModule_AppliesManifestDependencyRepairToInstalledModule()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModuleWithRequiredModule(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Core", "1.0.0");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateModuleFiles("Company.Core", "1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Repository", feed.Path);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, static action =>
            string.Equals(action.ModuleName, "Company.Tools", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Install", action.Kind);
        Assert.True(action.IsRepair);
        Assert.Equal("=1.0.0", action.VersionPolicy);
        Assert.True(result.Apply.ExecutionRequested);
        var execution = Assert.Single(result.Apply.ExecutionResults);
        Assert.Equal("Install", execution.Operation);
        Assert.True(execution.OperationPerformed);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0", "Company.Core.psd1")));
    }

    [Fact]
    public void RepairManagedModule_ForceDoesNotApproveExplicitDowngradePolicy()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "2.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Version", "1.0.0")
            .AddParameter("Repository", "LocalFeed")
            .AddParameter("Force")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var finding = Assert.Single(result.Plan.Findings, static finding =>
            string.Equals(finding.Code, "ModuleState.DowngradeRequiresCleanup", StringComparison.Ordinal));
        Assert.Equal(ModuleStateConflictSeverity.Error.ToString(), finding.Severity);
        Assert.False(result.Apply.CanApply);
        Assert.Contains("error findings", result.Apply.BlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.Apply.ExecutionRequested);
        Assert.Empty(result.Apply.ExecutionResults);
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

    private static string CreateInstalledModuleWithRequiredModule(
        string moduleRoot,
        string name,
        string version,
        string dependencyName,
        string dependencyVersion)
    {
        var modulePath = Path.Combine(moduleRoot, name, version);
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(
            Path.Combine(modulePath, name + ".psd1"),
            "@{ RootModule = '" + name + ".psm1'; ModuleVersion = '" + version + "'; RequiredModules = @(@{ ModuleName = '" + dependencyName + "'; RequiredVersion = '" + dependencyVersion + "'; }) }");
        File.WriteAllText(Path.Combine(modulePath, name + ".psm1"), string.Empty);
        return modulePath;
    }

    private static void WriteManagedReceipt(string modulePath, string repositoryName, string repositorySource)
    {
        var receiptDirectory = Path.Combine(modulePath, ".powerforge");
        Directory.CreateDirectory(receiptDirectory);
        File.WriteAllText(
            Path.Combine(receiptDirectory, "managed-module-receipt.json"),
            JsonSerializer.Serialize(new
            {
                RepositoryName = repositoryName,
                RepositorySource = repositorySource
            }));
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

    private static IDisposable UseProfileStore(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "profiles.json");
        return new TestEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH", path);
    }

    private static IDisposable UseProfileStores(string userRoot, string machineRoot)
    {
        Directory.CreateDirectory(userRoot);
        Directory.CreateDirectory(machineRoot);
        return new CompositeDisposable(
            new TestEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH", Path.Combine(userRoot, "profiles.json")),
            new TestEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH", Path.Combine(machineRoot, "profiles.json")));
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }

    private sealed class TestEnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        internal TestEnvironmentVariable(string name, string value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _items;

        internal CompositeDisposable(params IDisposable[] items)
            => _items = items;

        public void Dispose()
        {
            foreach (var item in _items.Reverse())
                item.Dispose();
        }
    }
}
