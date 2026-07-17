using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ModuleStateApplyServiceTests
{
    [Fact]
    public void Prepare_CreatesPrivateModuleDeliveryCommands()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Update, "Company.Tools", "1.0.0", ">=1.2.0", "stale"),
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Other", null, ">=1.0.0", "missing", isRepair: true)
            },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(
            plan,
            new ModuleStateDeliveryOptions(profileName: "Company", installPrerequisites: true, prerelease: true, force: true));

        Assert.True(result.Receipt.CanApply);
        Assert.Null(result.Receipt.BlockedReason);
        Assert.Equal(ModuleStateDeliveryTransport.PrivateModule, result.Receipt.Transport);
        Assert.Equal(2, result.Receipt.Commands.Length);

        var update = result.Receipt.Commands[0];
        Assert.Equal("Repair-ManagedModule", update.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-VersionPolicy", ">=1.2.0", "-ProfileName", "Company", "-Prerelease", "-Transport", "PrivateModule", "-Force" }, update.Arguments);
        Assert.True(update.Force);
        Assert.Contains("Repair-ManagedModule", update.CommandText, StringComparison.Ordinal);
        Assert.Contains("-Transport 'PrivateModule'", update.CommandText, StringComparison.Ordinal);

        var install = result.Receipt.Commands[1];
        Assert.Equal("Repair-ManagedModule", install.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Other", "-VersionPolicy", ">=1.0.0", "-InstallMissing", "-ProfileName", "Company", "-Prerelease", "-Transport", "PrivateModule", "-Force" }, install.Arguments);
        Assert.Equal(">=1.0.0", install.VersionPolicy);
        Assert.True(install.IsRepair);
        Assert.True(install.Force);
    }

    [Fact]
    public void Prepare_CreatesManagedModuleDeliveryCommands()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Update,
                    "Company.Tools",
                    "1.0.0",
                    "=1.2.0",
                    "stale",
                    targetScope: "CurrentUser",
                    expectedPackageSha256: new string('a', 64)),
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Other", null, ">=1.0.0 <2.0.0", "missing", isRepair: true, targetRepository: "CompanyModules")
            },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(
            plan,
            new ModuleStateDeliveryOptions(
                repository: "FallbackModules",
                installPrerequisites: true,
                prerelease: true,
                force: true,
                allowClobber: true,
                moduleRoot: @"C:\RepairRoot",
                transport: ModuleStateDeliveryTransport.ManagedModule));

        Assert.True(result.Receipt.CanApply);
        Assert.Null(result.Receipt.BlockedReason);
        Assert.Equal(ModuleStateDeliveryTransport.ManagedModule, result.Receipt.Transport);
        Assert.Equal(2, result.Receipt.Commands.Length);

        var update = result.Receipt.Commands[0];
        Assert.Equal("Update-ManagedModule", update.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-RequiredVersion", "1.2.0", "-Scope", "CurrentUser", "-ModuleRoot", @"C:\RepairRoot", "-Repository", "FallbackModules", "-Prerelease", "-ExpectedPackageSha256", new string('a', 64), "-Force", "-AllowClobber" }, update.Arguments);
        Assert.True(update.Force);
        Assert.Contains("Update-ManagedModule", update.CommandText, StringComparison.Ordinal);

        var install = result.Receipt.Commands[1];
        Assert.Equal("Install-ManagedModule", install.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Other", "-VersionPolicy", ">=1.0.0 <2.0.0", "-ModuleRoot", @"C:\RepairRoot", "-Repository", "CompanyModules", "-Prerelease", "-Force", "-AllowClobber" }, install.Arguments);
        Assert.Equal(">=1.0.0 <2.0.0", install.VersionPolicy);
        Assert.True(install.IsRepair);
        Assert.True(install.Force);
    }

    [Fact]
    public void Prepare_UsesActionTargetPathAsManagedModuleRoot()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Update,
                    "Company.Tools",
                    "1.0.0",
                    "=1.2.0",
                    "repair",
                    targetPath: @"C:\SelectedRoot",
                    targetRepository: "CompanyModules")
            },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(
            plan,
            new ModuleStateDeliveryOptions(
                profileName: "FallbackProfile",
                moduleRoot: @"C:\FallbackRoot",
                transport: ModuleStateDeliveryTransport.ManagedModule));

        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal("Update-ManagedModule", command.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-RequiredVersion", "1.2.0", "-ModuleRoot", @"C:\SelectedRoot", "-Repository", "CompanyModules" }, command.Arguments);
    }

    [Fact]
    public void Prepare_PreservesActionTargetRepositoryOverManagedProfile()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Install,
                    "Company.Tools",
                    null,
                    ">=1.2.0",
                    "repair",
                    targetRepository: "ActionModules")
            },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(
            plan,
            new ModuleStateDeliveryOptions(
                profileName: "ProfileModules",
                transport: ModuleStateDeliveryTransport.ManagedModule));

        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-VersionPolicy", ">=1.2.0", "-Repository", "ActionModules" }, command.Arguments);
    }

    [Fact]
    public void Prepare_UsesProfileNameWhenActionTargetIsCoveredByProfile()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Update,
                    "Company.Tools",
                    null,
                    "*",
                    "repair",
                    targetRepository: "CompanyModules")
            },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(
            plan,
            new ModuleStateDeliveryOptions(
                profileName: "Company",
                transport: ModuleStateDeliveryTransport.ManagedModule,
                profileRepository: "CompanyModules"));

        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-ProfileName", "Company" }, command.Arguments);
    }

    [Fact]
    public void Prepare_CreatesManagedSaveDeliveryCommand()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Save,
                    "Company.Tools",
                    null,
                    "=1.2.0",
                    "missing",
                    targetPath: @"C:\OfflineModules",
                    targetRepository: "CompanyModules")
            },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(
            plan,
            new ModuleStateDeliveryOptions(
                installPrerequisites: true,
                prerelease: true,
                force: true,
                transport: ModuleStateDeliveryTransport.ManagedModule));

        Assert.True(result.Receipt.CanApply);
        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal("Save-ManagedModule", command.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-RequiredVersion", "1.2.0", "-Path", @"C:\OfflineModules", "-Repository", "CompanyModules", "-Prerelease", "-Force" }, command.Arguments);
    }

    [Fact]
    public void Prepare_BlocksSaveDeliveryForPrivateTransport()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Save,
                    "Company.Tools",
                    null,
                    ">=1.2.0",
                    "missing",
                    targetPath: @"C:\OfflineModules",
                    targetRepository: "CompanyModules")
            },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions());

        Assert.False(result.Receipt.CanApply);
        Assert.Contains("managed module transport", result.Receipt.BlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prepare_BlocksPackageHashRequirementForPrivateTransport()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Install,
                    "Company.Tools",
                    null,
                    "=1.2.0",
                    "missing",
                    expectedPackageSha256: new string('a', 64))
            },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions(repository: "CompanyModules"));

        Assert.False(result.Receipt.CanApply);
        Assert.Contains("integrity enforcement", result.Receipt.BlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prepare_BlocksWhenPlanHasErrorFindings()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Other", null, ">=1.0.0", "missing") },
            new[]
            {
                new ModuleStateConflictFinding(
                    ModuleStateConflictSeverity.Error,
                    "ModuleState.FamilyVersionMismatch",
                    "Family mismatch.",
                    "Graph",
                    new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" },
                    new[] { "2.36.0", "2.38.0" })
            });

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions(profileName: "Company"));

        Assert.False(result.Receipt.CanApply);
        Assert.Contains("error findings", result.Receipt.BlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prepare_BlocksWhenDeliveryTargetIsMissing()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Other", null, ">=1.0.0", "missing") },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions());

        Assert.False(result.Receipt.CanApply);
        Assert.Contains("action target repository", result.Receipt.BlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prepare_BlocksManagedDeliveryWhenDeliveryTargetIsMissing()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Update, "Company.Other", "1.0.0", ">=1.2.0", "stale") },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(
            plan,
            new ModuleStateDeliveryOptions(transport: ModuleStateDeliveryTransport.ManagedModule));

        Assert.False(result.Receipt.CanApply);
        Assert.Contains("managed module delivery", result.Receipt.BlockedReason, StringComparison.OrdinalIgnoreCase);
        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal("Update-ManagedModule", command.CommandName);
    }

    [Fact]
    public void Prepare_AddsRequiredVersionForExactRepairPolicy()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Update, "Company.Tools", "1.3.0", "=1.2.0", "receipt drift", isRepair: true) },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company", allowErrorFindings: true));

        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal("Repair-ManagedModule", command.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-RequiredVersion", "1.2.0", "-Repository", "Company", "-Transport", "PrivateModule" }, command.Arguments);
        Assert.Equal("=1.2.0", command.VersionPolicy);
        Assert.True(command.IsRepair);
    }

    [Fact]
    public void Prepare_AddsScopeForTargetedModuleStateAction()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", null, ">=1.2.0", "missing in scope", targetScope: "AllUsers") },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal("Repair-ManagedModule", command.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-VersionPolicy", ">=1.2.0", "-InstallMissing", "-Scope", "AllUsers", "-Repository", "Company", "-Transport", "PrivateModule" }, command.Arguments);
        Assert.Contains("-VersionPolicy '>=1.2.0'", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("-Scope 'AllUsers'", command.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepare_AddsRequiredVersionAndScopeForScopedReceiptRepair()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", "1.2.0", "=1.2.0", "scope repair", isRepair: true, targetScope: "AllUsers") },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal("Repair-ManagedModule", command.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-RequiredVersion", "1.2.0", "-InstallMissing", "-Scope", "AllUsers", "-Repository", "Company", "-Transport", "PrivateModule" }, command.Arguments);
        Assert.True(command.IsRepair);
    }

    [Fact]
    public void Prepare_UsesActionTargetRepositoryAsDeliveryTarget()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", "1.2.0", "=1.2.0", "source repair", isRepair: true, targetRepository: "CompanyModules") },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions());

        Assert.True(result.Receipt.CanApply);
        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-RequiredVersion", "1.2.0", "-InstallMissing", "-Repository", "CompanyModules", "-Transport", "PrivateModule" }, command.Arguments);
    }

    [Fact]
    public void Prepare_PrivateInstallRepairCommandIncludesInstallMissing()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", null, "*", "missing repair", isRepair: true, targetRepository: "CompanyModules") },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions());

        Assert.True(result.Receipt.CanApply);
        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal("Repair-ManagedModule", command.CommandName);
        Assert.Contains("-InstallMissing", command.Arguments);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-InstallMissing", "-Repository", "CompanyModules", "-Transport", "PrivateModule" }, command.Arguments);
    }

    [Fact]
    public void Prepare_PrivateInstallRepairCommandPreservesTargetRoot()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Install,
                    "Company.Tools",
                    null,
                    "*",
                    "missing repair",
                    isRepair: true,
                    targetPath: @"C:\SelectedRoot",
                    targetRepository: "CompanyModules")
            },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions());

        Assert.True(result.Receipt.CanApply);
        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal("Repair-ManagedModule", command.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-InstallMissing", "-ModuleRoot", @"C:\SelectedRoot", "-Repository", "CompanyModules", "-Transport", "PrivateModule" }, command.Arguments);
    }

    [Fact]
    public void Prepare_PrivateTransportPreservesExplicitAcceptLicenseWithoutLicenseMetadata()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", null, "*", "missing repair", isRepair: true, targetRepository: "CompanyModules", acceptLicense: true) },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions());

        Assert.True(result.Receipt.CanApply);
        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal("Repair-ManagedModule", command.CommandName);
        Assert.Contains("-AcceptLicense", command.Arguments);
    }

    [Fact]
    public void Prepare_PreservesActionTargetRepositoryOverGlobalRepository()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Update, "Company.Tools", "1.2.0", ">=1.2.0", "source policy", targetRepository: "CompanyModules") },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions(repository: "PSGallery"));

        Assert.True(result.Receipt.CanApply);
        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-VersionPolicy", ">=1.2.0", "-Repository", "CompanyModules", "-Transport", "PrivateModule" }, command.Arguments);
    }

    [Fact]
    public void Prepare_PreservesActionTargetRepositoryOverFallbackProfile()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Update, "Company.Tools", "1.2.0", ">=1.2.0", "source policy", targetRepository: "CompanyModules") },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions(profileName: "Company"));

        Assert.True(result.Receipt.CanApply);
        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-VersionPolicy", ">=1.2.0", "-Repository", "CompanyModules", "-Transport", "PrivateModule" }, command.Arguments);
    }

    [Fact]
    public void Prepare_UsesProfileNameWhenActionHasNoTargetRepository()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Update, "Company.Tools", "1.2.0", ">=1.2.0", "source policy") },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions(profileName: "Company"));

        Assert.True(result.Receipt.CanApply);
        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-VersionPolicy", ">=1.2.0", "-ProfileName", "Company", "-Transport", "PrivateModule" }, command.Arguments);
    }

    [Fact]
    public void Prepare_DoesNotCreatePrivateDeliveryCommandForCleanupAction()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Remove, "Company.Tools", "1.0.0", "cleanup:old-versions", "old version") },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        Assert.False(result.Receipt.CanApply);
        Assert.Equal(1, result.Receipt.ActionCount);
        Assert.Empty(result.Receipt.Commands);
        Assert.Contains("cleanup actions", result.Receipt.BlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateMaintenanceReceipt_IncludesOnlyKnownMaintainedVersions()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(ModuleStatePlanActionKind.NoAction, "Company.Tools", "1.3.0", ">=1.2.0", "satisfied"),
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Update, "Company.Exact", "1.3.0", "=1.2.0", "repair", isRepair: true),
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Unknown", null, ">=1.0.0", "missing"),
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Remove, "Company.Old", "1.0.0", "cleanup:old-versions", "old")
            },
            Array.Empty<ModuleStateConflictFinding>());
        var service = new ModuleStateApplyService();
        var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        var receipt = service.CreateMaintenanceReceipt(result, source: "ModuleStatePlan", sourceRepository: "Company");

        Assert.Equal("ModuleStatePlan", receipt.Source);
        Assert.Equal(ModuleStateDeliveryTransport.PrivateModule, receipt.DeliveryTransport);
        Assert.Equal("PrivateModule", receipt.Engine);
        Assert.Equal(2, receipt.Modules.Length);
        Assert.Contains(receipt.Modules, static module => module.Name == "Company.Tools" && module.Version == "1.3.0");
        Assert.Contains(receipt.Modules, static module => module.Name == "Company.Exact" && module.Version == "1.2.0");
        Assert.DoesNotContain(receipt.Modules, static module => module.Name == "Company.Unknown");
        Assert.All(receipt.Modules, static module => Assert.Null(module.SourceRepository));
    }

    [Fact]
    public void CreateMaintenanceReceipt_TreatsShorthandExactPolicyAsKnownVersion()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", null, "1.2", "missing")
            },
            Array.Empty<ModuleStateConflictFinding>());
        var service = new ModuleStateApplyService();
        var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        var receipt = service.CreateMaintenanceReceipt(result, sourceRepository: "Company");

        var module = Assert.Single(receipt.Modules);
        Assert.Equal("Company.Tools", module.Name);
        Assert.Equal("1.2.0", module.Version);
    }

    [Fact]
    public void CreateMaintenanceReceipt_UsesObservedVersionForRangeAction()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", null, ">=1.2.0", "missing", targetScope: "AllUsers")
            },
            Array.Empty<ModuleStateConflictFinding>());
        var service = new ModuleStateApplyService();
        var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        var withoutEvidence = service.CreateMaintenanceReceipt(result, sourceRepository: "Company");
        var withEvidence = service.CreateMaintenanceReceipt(
            result,
            sourceRepository: "Company",
            observedModules: new[]
            {
                new ModuleStateInstalledModule("Company.Tools", "2.0.0", scope: "CurrentUser", sourceRepository: "Company"),
                new ModuleStateInstalledModule("Company.Tools", "1.4.0", scope: "AllUsers", sourceRepository: "Company")
            });

        Assert.Empty(withoutEvidence.Modules);
        var module = Assert.Single(withEvidence.Modules);
        Assert.Equal("Company.Tools", module.Name);
        Assert.Equal("1.4.0", module.Version);
        Assert.Equal("Company", module.SourceRepository);
        Assert.Equal("AllUsers", module.Scope);
    }

    [Fact]
    public void CreateMaintenanceReceipt_UsesScopeLessExecutionEvidenceForExactTargetScope()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Update, "Company.Tools", "1.2.0", "=1.4.0", "update", targetScope: "AllUsers")
            },
            Array.Empty<ModuleStateConflictFinding>());
        var service = new ModuleStateApplyService();
        var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        var receipt = service.CreateMaintenanceReceipt(
            result,
            sourceRepository: "Company",
            observedModules: new[] { new ModuleStateInstalledModule("Company.Tools", "1.4.0", sourceRepository: "Company") });

        var module = Assert.Single(receipt.Modules);
        Assert.Equal("Company.Tools", module.Name);
        Assert.Equal("1.4.0", module.Version);
        Assert.Equal("AllUsers", module.Scope);
    }

    [Fact]
    public void CreateMaintenanceReceipt_RequiresScopedEvidenceForScopedRangeAction()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Update, "Company.Tools", "1.2.0", ">=1.2.0", "update", targetScope: "AllUsers")
            },
            Array.Empty<ModuleStateConflictFinding>());
        var service = new ModuleStateApplyService();
        var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        var receipt = service.CreateMaintenanceReceipt(
            result,
            sourceRepository: "Company",
            observedModules: new[] { new ModuleStateInstalledModule("Company.Tools", "3.0.0", sourceRepository: "Company") });

        Assert.Empty(receipt.Modules);
    }

    [Fact]
    public void CreateMaintenanceReceipt_DoesNotInventDesiredSourceWithoutObservedEvidence()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(ModuleStatePlanActionKind.NoAction, "Company.Tools", "1.2.0", ">=1.2.0", "satisfied", targetRepository: "Company")
            },
            Array.Empty<ModuleStateConflictFinding>());
        var service = new ModuleStateApplyService();
        var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        var receipt = service.CreateMaintenanceReceipt(result, sourceRepository: "Company");

        var module = Assert.Single(receipt.Modules);
        Assert.Equal("Company.Tools", module.Name);
        Assert.Null(module.SourceRepository);
    }

    [Fact]
    public void CreateMaintenanceReceipt_DoesNotUseObservedModuleFromWrongScope()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", null, ">=1.2.0", "missing", targetScope: "AllUsers")
            },
            Array.Empty<ModuleStateConflictFinding>());
        var service = new ModuleStateApplyService();
        var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        var receipt = service.CreateMaintenanceReceipt(
            result,
            sourceRepository: "Company",
            observedModules: new[] { new ModuleStateInstalledModule("Company.Tools", "2.0.0", scope: "CurrentUser", sourceRepository: "Company") });

        Assert.Empty(receipt.Modules);
    }

    [Fact]
    public void CreateMaintenanceReceipt_PreservesSameModuleAcrossScopes()
    {
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(ModuleStatePlanActionKind.NoAction, "Company.Tools", "1.2.0", "=1.2.0", "satisfied", targetScope: "CurrentUser"),
                new ModuleStatePlanAction(ModuleStatePlanActionKind.NoAction, "Company.Tools", "1.2.0", "=1.2.0", "satisfied", targetScope: "AllUsers")
            },
            Array.Empty<ModuleStateConflictFinding>());
        var service = new ModuleStateApplyService();
        var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        var receipt = service.CreateMaintenanceReceipt(result, sourceRepository: "Company");

        Assert.Equal(2, receipt.Modules.Length);
        Assert.Contains(receipt.Modules, static module => module.Name == "Company.Tools" && module.Scope == "CurrentUser");
        Assert.Contains(receipt.Modules, static module => module.Name == "Company.Tools" && module.Scope == "AllUsers");
    }

    [Fact]
    public void CreateMaintenanceReceipt_PreservesSameModuleAcrossPhysicalRoots()
    {
        const string aliceRoot = "C:\\Users\\Alice\\Documents\\PowerShell\\Modules";
        const string bobRoot = "C:\\Users\\Bob\\Documents\\PowerShell\\Modules";
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(ModuleStatePlanActionKind.NoAction, "Company.Tools", "1.2.0", "=1.2.0", "satisfied", targetScope: "CurrentUser", targetModuleRoot: aliceRoot, targetPowerShellEdition: "Core", targetProfileName: "Alice"),
                new ModuleStatePlanAction(ModuleStatePlanActionKind.NoAction, "Company.Tools", "1.2.0", "=1.2.0", "satisfied", targetScope: "CurrentUser", targetModuleRoot: bobRoot, targetPowerShellEdition: "Core", targetProfileName: "Bob")
            },
            Array.Empty<ModuleStateConflictFinding>());
        var service = new ModuleStateApplyService();
        var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

        var receipt = service.CreateMaintenanceReceipt(result, sourceRepository: "Company");

        Assert.Equal(2, receipt.Modules.Length);
        Assert.Contains(receipt.Modules, static module => module.ModuleRoot == aliceRoot && module.ProfileName == "Alice" && module.PowerShellEdition == "Core");
        Assert.Contains(receipt.Modules, static module => module.ModuleRoot == bobRoot && module.ProfileName == "Bob" && module.PowerShellEdition == "Core");
    }

    [Fact]
    public void WriteMaintenanceReceipt_WritesDriftCheckableJson()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var receiptPath = Path.Combine(root.FullName, "module-state.maintenance.json");
            var plan = new ModuleStatePlan(
                new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.NoAction, "Company.Tools", "1.3.0", ">=1.2.0", "satisfied") },
                Array.Empty<ModuleStateConflictFinding>());
            var service = new ModuleStateApplyService();
            var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

            service.WriteMaintenanceReceipt(result, receiptPath, "ModuleStatePlan", "Company");

            var receipt = new ModuleStateJsonService().LoadMaintenanceReceipt(receiptPath);
            Assert.Equal("ModuleStatePlan", receipt.Source);
            Assert.Equal(ModuleStateDeliveryTransport.PrivateModule, receipt.DeliveryTransport);
            Assert.Equal("PrivateModule", receipt.Engine);
            var module = Assert.Single(receipt.Modules);
            Assert.Equal("Company.Tools", module.Name);
            Assert.Equal("1.3.0", module.Version);
            Assert.Null(module.SourceRepository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void WriteMaintenanceReceipt_WritesManagedTransportEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var receiptPath = Path.Combine(root.FullName, "module-state.maintenance.json");
            var plan = new ModuleStatePlan(
                new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", null, "=1.3.0", "missing", targetRepository: "Company") },
                Array.Empty<ModuleStateConflictFinding>());
            var service = new ModuleStateApplyService();
            var result = service.Prepare(
                plan,
                new ModuleStateDeliveryOptions(repository: "Company", transport: ModuleStateDeliveryTransport.ManagedModule));

            service.WriteMaintenanceReceipt(result, receiptPath, "ModuleStatePlan", "Company");

            using var document = JsonDocument.Parse(File.ReadAllText(receiptPath));
            Assert.Equal("ManagedModule", document.RootElement.GetProperty("deliveryTransport").GetString());
            Assert.Equal("ManagedModule", document.RootElement.GetProperty("engine").GetString());

            var receipt = new ModuleStateJsonService().LoadMaintenanceReceipt(receiptPath);
            Assert.Equal(ModuleStateDeliveryTransport.ManagedModule, receipt.DeliveryTransport);
            Assert.Equal("ManagedModule", receipt.Engine);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void WriteMaintenanceReceipt_RefusesBlockedPlan()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var receiptPath = Path.Combine(root.FullName, "module-state.maintenance.json");
            var plan = new ModuleStatePlan(
                new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Other", null, ">=1.0.0", "missing") },
                new[]
                {
                    new ModuleStateConflictFinding(
                        ModuleStateConflictSeverity.Error,
                        "ModuleState.SourceMismatch",
                        "Source mismatch.",
                        "Company.Other",
                        new[] { "Company.Other" },
                        new[] { "Unknown", "Company" })
                });
            var service = new ModuleStateApplyService();
            var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

            var exception = Assert.Throws<InvalidOperationException>(
                () => service.WriteMaintenanceReceipt(result, receiptPath, "ModuleStatePlan", "Company"));

            Assert.Contains("cannot be written", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("cannot be applied", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(receiptPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void WriteReceipt_WritesDeliveryCommandEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var receiptPath = Path.Combine(root.FullName, "module-state.receipt.json");
            var plan = new ModuleStatePlan(
                new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Other", null, ">=1.0.0", "missing") },
                Array.Empty<ModuleStateConflictFinding>());
            var service = new ModuleStateApplyService();
            var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));

            service.WriteReceipt(result, receiptPath);

            using var document = JsonDocument.Parse(File.ReadAllText(receiptPath));
            Assert.True(document.RootElement.GetProperty("CanApply").GetBoolean());
            Assert.Equal("PrivateModule", document.RootElement.GetProperty("Transport").GetString());
            Assert.Equal("Repair-ManagedModule", document.RootElement.GetProperty("Commands")[0].GetProperty("CommandName").GetString());
            Assert.Contains("PrivateModule", document.RootElement.GetProperty("Commands")[0].GetProperty("Arguments").EnumerateArray().Select(static item => item.GetString()));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
