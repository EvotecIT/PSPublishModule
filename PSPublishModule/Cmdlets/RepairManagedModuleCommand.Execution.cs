using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

public sealed partial class RepairManagedModuleCommand : AsyncPSCmdlet
{
    private async Task<ModuleStateApplyResult> PrepareApplyAsync(
        ModuleStatePlanResult plan,
        ModuleStateInventoryResult inventory,
        string? deliveryModuleRoot,
        string? credentialSecretFilePath,
        ModuleStateManagedDeliveryOptions managedDeliveryOptions,
        bool requiredResourceInputSupplied,
        object desiredState,
        string[] maintenanceReceiptPaths)
    {
        var deliveryOptions = new ModuleStateDeliveryOptions(
            ProfileName,
            Repository,
            installPrerequisites: false,
            prerelease: !requiredResourceInputSupplied && Prerelease.IsPresent,
            force: !requiredResourceInputSupplied && Force.IsPresent,
            acceptLicense: !requiredResourceInputSupplied && AcceptLicense.IsPresent,
            allowErrorFindings: AllowConflict.IsPresent,
            allowClobber: !requiredResourceInputSupplied && AllowClobber.IsPresent,
            skipDependencyCheck: !requiredResourceInputSupplied && SkipDependencyCheck.IsPresent,
            moduleRoot: deliveryModuleRoot,
            transport: Transport,
            profileRepository: ResolveProfileRepositoryName());
        var service = new ModuleStateApplyService();
        var corePlan = ModuleStatePlanResultMapper.ToCorePlan(plan);
        var result = service.Prepare(corePlan, deliveryOptions);
        var executionResults = Array.Empty<ModuleStateDeliveryExecutionResult>();
        var executionRequested = false;
        ModuleStateInventoryResult? postApplyInventory = null;
        ModuleStatePlanResult? postApplyPlan = null;
        ModuleStateTestResult? postApplyTest = null;
        var hasActions = result.Plan.Actions.Any(static action => action.Kind != ModuleStatePlanActionKind.NoAction);

        if (!Plan.IsPresent && result.Receipt.CanApply && hasActions && ShouldProcess("managed module estate", "Repair managed modules"))
        {
            executionRequested = true;
            executionResults = await ExecuteDeliveryAsync(
                result,
                inventory,
                deliveryModuleRoot,
                credentialSecretFilePath,
                managedDeliveryOptions,
                requiredResourceInputSupplied).ConfigureAwait(false);
            if (executionResults.All(static execution => execution.Succeeded && !execution.Skipped))
            {
                var cleanupInventory = CollectPostApplyInventory(inventory);
                if (cleanupInventory is not null)
                {
                    var cleanupPlan = ModuleStatePlanCommandSupport.CreatePlanResult(
                        cleanupInventory,
                        CreateConvergenceDesiredState(desiredState),
                        maintenanceReceiptPaths,
                        repair: true,
                        ParseCleanupMode(Cleanup),
                        Family);
                    var cleanupResults = new ModuleStateManagedCleanupService(this).Execute(
                        ModuleStatePlanResultMapper.ToCorePlan(cleanupPlan),
                        cleanupInventory,
                        managedDeliveryOptions);
                    executionResults = executionResults.Concat(cleanupResults).ToArray();
                }
            }
            CollectPostApplyEvidence(
                inventory,
                desiredState,
                maintenanceReceiptPaths,
                out postApplyInventory,
                out postApplyPlan,
                out postApplyTest);
        }

        var executionSucceeded = executionRequested && executionResults.All(static execution => execution.Succeeded && !execution.Skipped);
        var converged = executionSucceeded && postApplyTest?.IsCompliant == true;
        if (!Plan.IsPresent && result.Receipt.CanApply && !hasActions)
        {
            CollectPostApplyEvidence(
                inventory,
                desiredState,
                maintenanceReceiptPaths,
                out postApplyInventory,
                out postApplyPlan,
                out postApplyTest);
            executionSucceeded = true;
            converged = postApplyTest?.IsCompliant == true;
        }

        return ModuleStateApplyResultMapper.ToCmdletResult(
            result,
            receiptPath: null,
            maintenanceReceiptOutputPath: null,
            executionRequested,
            executionResults,
            postApplyInventory,
            postApplyPlan,
            postApplyTest,
            executionSucceeded,
            converged);
    }

    private void CollectPostApplyEvidence(
        ModuleStateInventoryResult inventory,
        object desiredState,
        string[] maintenanceReceiptPaths,
        out ModuleStateInventoryResult? postApplyInventory,
        out ModuleStatePlanResult? postApplyPlan,
        out ModuleStateTestResult? postApplyTest)
    {
        postApplyInventory = CollectPostApplyInventory(inventory);
        postApplyPlan = null;
        postApplyTest = null;
        if (postApplyInventory is null)
            return;

        postApplyPlan = ModuleStatePlanCommandSupport.CreatePlanResult(
            postApplyInventory,
            CreateConvergenceDesiredState(desiredState),
            maintenanceReceiptPaths,
            repair: true,
            ParseCleanupMode(Cleanup),
            Family);
        postApplyTest = ModuleStateTestResult.FromPlan(postApplyPlan);
    }

    private static ModuleStateInventoryResult? CollectPostApplyInventory(ModuleStateInventoryResult inventory)
    {
        var pathEntries = inventory.ScannedPaths is { Length: > 0 }
            ? inventory.ScannedPaths.Select(static path => new ModuleStateModulePath(
                path.Path,
                path.PowerShellEdition,
                path.Scope,
                path.ProfileName,
                path.IsRequired,
                dependencyVisibilityGroup: path.DependencyVisibilityGroup)).ToArray()
            : (inventory.ModulePaths ?? Array.Empty<string>())
                .Select(static path => new ModuleStateModulePath(path, isRequired: true))
                .ToArray();
        if (pathEntries.Length == 0)
        {
            pathEntries = (inventory.InstalledModules ?? Array.Empty<ModuleStateInstalledModuleResult>())
                .Select(static module => module.ModuleRoot ?? ResolveSelectedModuleRoot(module))
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(ModuleStatePathIdentity.Comparer)
                .Select(static path => new ModuleStateModulePath(path!, isRequired: true))
                .ToArray();
        }

        if (pathEntries.Length == 0)
            return null;

        var loadedModules = (inventory.InstalledModules ?? Array.Empty<ModuleStateInstalledModuleResult>())
            .Where(static module => module.IsLoaded)
            .Select(static module => new ModuleStateLoadedModuleEvidence(module.Name, module.Version, module.Path))
            .ToArray();
        return ModuleStateInventoryCommandSupport.CreateInventoryResultFromModulePathEntries(
            pathEntries,
            loadedModules,
            source: "PostApply");
    }

    private static object CreateConvergenceDesiredState(object desiredState)
    {
        if (desiredState is not IDictionary desiredDictionary)
            return desiredState;

        var result = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in desiredDictionary)
        {
            var entryKey = entry.Key;
            if (entryKey is null)
                continue;
            if (!string.Equals(entryKey.ToString(), "Modules", StringComparison.OrdinalIgnoreCase) || entry.Value is not IEnumerable modules)
            {
                result[entryKey] = entry.Value;
                continue;
            }

            var normalizedModules = new ArrayList();
            foreach (var moduleValue in modules)
            {
                if (moduleValue is not IDictionary module)
                {
                    normalizedModules.Add(moduleValue);
                    continue;
                }

                var normalizedModule = new Hashtable(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry moduleEntry in module)
                {
                    var moduleEntryKey = moduleEntry.Key;
                    if (moduleEntryKey is null)
                        continue;
                    var key = moduleEntryKey.ToString();
                    if (string.Equals(key, "Reinstall", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "Force", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "ExpectedPackageSha256", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "PackageSha256", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "Sha256", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    normalizedModule[moduleEntryKey] = moduleEntry.Value;
                }

                normalizedModules.Add(normalizedModule);
            }

            result[entryKey] = normalizedModules;
        }

        return result;
    }

    private async Task<ModuleStateDeliveryExecutionResult[]> ExecuteDeliveryAsync(
        PowerForge.ModuleStateApplyResult result,
        ModuleStateInventoryResult inventory,
        string? deliveryModuleRoot,
        string? credentialSecretFilePath,
        ModuleStateManagedDeliveryOptions managedDeliveryOptions,
        bool requiredResourceInputSupplied)
    {
        ModuleStateDeliveryExecutionResult[] deliveryResults;
        if (Transport == ModuleStateDeliveryTransport.ManagedModule)
        {
            deliveryResults = await new ModuleStateManagedDeliveryService(this).ExecuteForRepairAsync(
                result,
                managedDeliveryOptions,
                CancelToken).ConfigureAwait(false);
        }
        else
        {
            try
            {
                deliveryResults = new ModuleStatePrivateDeliveryService(this).Execute(
                    result,
                    new ModuleStatePrivateDeliveryOptions
                    {
                        ProfileName = ProfileName,
                        Repository = Repository,
                        InstallPrerequisites = false,
                        Prerelease = !requiredResourceInputSupplied && Prerelease.IsPresent,
                        Force = !requiredResourceInputSupplied && Force.IsPresent,
                        DeliveryTransport = Transport,
                        CredentialUserName = Credential?.UserName ?? CredentialUserName,
                        CredentialSecret = Credential?.GetNetworkCredential().Password ?? CredentialSecret,
                        CredentialSecretFilePath = credentialSecretFilePath,
                        PromptForCredential = false,
                        ManagedModuleRoot = deliveryModuleRoot,
                        ManagedAllowClobber = !requiredResourceInputSupplied && AllowClobber.IsPresent,
                        ManagedAcceptLicense = !requiredResourceInputSupplied && AcceptLicense.IsPresent,
                        ManagedSkipDependencyCheck = !requiredResourceInputSupplied && SkipDependencyCheck.IsPresent,
                        LoadedModules = ResolveLoadedModules(inventory)
                    });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                deliveryResults = new[] { CreateExecutionFailure("Delivery", ex) };
            }
        }

        return deliveryResults;
    }

    private static ModuleStateDeliveryExecutionResult CreateExecutionFailure(string operation, Exception exception)
        => new()
        {
            Succeeded = false,
            ErrorMessage = exception.Message,
            Operation = operation,
            OperationPerformed = false,
            RequestedTransport = ModuleStateDeliveryTransport.PrivateModule,
            EffectiveTransport = ModuleStateDeliveryTransport.PrivateModule,
            DeliveryTransportReason = "ModuleState repair delivery stopped after an operational failure.",
            DependencyResults = new[]
            {
                new ModuleStateDependencyResult
                {
                    Name = operation,
                    Status = "Failed",
                    Installer = "PrivateModule",
                    Message = exception.Message
                }
            }
        };

    private async Task EnrichManagedLicenseMetadataAsync(
        ModuleStatePlanResult plan,
        ModuleStateManagedDeliveryOptions managedDeliveryOptions)
    {
        if (Transport != ModuleStateDeliveryTransport.ManagedModule)
            return;

        try
        {
            await new ModuleStateManagedPlanLicenseEnricher(this)
                .EnrichAsync(plan, managedDeliveryOptions, CancelToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or UriFormatException)
        {
            WriteVerbose("Managed module license preflight skipped: " + ex.Message);
        }
    }

    private bool PreResolveManagedDeliveryRepositories(
        ModuleStatePlanResult plan,
        ModuleStateManagedDeliveryOptions options,
        bool required)
    {
        if (Transport != ModuleStateDeliveryTransport.ManagedModule)
            return true;

        var actions = (plan.Actions ?? Array.Empty<ModuleStatePlanActionResult>())
            .Where(static action => action.Kind is "Install" or "Update" or "Save")
            .ToArray();
        if (actions.Length == 0)
            return true;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var action in actions)
            {
                var actionRepository = ResolveActionDeliveryRepository(action);
                var key = ModuleStateManagedRepositoryResolver.CreateRepositoryKey(actionRepository);
                if (!seen.Add(key))
                    continue;
                if (key.Length == 0 &&
                    string.IsNullOrWhiteSpace(options.Repository) &&
                    string.IsNullOrWhiteSpace(options.ProfileName))
                {
                    continue;
                }

                options.ResolvedRepositories[key] = ModuleStateManagedRepositoryResolver.ResolveRepositoryForAction(
                    this,
                    actionRepository,
                    options,
                    "Managed module delivery requires Repository, ProfileName, or action target repository.");
            }

            return true;
        }
        catch (Exception ex) when (!required &&
                                   (ex is InvalidOperationException or ArgumentException or NotSupportedException or UriFormatException))
        {
            WriteVerbose("Managed module license preflight skipped: " + ex.Message);
            return false;
        }
    }

    private ModuleStateManagedDeliveryOptions CreateManagedDeliveryOptions(
        ModuleStateInventoryResult? inventory = null,
        string? moduleRoot = null,
        RepositoryCredential? credential = null,
        bool inheritResourceDefaults = true)
        => new()
        {
            ProfileName = ProfileName,
            Repository = Repository,
            Prerelease = inheritResourceDefaults && Prerelease.IsPresent,
            Force = inheritResourceDefaults && Force.IsPresent,
            AllowClobber = inheritResourceDefaults && AllowClobber.IsPresent,
            AcceptLicense = inheritResourceDefaults && AcceptLicense.IsPresent,
            SkipDependencyCheck = inheritResourceDefaults && SkipDependencyCheck.IsPresent,
            ModuleRoot = moduleRoot,
            Credential = credential,
            LoadedModules = ResolveLoadedModules(inventory)
        };

    private RepositoryCredential? ResolveRepositoryCredential(string? credentialSecretFilePath)
        => ManagedModuleCommandSupport.ResolveCredential(
            this,
            Credential,
            CredentialUserName,
            CredentialSecret,
            credentialSecretFilePath);

    private bool ShouldResolveManagedCredential(ModuleStatePlanResult plan)
        => Transport == ModuleStateDeliveryTransport.ManagedModule &&
           HasDeliveryActions(plan);

    private static bool HasDeliveryActions(ModuleStatePlanResult plan)
        => (plan.Actions ?? Array.Empty<ModuleStatePlanActionResult>())
            .Any(static action => action.Kind is "Install" or "Update" or "Save");

    private string? ResolveCredentialSecretFilePath()
        => ManagedModuleCommandSupport.ResolveProviderPath(this, CredentialSecretFilePath);

    private string? ResolveManagedDeliveryModuleRoot()
    {
        if (!string.IsNullOrWhiteSpace(ModuleRoot))
            return ManagedModuleCommandSupport.ResolveProviderPath(this, ModuleRoot);

        return ModulePath is { Length: 1 }
            ? ManagedModuleCommandSupport.ResolveProviderPath(this, ModulePath[0])
            : null;
    }

    private ManagedModuleLoadedModule[] ResolveLoadedModules(ModuleStateInventoryResult? inventory)
    {
        var inventoryLoaded = (inventory?.InstalledModules ?? Array.Empty<ModuleStateInstalledModuleResult>())
            .Where(static module => module.IsLoaded)
            .Select(static module => new ManagedModuleLoadedModule
            {
                Name = module.Name,
                Version = module.Version,
                Path = module.Path,
                ModuleBase = module.Path
            });
        var sessionLoaded = ModuleStateInventoryCommandSupport.GetLoadedModules(this)
            .Select(static module => new ManagedModuleLoadedModule
            {
                Name = module.Name ?? string.Empty,
                Version = module.Version,
                Path = module.Path,
                ModuleBase = module.Path
            });
        return inventoryLoaded
            .Concat(sessionLoaded)
            .Where(static module => !string.IsNullOrWhiteSpace(module.Name))
            .GroupBy(CreateLoadedModuleIdentity, ModuleStatePathIdentity.Comparer)
            .Select(static group => group.First())
            .ToArray();
    }

    private static string CreateLoadedModuleIdentity(ManagedModuleLoadedModule module)
    {
        var path = module.Path ?? module.ModuleBase;
        return string.Join(
            "|",
            module.Name.ToUpperInvariant(),
            (module.Version ?? string.Empty).ToUpperInvariant(),
            string.IsNullOrWhiteSpace(path) ? string.Empty : ModuleStatePathIdentity.Normalize(path!));
    }
}
