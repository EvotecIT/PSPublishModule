using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge;

internal sealed class ModuleStateApplyService
{
    private static readonly JsonSerializerOptions ReceiptJsonOptions = new()
    {
        WriteIndented = true
    };

    internal ModuleStateApplyResult Prepare(ModuleStatePlan plan, ModuleStateDeliveryOptions deliveryOptions)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        if (deliveryOptions is null)
            throw new ArgumentNullException(nameof(deliveryOptions));

        var actionCount = plan.Actions.Count(static action => action.Kind != ModuleStatePlanActionKind.NoAction);
        var deliveryActions = plan.Actions
            .Where(static action => IsDeliveryAction(action.Kind))
            .ToArray();
        var commands = deliveryActions.Select(action => CreateCommand(action, deliveryOptions)).ToArray();
        var blockedReason = ResolveBlockedReason(plan, deliveryOptions, commands);
        var receipt = new ModuleStateApplyReceipt(
            DateTimeOffset.UtcNow,
            blockedReason is null,
            blockedReason,
            actionCount,
            plan.Findings.Length,
            deliveryOptions.Transport,
            commands);

        return new ModuleStateApplyResult(receipt, plan);
    }

    internal void WriteReceipt(ModuleStateApplyResult result, string path)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Receipt path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var receipt = result.Receipt;
        var dto = new
        {
            receipt.CreatedAtUtc,
            receipt.CanApply,
            receipt.BlockedReason,
            receipt.ActionCount,
            receipt.FindingCount,
            Transport = receipt.Transport.ToString(),
            Commands = receipt.Commands.Select(static command => new
            {
                ActionKind = command.ActionKind.ToString(),
                command.ModuleName,
                command.VersionPolicy,
                command.IsRepair,
                command.Force,
                command.CommandName,
                command.Arguments,
                command.CommandText
            }).ToArray()
        };

        File.WriteAllText(fullPath, JsonSerializer.Serialize(dto, ReceiptJsonOptions));
    }

    internal ModuleStateMaintenanceReceipt CreateMaintenanceReceipt(
        ModuleStateApplyResult result,
        string? source = null,
        string? sourceRepository = null,
        IEnumerable<ModuleStateInstalledModule>? observedModules = null)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        var observedByName = (observedModules ?? Array.Empty<ModuleStateInstalledModule>())
            .Where(static module => module is not null)
            .GroupBy(static module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var modules = result.Plan.Actions
            .Where(static action => action.Kind is ModuleStatePlanActionKind.NoAction or ModuleStatePlanActionKind.Install or ModuleStatePlanActionKind.Update or ModuleStatePlanActionKind.Save)
            .Select(action => CreateMaintenanceReceiptModule(action, sourceRepository, observedByName))
            .Where(static module => module is not null)
            .Cast<ModuleStateMaintenanceReceiptModule>()
            .GroupBy(static module => string.Join("|", module.Name, module.Scope ?? string.Empty), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ModuleStateMaintenanceReceipt(source, modules, result.Receipt.Transport, ResolveMaintenanceReceiptEngine(result.Receipt.Transport));
    }

    internal void WriteMaintenanceReceipt(
        ModuleStateApplyResult result,
        string path,
        string? source = null,
        string? sourceRepository = null,
        IEnumerable<ModuleStateInstalledModule>? observedModules = null)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Maintenance receipt path is required.", nameof(path));
        if (!result.Receipt.CanApply)
            throw new InvalidOperationException(
                "ModuleState maintenance receipt cannot be written because the plan cannot be applied: "
                + (result.Receipt.BlockedReason ?? "unknown reason"));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var receipt = CreateMaintenanceReceipt(result, source, sourceRepository, observedModules);
        var dto = new
        {
            source = receipt.Source,
            deliveryTransport = receipt.DeliveryTransport?.ToString(),
            engine = receipt.Engine,
            maintainedModules = receipt.Modules.Select(static module => new
            {
                name = module.Name,
                version = module.Version,
                sourceRepository = module.SourceRepository,
                scope = module.Scope
            }).ToArray()
        };

        File.WriteAllText(fullPath, JsonSerializer.Serialize(dto, ReceiptJsonOptions));
    }

    private static string? ResolveBlockedReason(
        ModuleStatePlan plan,
        ModuleStateDeliveryOptions deliveryOptions,
        ModuleStateDeliveryCommand[] commands)
    {
        if (plan.HasErrors && !deliveryOptions.AllowErrorFindings)
            return "Plan has error findings. Re-run with an explicit allow-conflict choice after reviewing findings.";

        if (plan.Actions.Any(static action => action.Kind == ModuleStatePlanActionKind.Remove))
            return "Plan includes cleanup actions that private module delivery does not execute. Review the plan and run without Cleanup to execute install/update delivery only.";

        if (plan.Actions.Any(static action => action.Kind == ModuleStatePlanActionKind.Save) &&
            deliveryOptions.Transport != ModuleStateDeliveryTransport.ManagedModule)
        {
            return "Plan includes save actions. Save delivery requires managed module transport.";
        }

        if (deliveryOptions.Transport != ModuleStateDeliveryTransport.ManagedModule &&
            plan.Actions.Any(static action => IsDeliveryAction(action.Kind) && !string.IsNullOrWhiteSpace(action.ExpectedPackageSha256)))
        {
            return "Plan includes package SHA256 requirements. Package integrity enforcement requires managed module transport.";
        }

        if (plan.Actions.Any(static action => action.Kind == ModuleStatePlanActionKind.Save && string.IsNullOrWhiteSpace(action.TargetPath)))
            return "Plan includes save actions but no target path was supplied.";

        if (deliveryOptions.Transport == ModuleStateDeliveryTransport.ManagedModule &&
            !deliveryOptions.AcceptLicense &&
            plan.Actions.Any(static action => action.LicenseAcceptanceRequired && IsDeliveryAction(action.Kind)))
        {
            var modules = plan.Actions
                .Where(static action => action.LicenseAcceptanceRequired && IsDeliveryAction(action.Kind))
                .Select(static action => action.ModuleName)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return "Planned managed delivery requires license acceptance for " + string.Join(", ", modules) + ".";
        }

        if (commands.Any(static command => !HasCommandDeliveryTarget(command)))
            return deliveryOptions.Transport == ModuleStateDeliveryTransport.ManagedModule
                ? "Plan has delivery actions but no ProfileName, Repository, or action target repository was supplied for managed module delivery."
                : "Plan has delivery actions but no ProfileName, Repository, or action target repository was supplied for private module delivery.";

        return null;
    }

    private static ModuleStateDeliveryCommand CreateCommand(
        ModuleStatePlanAction action,
        ModuleStateDeliveryOptions deliveryOptions)
    {
        var commandName = ResolveCommandName(action.Kind, deliveryOptions.Transport);
        var arguments = new List<string>
        {
            "-Name",
            action.ModuleName
        };
        var requiredVersion = GetExactVersionPolicyValue(action.VersionPolicy);
        if (!string.IsNullOrWhiteSpace(requiredVersion))
        {
            arguments.Add("-RequiredVersion");
            arguments.Add(requiredVersion!);
        }
        else if (!string.IsNullOrWhiteSpace(action.VersionPolicy) &&
                 !string.Equals(action.VersionPolicy, "*", StringComparison.Ordinal))
        {
            arguments.Add("-VersionPolicy");
            arguments.Add(action.VersionPolicy);
        }

        if (!string.IsNullOrWhiteSpace(action.TargetScope) && action.Kind != ModuleStatePlanActionKind.Save)
        {
            arguments.Add("-Scope");
            arguments.Add(action.TargetScope!);
        }

        if (!string.IsNullOrWhiteSpace(action.TargetPath) && action.Kind == ModuleStatePlanActionKind.Save)
        {
            arguments.Add("-Path");
            arguments.Add(action.TargetPath!);
        }
        else if (deliveryOptions.Transport == ModuleStateDeliveryTransport.ManagedModule &&
                 !string.IsNullOrWhiteSpace(deliveryOptions.ModuleRoot))
        {
            arguments.Add(action.Kind == ModuleStatePlanActionKind.Save ? "-Path" : "-ModuleRoot");
            arguments.Add(deliveryOptions.ModuleRoot!);
        }

        if (!string.IsNullOrWhiteSpace(deliveryOptions.ProfileName))
        {
            arguments.Add("-ProfileName");
            arguments.Add(deliveryOptions.ProfileName!);
        }
        else if (!string.IsNullOrWhiteSpace(action.TargetRepository))
        {
            arguments.Add("-Repository");
            arguments.Add(action.TargetRepository!);
        }
        else if (!string.IsNullOrWhiteSpace(deliveryOptions.Repository))
        {
            arguments.Add("-Repository");
            arguments.Add(deliveryOptions.Repository!);
        }

        if (deliveryOptions.Prerelease)
            arguments.Add("-Prerelease");
        if (deliveryOptions.Transport == ModuleStateDeliveryTransport.ManagedModule &&
            !string.IsNullOrWhiteSpace(action.ExpectedPackageSha256))
        {
            arguments.Add("-ExpectedPackageSha256");
            arguments.Add(action.ExpectedPackageSha256!);
        }

        var effectiveForce = deliveryOptions.Force || action.Force;
        if (effectiveForce && IsDeliveryAction(action.Kind))
        {
            arguments.Add("-Force");
        }

        if (deliveryOptions.Transport == ModuleStateDeliveryTransport.ManagedModule &&
            deliveryOptions.AcceptLicense &&
            action.LicenseAcceptanceRequired &&
            IsDeliveryAction(action.Kind))
        {
            arguments.Add("-AcceptLicense");
        }

        if (deliveryOptions.Transport == ModuleStateDeliveryTransport.ManagedModule &&
            deliveryOptions.AllowClobber &&
            IsDeliveryAction(action.Kind))
        {
            arguments.Add("-AllowClobber");
        }

        return new ModuleStateDeliveryCommand(
            action.Kind,
            action.ModuleName,
            action.VersionPolicy,
            action.IsRepair,
            effectiveForce,
            commandName,
            arguments.ToArray(),
            commandName + " " + string.Join(" ", FormatArguments(arguments)));
    }

    private static string ResolveCommandName(ModuleStatePlanActionKind actionKind, ModuleStateDeliveryTransport transport)
    {
        if (actionKind == ModuleStatePlanActionKind.Save)
            return "Save-ManagedModule";
        return actionKind == ModuleStatePlanActionKind.Update
            ? "Update-ManagedModule"
            : "Install-ManagedModule";
    }

    private static string? GetExactVersionPolicyValue(string? versionPolicy)
    {
        if (string.IsNullOrWhiteSpace(versionPolicy))
            return null;

        var trimmed = versionPolicy!.Trim();
        if (trimmed.StartsWith("=", StringComparison.Ordinal))
            return trimmed.Substring(1).Trim();
        if (ModuleStateVersion.TryParse(trimmed, out var exactVersion))
            return exactVersion.Normalized;

        return null;
    }

    private static ModuleStateMaintenanceReceiptModule? CreateMaintenanceReceiptModule(
        ModuleStatePlanAction action,
        string? sourceRepository,
        IReadOnlyDictionary<string, ModuleStateInstalledModule[]> observedByName)
    {
        var version = GetExactVersionPolicyValue(action.VersionPolicy);
        var observedModules = observedByName.TryGetValue(action.ModuleName, out var modules)
            ? modules
            : Array.Empty<ModuleStateInstalledModule>();
        if (string.IsNullOrWhiteSpace(version) && action.Kind == ModuleStatePlanActionKind.NoAction)
            version = action.InstalledVersion;
        var observedModule = SelectObservedModule(action, sourceRepository, observedModules, version);
        if (string.IsNullOrWhiteSpace(version) && observedModule is not null)
            version = observedModule.Version;

        if (string.IsNullOrWhiteSpace(version))
            return null;

        return new ModuleStateMaintenanceReceiptModule(
            action.ModuleName,
            version!,
            observedModule?.SourceRepository,
            observedModule?.Scope ?? action.TargetScope);
    }

    private static bool HasCommandDeliveryTarget(ModuleStateDeliveryCommand command)
        => command.Arguments.Contains("-ProfileName", StringComparer.OrdinalIgnoreCase) ||
           command.Arguments.Contains("-Repository", StringComparer.OrdinalIgnoreCase);

    private static bool IsDeliveryAction(ModuleStatePlanActionKind kind)
        => kind is ModuleStatePlanActionKind.Install or ModuleStatePlanActionKind.Update or ModuleStatePlanActionKind.Save;

    private static string ResolveMaintenanceReceiptEngine(ModuleStateDeliveryTransport transport)
        => transport == ModuleStateDeliveryTransport.ManagedModule
            ? "ManagedModule"
            : "PrivateModule";

    private static ModuleStateInstalledModule? SelectObservedModule(
        ModuleStatePlanAction action,
        string? sourceRepository,
        IEnumerable<ModuleStateInstalledModule> modules,
        string? version)
    {
        var candidates = modules;
        if (!string.IsNullOrWhiteSpace(version))
            candidates = candidates.Where(module => VersionsEqual(module.Version, version!));
        if (!string.IsNullOrWhiteSpace(action.TargetScope))
        {
            candidates = string.IsNullOrWhiteSpace(version)
                ? candidates.Where(module => string.Equals(module.Scope, action.TargetScope, StringComparison.OrdinalIgnoreCase))
                : candidates.Where(module =>
                    string.IsNullOrWhiteSpace(module.Scope) ||
                    string.Equals(module.Scope, action.TargetScope, StringComparison.OrdinalIgnoreCase));
        }

        var expectedRepository = action.TargetRepository ?? sourceRepository;
        if (!string.IsNullOrWhiteSpace(expectedRepository))
            candidates = candidates.Where(module => string.Equals(module.SourceRepository, expectedRepository, StringComparison.OrdinalIgnoreCase));

        return candidates
            .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var parsed) ? parsed : default)
            .FirstOrDefault();
    }

    private static bool VersionsEqual(string installedVersion, string receiptVersion)
    {
        if (ModuleStateVersion.TryParse(installedVersion, out var installed) &&
            ModuleStateVersion.TryParse(receiptVersion, out var expected))
        {
            return installed.CompareTo(expected) == 0;
        }

        return string.Equals(installedVersion, receiptVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FormatArguments(IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                yield return argument;
                continue;
            }

            yield return "'" + argument.Replace("'", "''") + "'";
        }
    }
}
