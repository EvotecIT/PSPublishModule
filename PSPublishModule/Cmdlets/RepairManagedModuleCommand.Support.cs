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
    private void ValidateVersionPolicy()
    {
        if (Latest.IsPresent &&
            (!string.IsNullOrWhiteSpace(Version) ||
             !string.IsNullOrWhiteSpace(MinimumVersion) ||
             !string.IsNullOrWhiteSpace(VersionPolicy)))
        {
            throw new InvalidOperationException("Latest cannot be combined with Version, MinimumVersion, or VersionPolicy.");
        }

        if (!string.IsNullOrWhiteSpace(Version) &&
            (!string.IsNullOrWhiteSpace(MinimumVersion) || !string.IsNullOrWhiteSpace(VersionPolicy)))
        {
            throw new InvalidOperationException("Version cannot be combined with MinimumVersion or VersionPolicy.");
        }

        if (!string.IsNullOrWhiteSpace(MinimumVersion) && !string.IsNullOrWhiteSpace(VersionPolicy))
            throw new InvalidOperationException("MinimumVersion cannot be combined with VersionPolicy.");
    }

    private void ApplyLatestUpdateIntent(ModuleStatePlanResult plan)
    {
        if (!Latest.IsPresent || plan.Actions is null)
            return;

        foreach (var action in plan.Actions)
        {
            if (!string.Equals(action.Kind, ModuleStatePlanActionKind.NoAction.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsExactVersionPolicy(action.VersionPolicy))
                continue;

            action.Kind = ModuleStatePlanActionKind.Update.ToString();
            action.Reason = "Latest requested; update delivery will keep the module unchanged when the repository has no newer version.";
        }
    }

    private static bool IsExactVersionPolicy(string? versionPolicy)
        => !string.IsNullOrWhiteSpace(versionPolicy) &&
           versionPolicy!.Trim().StartsWith("=", StringComparison.Ordinal);

    private static string? ResolveActionDeliveryRepository(ModuleStatePlanActionResult action)
        => string.IsNullOrWhiteSpace(action.TargetRepositorySource)
            ? action.TargetRepository
            : action.TargetRepositorySource;

    private void ApplyForceRepairIntent(ModuleStatePlanResult plan, bool inheritGlobalForce = true)
    {
        if (!inheritGlobalForce || !Force.IsPresent || Latest.IsPresent || plan.Actions is null)
            return;

        foreach (var action in plan.Actions)
        {
            if (!string.Equals(action.Kind, ModuleStatePlanActionKind.NoAction.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            action.Kind = ModuleStatePlanActionKind.Update.ToString();
            action.IsRepair = true;
            action.Force = true;
            action.Reason = "Force requested; repair delivery will reinstall the selected module version.";
        }
    }

    private string ResolveFilePath(string path, string parameterName)
    {
        var resolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
        if (!File.Exists(resolved))
            throw new FileNotFoundException($"The {parameterName} file was not found.", resolved);

        return resolved;
    }

    private string[] ResolveOptionalFilePaths(string[]? paths, string parameterName)
        => (paths ?? Array.Empty<string>())
            .Select(path => ResolveFilePath(path, parameterName))
            .ToArray();

    private string? ResolveRepositoryName()
    {
        if (!string.IsNullOrWhiteSpace(Repository))
            return ResolveRepositoryName(Repository);
        if (!string.IsNullOrWhiteSpace(ProfileName))
            return ModuleRepositoryProfileCommandSupport.TryResolve(ProfileName)?.RepositoryName;
        return null;
    }

    private string? ResolveRepositorySource()
        => ResolveRepositorySource(Repository);

    private string? ResolveRepositorySource(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return null;

        var trimmed = repository!.Trim();
        if (ModuleStateManagedRepositoryResolver.IsRepositorySource(trimmed))
            return trimmed;

        var providerPath = ManagedModuleCommandSupport.ResolveProviderPath(this, trimmed);
        return !string.IsNullOrWhiteSpace(providerPath) && Directory.Exists(providerPath)
            ? providerPath
            : null;
    }

    private string? ResolveRepositoryName(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return null;

        var source = ResolveRepositorySource(repository);
        return ModuleStateManagedRepositoryResolver.ResolveRepositoryIdentity(this, source ?? repository!);
    }

    private string? ResolveProfileRepositoryName()
        => string.IsNullOrWhiteSpace(ProfileName)
            ? null
            : ModuleRepositoryProfileCommandSupport.TryResolve(ProfileName)?.RepositoryName;

    private static ModuleStateCleanupMode ParseCleanupMode(string? cleanup)
        => string.Equals(cleanup, "OldVersions", StringComparison.OrdinalIgnoreCase)
            ? ModuleStateCleanupMode.OldVersions
            : ModuleStateCleanupMode.None;

    private static ManagedModuleInstallScope ParseInstallScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return ManagedModuleInstallScope.CurrentUser;
        if (Enum.TryParse<ManagedModuleInstallScope>(scope, ignoreCase: true, out var parsed))
            return parsed;

        throw new InvalidOperationException($"Unsupported scope '{scope}'.");
    }
}
