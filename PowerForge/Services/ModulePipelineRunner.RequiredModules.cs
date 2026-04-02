using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static bool IsAutoVersion(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);

    private static bool HasAutoRequiredModules(IEnumerable<RequiredModuleDraft> drafts)
        => new RequiredModuleResolutionEngine(new NullLogger())
            .HasAutoRequiredModules(ToRequiredModuleDraftDescriptors(drafts));

    private static bool AreRequiredModuleDraftListsEquivalent(
        IReadOnlyList<RequiredModuleDraft> left,
        IReadOnlyList<RequiredModuleDraft> right)
        => RequiredModuleResolutionEngine.AreDraftListsEquivalent(
            ToRequiredModuleDraftDescriptors(left),
            ToRequiredModuleDraftDescriptors(right));

    private RequiredModuleReference[] ResolveRequiredModules(
        IReadOnlyList<RequiredModuleDraft> drafts,
        bool resolveMissingModulesOnline,
        bool warnIfRequiredModulesOutdated,
        bool prerelease,
        string? repository,
        RepositoryCredential? credential)
    {
        var list = (drafts ?? Array.Empty<RequiredModuleDraft>())
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.ModuleName))
            .ToArray();
        if (list.Length == 0)
            return Array.Empty<RequiredModuleReference>();

        var moduleNames = list.Select(static d => d.ModuleName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var installed = TryGetLatestInstalledModuleInfo(moduleNames);
        var installedMetadata = installed.ToDictionary(
            static kvp => kvp.Key,
            static kvp => (kvp.Value.Version, kvp.Value.Guid),
            StringComparer.OrdinalIgnoreCase);

        Func<IReadOnlyCollection<string>, IReadOnlyDictionary<string, (string? Version, string? Guid)>>? onlineLookup = null;
        if (resolveMissingModulesOnline || warnIfRequiredModulesOutdated)
        {
            onlineLookup = candidates => TryResolveLatestOnlineVersions(candidates, repository, credential, prerelease);
        }

        var resolver = new RequiredModuleResolutionEngine(_logger);
        return resolver.ResolveRequiredModules(
            ToRequiredModuleDraftDescriptors(list),
            installedMetadata,
            onlineLookup,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated);
    }

    private static RequiredModuleReference[] ResolveOutputRequiredModules(
        RequiredModuleReference[] modules,
        bool mergeMissing,
        IReadOnlyCollection<string> approvedModules)
        => RequiredModuleResolutionEngine.ResolveOutputRequiredModules(modules, mergeMissing, approvedModules);

    private static RequiredModuleDraftDescriptor[] ToRequiredModuleDraftDescriptors(IEnumerable<RequiredModuleDraft> drafts)
    {
        return (drafts ?? Array.Empty<RequiredModuleDraft>())
            .Where(static draft => draft is not null && !string.IsNullOrWhiteSpace(draft.ModuleName))
            .Select(static draft => new RequiredModuleDraftDescriptor(
                draft.ModuleName,
                draft.ModuleVersion,
                draft.MinimumVersion,
                draft.RequiredVersion,
                draft.Guid))
            .ToArray();
    }

}
