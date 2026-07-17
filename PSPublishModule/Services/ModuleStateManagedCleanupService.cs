using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal sealed class ModuleStateManagedCleanupService
{
    private readonly PSCmdlet _cmdlet;

    internal ModuleStateManagedCleanupService(PSCmdlet cmdlet)
        => _cmdlet = cmdlet ?? throw new ArgumentNullException(nameof(cmdlet));

    internal ModuleStateDeliveryExecutionResult[] Execute(
        ModuleStatePlan plan,
        ModuleStateInventoryResult inventory,
        ModuleStateManagedDeliveryOptions options)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        if (inventory is null)
            throw new ArgumentNullException(nameof(inventory));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var actions = plan.Actions
            .Where(static action => action.Kind == ModuleStatePlanActionKind.Remove)
            .ToArray();
        if (actions.Length == 0)
            return Array.Empty<ModuleStateDeliveryExecutionResult>();

        if (plan.HasErrors)
            return new[] { CreateUnsafePlanFailure(plan) };

        var service = new ManagedModuleUninstallService();
        var prepared = new List<PreparedCleanupAction>(actions.Length);
        foreach (var action in actions)
        {
            try
            {
                var item = PrepareAction(service, action, inventory, options);
                if (prepared.Any(existing => ModuleStatePathIdentity.Equals(existing.Target.ModulePath, item.Target.ModulePath)))
                {
                    throw new InvalidOperationException(
                        $"Cleanup target '{item.Target.ModulePath}' was selected more than once. No cleanup was performed.");
                }

                prepared.Add(item);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new[] { CreateFailure(action, ex) };
            }
        }

        var ordered = ManagedModuleUninstallService.OrderTargetsForRemoval(prepared.Select(static item => item.Target).ToArray())
            .Select(target => prepared.Single(item => ReferenceEquals(item.Target, target)))
            .ToArray();
        var results = new List<ModuleStateDeliveryExecutionResult>(actions.Length);
        var selected = new List<PreparedCleanupAction>(actions.Length);
        foreach (var item in ordered)
        {
            if (ShouldProcess(
                    item.Action.TargetPath!,
                    $"Remove managed module '{item.Action.ModuleName}' version '{item.Action.InstalledVersion}'"))
            {
                selected.Add(item);
            }
            else
            {
                results.Add(CreateSkipped(item.Action));
            }
        }

        if (selected.Count == 0)
            return results.ToArray();

        var selectedTargets = selected.Select(static item => item.Target).ToArray();
        foreach (var item in selected)
            item.Plan.DependencyRemovalTargets = selectedTargets;

        foreach (var item in selected)
        {
            try
            {
                service.ValidateUninstallPlan(item.Plan);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(CreateFailure(item.Action, ex));
                return results.ToArray();
            }
        }

        foreach (var item in selected)
        {
            try
            {
                results.Add(ExecutePreparedAction(service, item));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(CreateFailure(item.Action, ex));
                break;
            }
        }

        return results.ToArray();
    }

    private static PreparedCleanupAction PrepareAction(
        ManagedModuleUninstallService service,
        ModuleStatePlanAction action,
        ModuleStateInventoryResult inventory,
        ModuleStateManagedDeliveryOptions options)
    {
        if (string.IsNullOrWhiteSpace(action.TargetPath) || string.IsNullOrWhiteSpace(action.TargetModuleRoot))
            throw new InvalidOperationException($"Cleanup action for '{action.ModuleName}' does not identify an exact installed location and module root.");

        var request = new ManagedModuleUninstallRequest
        {
            Name = new[] { action.ModuleName },
            Version = action.InstalledVersion,
            Scope = ManagedModuleInstallScope.Custom,
            ShellEdition = ParseShellEdition(action.TargetPowerShellEdition),
            ModuleRoot = action.TargetModuleRoot,
            InstalledLocation = action.TargetPath,
            DependencyModuleRoots = ResolveDependencyModuleRoots(action, inventory),
            SkipDependencyCheck = options.SkipDependencyCheck || action.SkipDependencyCheck,
            DeferDependencyCheck = true,
            LoadedModules = options.LoadedModules
        };
        var plan = service.PlanUninstall(request);
        if (plan.Targets.Count != 1 || !ModuleStatePathIdentity.Equals(plan.Targets[0].ModulePath, action.TargetPath))
        {
            throw new InvalidOperationException(
                $"Cleanup target '{action.TargetPath}' was not found exactly under module root '{action.TargetModuleRoot}'. The estate changed after planning; no cleanup was performed for this action.");
        }

        return new PreparedCleanupAction(action, plan, plan.Targets[0]);
    }

    private static ModuleStateDeliveryExecutionResult ExecutePreparedAction(
        ManagedModuleUninstallService service,
        PreparedCleanupAction item)
    {
        var uninstallResults = service.Uninstall(item.Plan);
        var result = uninstallResults.Single();
        return new ModuleStateDeliveryExecutionResult
        {
            Operation = "Remove",
            OperationPerformed = result.Status == ManagedModuleUninstallStatus.Uninstalled,
            TargetPath = result.ModulePath ?? item.Action.TargetPath,
            RequestedTransport = ModuleStateDeliveryTransport.ManagedModule,
            EffectiveTransport = ModuleStateDeliveryTransport.ManagedModule,
            DeliveryTransportReason = "ModuleState cleanup uses exact-path managed uninstall with loaded-module and dependency revalidation.",
            DependencyResults = new[]
            {
                new ModuleStateDependencyResult
                {
                    Name = result.Name,
                    InstalledVersion = result.Version,
                    ResolvedVersion = null,
                    RequestedVersion = item.Action.InstalledVersion,
                    Status = result.Status.ToString(),
                    Installer = "ManagedModule",
                    Message = result.ModulePath
                }
            }
        };
    }

    private static string[] ResolveDependencyModuleRoots(
        ModuleStatePlanAction action,
        ModuleStateInventoryResult inventory)
    {
        var paths = inventory.ScannedPaths ?? Array.Empty<ModuleStateInventoryPathResult>();
        return paths
            .Where(path => string.IsNullOrWhiteSpace(action.TargetPowerShellEdition) ||
                           string.IsNullOrWhiteSpace(path.PowerShellEdition) ||
                           string.Equals(path.PowerShellEdition, action.TargetPowerShellEdition, StringComparison.OrdinalIgnoreCase))
            .Where(path => string.IsNullOrWhiteSpace(action.TargetProfileName) ||
                           string.IsNullOrWhiteSpace(path.ProfileName) ||
                           string.Equals(path.ProfileName, action.TargetProfileName, StringComparison.OrdinalIgnoreCase))
            .Select(static path => path.Path)
            .Append(action.TargetModuleRoot!)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(ModuleStatePathIdentity.Comparer)
            .ToArray();
    }

    private bool ShouldProcess(string target, string action)
        => _cmdlet is AsyncPSCmdlet asyncCmdlet
            ? asyncCmdlet.ShouldProcess(target, action)
            : _cmdlet.ShouldProcess(target, action);

    private static ManagedModuleShellEdition ParseShellEdition(string? edition)
        => Enum.TryParse<ManagedModuleShellEdition>(edition, ignoreCase: true, out var parsed)
            ? parsed
            : ManagedModuleShellEdition.Auto;

    private static ModuleStateDeliveryExecutionResult CreateSkipped(ModuleStatePlanAction action)
        => new()
        {
            Skipped = true,
            Operation = "Remove",
            OperationPerformed = false,
            TargetPath = action.TargetPath,
            RequestedTransport = ModuleStateDeliveryTransport.ManagedModule,
            EffectiveTransport = ModuleStateDeliveryTransport.ManagedModule,
            DeliveryTransportReason = "ModuleState cleanup uses exact-path managed uninstall with loaded-module and dependency revalidation.",
            DependencyResults = new[]
            {
                new ModuleStateDependencyResult
                {
                    Name = action.ModuleName,
                    InstalledVersion = action.InstalledVersion,
                    RequestedVersion = action.InstalledVersion,
                    Status = "Skipped",
                    Installer = "ManagedModule",
                    Message = "ShouldProcess declined the exact cleanup target."
                }
            }
        };

    private static ModuleStateDeliveryExecutionResult CreateUnsafePlanFailure(ModuleStatePlan plan)
    {
        var errorFindings = plan.Findings
            .Where(static finding => finding.Severity == ModuleStateConflictSeverity.Error)
            .ToArray();
        var message = errorFindings.Length == 0
            ? "Post-delivery cleanup was blocked because the refreshed estate plan is unsafe."
            : "Post-delivery cleanup was blocked because the refreshed estate plan contains errors: " +
              string.Join("; ", errorFindings.Select(static finding => $"{finding.Code}: {finding.Message}"));

        return new ModuleStateDeliveryExecutionResult
        {
            Succeeded = false,
            ErrorMessage = message,
            Operation = "Cleanup",
            OperationPerformed = false,
            TargetPath = errorFindings.Select(static finding => finding.Path).FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path)),
            RequestedTransport = ModuleStateDeliveryTransport.ManagedModule,
            EffectiveTransport = ModuleStateDeliveryTransport.ManagedModule,
            DeliveryTransportReason = "ModuleState cleanup requires an error-free post-delivery estate plan before any exact-path removal.",
            DependencyResults = new[]
            {
                new ModuleStateDependencyResult
                {
                    Name = "Cleanup",
                    Status = "Failed",
                    Installer = "ManagedModule",
                    Message = message
                }
            }
        };
    }

    private static ModuleStateDeliveryExecutionResult CreateFailure(
        ModuleStatePlanAction action,
        Exception exception)
        => new()
        {
            Succeeded = false,
            ErrorMessage = exception.Message,
            Operation = "Remove",
            OperationPerformed = false,
            TargetPath = action.TargetPath,
            RequestedTransport = ModuleStateDeliveryTransport.ManagedModule,
            EffectiveTransport = ModuleStateDeliveryTransport.ManagedModule,
            DeliveryTransportReason = "ModuleState cleanup stopped after exact-path uninstall revalidation failed.",
            DependencyResults = new[]
            {
                new ModuleStateDependencyResult
                {
                    Name = action.ModuleName,
                    InstalledVersion = action.InstalledVersion,
                    RequestedVersion = action.InstalledVersion,
                    Status = "Failed",
                    Installer = "ManagedModule",
                    Message = exception.Message
                }
            }
        };

    private sealed class PreparedCleanupAction
    {
        internal PreparedCleanupAction(
            ModuleStatePlanAction action,
            ManagedModuleUninstallPlan plan,
            ManagedModuleUninstallTarget target)
        {
            Action = action;
            Plan = plan;
            Target = target;
        }

        internal ModuleStatePlanAction Action { get; }

        internal ManagedModuleUninstallPlan Plan { get; }

        internal ManagedModuleUninstallTarget Target { get; }
    }
}
