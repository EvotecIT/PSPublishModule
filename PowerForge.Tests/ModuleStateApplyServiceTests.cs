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
        Assert.Equal(2, result.Receipt.Commands.Length);

        var update = result.Receipt.Commands[0];
        Assert.Equal("Update-PrivateModule", update.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-VersionPolicy", ">=1.2.0", "-ProfileName", "Company", "-InstallPrerequisites", "-Prerelease" }, update.Arguments);
        Assert.Contains("Update-PrivateModule", update.CommandText, StringComparison.Ordinal);

        var install = result.Receipt.Commands[1];
        Assert.Equal("Install-PrivateModule", install.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Other", "-VersionPolicy", ">=1.0.0", "-ProfileName", "Company", "-InstallPrerequisites", "-Prerelease", "-Force" }, install.Arguments);
        Assert.Equal(">=1.0.0", install.VersionPolicy);
        Assert.True(install.IsRepair);
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
    public void Prepare_AddsRequiredVersionForExactRepairPolicy()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Update, "Company.Tools", "1.3.0", "=1.2.0", "receipt drift", isRepair: true) },
            Array.Empty<ModuleStateConflictFinding>());

        var result = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company", allowErrorFindings: true));

        var command = Assert.Single(result.Receipt.Commands);
        Assert.Equal("Update-PrivateModule", command.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-RequiredVersion", "1.2.0", "-Repository", "Company" }, command.Arguments);
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
        Assert.Equal("Install-PrivateModule", command.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-VersionPolicy", ">=1.2.0", "-Scope", "AllUsers", "-Repository", "Company" }, command.Arguments);
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
        Assert.Equal("Install-PrivateModule", command.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-RequiredVersion", "1.2.0", "-Scope", "AllUsers", "-Repository", "Company" }, command.Arguments);
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
        Assert.Equal(new[] { "-Name", "Company.Tools", "-RequiredVersion", "1.2.0", "-Repository", "CompanyModules" }, command.Arguments);
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
        Assert.Equal(2, receipt.Modules.Length);
        Assert.Contains(receipt.Modules, static module => module.Name == "Company.Tools" && module.Version == "1.3.0");
        Assert.Contains(receipt.Modules, static module => module.Name == "Company.Exact" && module.Version == "1.2.0");
        Assert.DoesNotContain(receipt.Modules, static module => module.Name == "Company.Unknown");
        Assert.All(receipt.Modules, static module => Assert.Equal("Company", module.SourceRepository));
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
            var module = Assert.Single(receipt.Modules);
            Assert.Equal("Company.Tools", module.Name);
            Assert.Equal("1.3.0", module.Version);
            Assert.Equal("Company", module.SourceRepository);
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
            Assert.Equal("Install-PrivateModule", document.RootElement.GetProperty("Commands")[0].GetProperty("CommandName").GetString());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
