using System;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

internal sealed class ModuleStateManagedPlanLicenseEnricher
{
    private readonly PSCmdlet _cmdlet;

    internal ModuleStateManagedPlanLicenseEnricher(PSCmdlet cmdlet)
        => _cmdlet = cmdlet ?? throw new ArgumentNullException(nameof(cmdlet));

    internal void Enrich(ModuleStatePlanResult plan, ModuleStateManagedDeliveryOptions options)
        => EnrichAsync(plan, options).GetAwaiter().GetResult();

    internal async Task EnrichAsync(
        ModuleStatePlanResult plan,
        ModuleStateManagedDeliveryOptions options,
        CancellationToken cancellationToken = default)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var actions = (plan.Actions ?? Array.Empty<ModuleStatePlanActionResult>())
            .Where(static action => action.Kind is "Install" or "Update" or "Save")
            .ToArray();
        if (actions.Length == 0)
            return;

        var logger = new CmdletLogger(_cmdlet, _cmdlet.MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var installService = new ManagedModuleInstallService(logger);
        var updateService = new ManagedModuleUpdateService(logger);

        foreach (var action in actions)
        {
            try
            {
                var repository = ResolveRepository(action, options);
                if (string.Equals(action.Kind, "Update", StringComparison.OrdinalIgnoreCase))
                {
                    var updatePlan = await updateService.PlanUpdateAsync(CreateUpdateRequest(repository, action, options), cancellationToken).ConfigureAwait(false);
                    ApplyLicense(action, updatePlan.License, updatePlan.LicenseAcceptanceRequired, updatePlan.LicenseAccepted);
                }
                else
                {
                    var installPlan = await installService.PlanInstallAsync(CreateInstallRequest(repository, action, options), cancellationToken).ConfigureAwait(false);
                    ApplyLicense(action, installPlan.License, installPlan.LicenseAcceptanceRequired, installPlan.LicenseAccepted);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or UriFormatException or ManagedModuleRepositoryException)
            {
                logger.Verbose($"Managed module plan license metadata was not available for '{action.ModuleName}': {ex.Message}");
            }
        }
    }

    private static void ApplyLicense(
        ModuleStatePlanActionResult action,
        string? license,
        bool licenseAcceptanceRequired,
        bool licenseAccepted)
    {
        action.License = license;
        action.LicenseAcceptanceRequired = licenseAcceptanceRequired;
        action.LicenseAccepted = licenseAccepted;
    }

    private static ManagedModuleInstallRequest CreateInstallRequest(
        ManagedModuleRepository repository,
        ModuleStatePlanActionResult action,
        ModuleStateManagedDeliveryOptions options)
    {
        var versionPolicy = ResolveVersionPolicy(action.VersionPolicy);
        return new ManagedModuleInstallRequest
        {
            Repository = repository,
            Name = action.ModuleName,
            Version = versionPolicy.ExactVersion,
            VersionPolicy = versionPolicy.RangePolicy,
            IncludePrerelease = options.Prerelease,
            Scope = ResolveScope(action.TargetScope, action.TargetPath, options.ModuleRoot),
            ModuleRoot = ResolveModuleRoot(action, options),
            ExpectedPackageSha256 = action.ExpectedPackageSha256,
            Credential = options.Credential,
            Force = options.Force || action.Force,
            AllowClobber = options.AllowClobber,
            AcceptLicense = options.AcceptLicense
        };
    }

    private static ManagedModuleUpdateRequest CreateUpdateRequest(
        ManagedModuleRepository repository,
        ModuleStatePlanActionResult action,
        ModuleStateManagedDeliveryOptions options)
    {
        var versionPolicy = ResolveVersionPolicy(action.VersionPolicy);
        return new ManagedModuleUpdateRequest
        {
            Repository = repository,
            Name = action.ModuleName,
            Version = versionPolicy.ExactVersion,
            VersionPolicy = versionPolicy.RangePolicy,
            IncludePrerelease = options.Prerelease,
            Scope = ResolveScope(action.TargetScope, action.TargetPath, options.ModuleRoot),
            ModuleRoot = ResolveModuleRoot(action, options),
            ExpectedPackageSha256 = action.ExpectedPackageSha256,
            Credential = options.Credential,
            Force = options.Force || action.Force,
            AllowClobber = options.AllowClobber,
            AcceptLicense = options.AcceptLicense,
            SourcePolicy = action.IsRepair ? new ManagedModuleSourcePolicy() : null
        };
    }

    private ManagedModuleRepository ResolveRepository(
        ModuleStatePlanActionResult action,
        ModuleStateManagedDeliveryOptions options)
        => ModuleStateManagedRepositoryResolver.ResolveRepositoryForAction(
            _cmdlet,
            action.TargetRepository,
            options,
            "Managed module license preflight requires Repository, ProfileName, or action target repository.");

    private static string? ResolveModuleRoot(ModuleStatePlanActionResult action, ModuleStateManagedDeliveryOptions options)
        => string.IsNullOrWhiteSpace(action.TargetPath) ? options.ModuleRoot : action.TargetPath;

    private static ManagedModuleInstallScope ResolveScope(string? scope, string? targetPath, string? moduleRoot)
        => !string.IsNullOrWhiteSpace(targetPath) || !string.IsNullOrWhiteSpace(moduleRoot)
            ? ManagedModuleInstallScope.Custom
            : string.Equals(scope, "AllUsers", StringComparison.OrdinalIgnoreCase)
                ? ManagedModuleInstallScope.AllUsers
                : ManagedModuleInstallScope.CurrentUser;

    private static ModuleStateManagedVersionPolicy ResolveVersionPolicy(string? versionPolicy)
    {
        if (string.IsNullOrWhiteSpace(versionPolicy) || string.Equals(versionPolicy!.Trim(), "*", StringComparison.Ordinal))
            return ModuleStateManagedVersionPolicy.Latest;

        var trimmed = versionPolicy!.Trim();
        if (trimmed.StartsWith("=", StringComparison.Ordinal))
            return new ModuleStateManagedVersionPolicy(trimmed.Substring(1).Trim(), null);
        if (ModuleStateVersion.TryParse(trimmed, out var exactVersion))
            return new ModuleStateManagedVersionPolicy(exactVersion.Normalized, null);

        return new ModuleStateManagedVersionPolicy(null, trimmed);
    }
}
