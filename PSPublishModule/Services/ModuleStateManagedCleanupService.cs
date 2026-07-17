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

        var service = new ManagedModuleUninstallService();
        var results = new List<ModuleStateDeliveryExecutionResult>(actions.Length);
        foreach (var action in actions)
        {
            try
            {
                results.Add(ExecuteAction(service, action, inventory, options));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(CreateFailure(action, ex));
                break;
            }
        }

        return results.ToArray();
    }

    private ModuleStateDeliveryExecutionResult ExecuteAction(
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
            LoadedModules = options.LoadedModules
        };
        var plan = service.PlanUninstall(request);
        if (plan.Targets.Count != 1 || !ModuleStatePathIdentity.Equals(plan.Targets[0].ModulePath, action.TargetPath))
        {
            throw new InvalidOperationException(
                $"Cleanup target '{action.TargetPath}' was not found exactly under module root '{action.TargetModuleRoot}'. The estate changed after planning; no cleanup was performed for this action.");
        }

        if (!ShouldProcess(action.TargetPath!, $"Remove managed module '{action.ModuleName}' version '{action.InstalledVersion}'"))
            return CreateSkipped(action);

        var uninstallResults = service.Uninstall(plan);
        var result = uninstallResults.Single();
        return new ModuleStateDeliveryExecutionResult
        {
            Operation = "Remove",
            OperationPerformed = result.Status == ManagedModuleUninstallStatus.Uninstalled,
            TargetPath = result.ModulePath ?? action.TargetPath,
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
                    RequestedVersion = action.InstalledVersion,
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
                           string.Equals(path.PowerShellEdition, action.TargetPowerShellEdition, StringComparison.OrdinalIgnoreCase))
            .Where(path => string.IsNullOrWhiteSpace(action.TargetProfileName)
                ? string.IsNullOrWhiteSpace(path.ProfileName)
                : string.IsNullOrWhiteSpace(path.ProfileName) ||
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
}
