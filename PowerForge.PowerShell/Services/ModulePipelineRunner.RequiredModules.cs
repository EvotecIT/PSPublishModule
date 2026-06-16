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

    private static bool HasOnlineResolvableAutoRequiredModules(IEnumerable<RequiredModuleDraft> drafts)
        => HasAutoRequiredModules((drafts ?? Array.Empty<RequiredModuleDraft>())
            .Where(static draft => draft?.VersionSource != ModuleDependencyVersionSource.Installed));

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
        RepositoryCredential? credential,
        DependencyVersionSourceRepository? publishVersionSource = null)
    {
        var list = (drafts ?? Array.Empty<RequiredModuleDraft>())
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.ModuleName))
            .ToArray();
        if (list.Length == 0)
            return Array.Empty<RequiredModuleReference>();

        var moduleNames = list.Select(static d => d.ModuleName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var installed = _moduleDependencyMetadataProvider.GetLatestInstalledModules(moduleNames);
        var installedMetadata = installed.ToDictionary(
            static kvp => kvp.Key,
            static kvp => (kvp.Value.Version, kvp.Value.Guid),
            StringComparer.OrdinalIgnoreCase);

        if (publishVersionSource is not null ||
            list.Any(static draft => draft.VersionSource != ModuleDependencyVersionSource.Auto))
        {
            var resolvedByName = new Dictionary<string, RequiredModuleReference>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in list.GroupBy(draft => ResolveDependencyVersionSource(draft.VersionSource, publishVersionSource)))
            {
                var source = group.Key;
                var lookupRepository = source.PreferOnlineMetadata ? source.Repository : repository;
                var lookupCredential = source.PreferOnlineMetadata ? source.Credential : credential;
                var allowOnlineLookup = source.AllowOnlineLookup &&
                                        (resolveMissingModulesOnline || source.PreferOnlineMetadata);
                var groupResult = ResolveRequiredModulesFromSingleSource(
                    group.ToArray(),
                    installedMetadata,
                    allowOnlineLookup,
                    warnIfRequiredModulesOutdated,
                    prerelease,
                    lookupRepository,
                    lookupCredential,
                    source.PreferOnlineMetadata);

                foreach (var module in groupResult)
                {
                    if (!string.IsNullOrWhiteSpace(module.ModuleName))
                        resolvedByName[module.ModuleName] = module;
                }
            }

            return list
                .Where(static draft => !string.IsNullOrWhiteSpace(draft.ModuleName))
                .Select(draft => resolvedByName.TryGetValue(draft.ModuleName, out var module)
                    ? module
                    : new RequiredModuleReference(draft.ModuleName))
                .ToArray();
        }

        return ResolveRequiredModulesFromSingleSource(
            list,
            installedMetadata,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            prerelease,
            repository,
            credential,
            preferOnlineMetadata: false);
    }

    private static string[] NormalizeApprovedModules(IEnumerable<string> approvedModules)
        => (approvedModules ?? Array.Empty<string>())
            .Where(static module => !string.IsNullOrWhiteSpace(module))
            .Select(static module => module.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void ApplyMergeDefaultsForPlan(
        bool refreshPsd1Only,
        string? csproj,
        IReadOnlyCollection<string> approved,
        bool mergeModuleSet,
        bool mergeMissingSet,
        ref bool mergeModule,
        ref bool mergeMissing)
    {
        if (refreshPsd1Only)
        {
            if (!string.IsNullOrWhiteSpace(csproj))
                _logger.Info("RefreshPSD1Only enabled: skipping .NET publish/binary rebuild for this run.");

            if (mergeModule)
                _logger.Info("RefreshPSD1Only enabled: disabling merge for this run.");
            mergeModule = false;
            mergeMissing = false;
            return;
        }

        if (!mergeModuleSet)
        {
            mergeModule = true;
            _logger.Info("MergeModule not explicitly set; enabling by default for legacy compatibility.");
        }

        if (!mergeMissingSet && !mergeMissing && approved.Count > 0)
        {
            mergeMissing = true;
            var context = mergeModule ? "and MergeModule is enabled" : "for approved-module inlining";
            _logger.Info($"MergeMissing not explicitly set; enabling because approved modules are configured {context}.");
        }
    }

    private RequiredModuleSetResolution ResolveRequiredModuleSets(
        IReadOnlyList<RequiredModuleDraft> requiredModulesDraft,
        IReadOnlyList<RequiredModuleDraft> requiredModulesDraftForPackaging,
        string[] approved,
        bool mergeMissing,
        ImportModulesConfiguration? importModules,
        string[] compatible,
        bool resolveMissingModulesOnline,
        bool warnIfRequiredModulesOutdated,
        bool prerelease,
        string? repository,
        RepositoryCredential? credential,
        DependencyVersionSourceRepository? publishVersionSource = null)
    {
        var requiredSourceDrafts = BuildRequiredModuleDraftMap(requiredModulesDraft);
        var requiredPackagingSourceDrafts = BuildRequiredModuleDraftMap(requiredModulesDraftForPackaging);

        var requiredModules = ResolveRequiredModules(
            requiredModulesDraft,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            prerelease,
            repository,
            credential,
            publishVersionSource);
        if (mergeMissing && approved.Length > 0)
        {
            var approvedRequiredRoots = approved
                .Where(requiredSourceDrafts.ContainsKey)
                .ToArray();
            requiredModules = IncludeTransitiveRequiredModules(
                requiredModules,
                approvedRequiredRoots,
                requiredSourceDrafts,
                resolveMissingModulesOnline,
                warnIfRequiredModulesOutdated,
                prerelease,
                repository,
                credential,
                publishVersionSource);
        }

        if (importModules?.PreferBinaryConflictOrder == true)
            requiredModules = ReorderRequiredModulesForBinaryConflicts(requiredModules, compatible);

        var requiredModulesForPackaging = AreRequiredModuleDraftListsEquivalent(requiredModulesDraft, requiredModulesDraftForPackaging)
            ? requiredModules.ToArray()
            : ResolveRequiredModules(
                requiredModulesDraftForPackaging,
                resolveMissingModulesOnline,
                warnIfRequiredModulesOutdated,
                prerelease,
                repository,
                credential,
                publishVersionSource);

        var requiredPackagingRoots = requiredModulesDraftForPackaging
            .Select(static draft => draft.ModuleName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        requiredModulesForPackaging = IncludeTransitiveRequiredModules(
            requiredModulesForPackaging,
            requiredPackagingRoots,
            requiredPackagingSourceDrafts,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            prerelease,
            repository,
            credential,
            publishVersionSource);

        return new RequiredModuleSetResolution(requiredModules, requiredModulesForPackaging);
    }

    private static Dictionary<string, RequiredModuleDraft> BuildRequiredModuleDraftMap(IEnumerable<RequiredModuleDraft> drafts)
        => (drafts ?? Array.Empty<RequiredModuleDraft>())
            .Where(static draft => draft is not null && !string.IsNullOrWhiteSpace(draft.ModuleName))
            .GroupBy(static draft => draft.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);

    private RequiredModuleReference[] IncludeTransitiveRequiredModules(
        RequiredModuleReference[] modules,
        IEnumerable<string> rootModules,
        IReadOnlyDictionary<string, RequiredModuleDraft> sourceDrafts,
        bool resolveMissingModulesOnline,
        bool warnIfRequiredModulesOutdated,
        bool prerelease,
        string? repository,
        RepositoryCredential? credential,
        DependencyVersionSourceRepository? publishVersionSource = null)
    {
        var output = (modules ?? Array.Empty<RequiredModuleReference>())
            .Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.ModuleName))
            .ToList();
        var known = new HashSet<string>(
            output.Select(static module => module.ModuleName),
            StringComparer.OrdinalIgnoreCase);

        var discovered = new List<(RequiredModuleReference Reference, ModuleDependencyVersionSource VersionSource)>();
        var discoveredIndex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rootsByName = output
            .GroupBy(static module => module.ModuleName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var root in NormalizeStringArray(rootModules))
        {
            var source = ResolveInheritedDependencyVersionSource(root, sourceDrafts, ModuleDependencyVersionSource.Auto);
            var rootReference = rootsByName.TryGetValue(root, out var resolvedRoot)
                ? resolvedRoot
                : new RequiredModuleReference(root);
            CollectTransitiveRequiredModules(rootReference, source, sourceDrafts, known, discoveredIndex, visited, discovered);
        }

        if (discovered.Count == 0)
            return output.ToArray();

        var drafts = discovered
            .Select(item =>
            {
                var preferSourceMetadata = ShouldPreferTransitiveDependencySourceMetadata(
                    item.VersionSource,
                    publishVersionSource) &&
                    !HasExplicitDependencyConstraint(item.Reference);
                return new RequiredModuleDraft(
                    item.Reference.ModuleName,
                    preferSourceMetadata ? "Latest" : item.Reference.ModuleVersion,
                    preferSourceMetadata ? "Latest" : item.Reference.ModuleVersion,
                    preferSourceMetadata ? null : item.Reference.RequiredVersion,
                    preferSourceMetadata ? "Auto" : item.Reference.Guid,
                    item.VersionSource);
            })
            .ToArray();

        var resolved = ResolveRequiredModules(
            drafts,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            prerelease,
            repository,
            credential,
            publishVersionSource);

        var discoveredByName = discovered
            .GroupBy(static item => item.Reference.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Reference, StringComparer.OrdinalIgnoreCase);

        foreach (var module in resolved)
        {
            if (module is null || string.IsNullOrWhiteSpace(module.ModuleName))
                continue;
            if (!known.Add(module.ModuleName))
                continue;

            if (discoveredByName.TryGetValue(module.ModuleName, out var original) &&
                !string.IsNullOrWhiteSpace(original.MaximumVersion))
            {
                output.Add(new RequiredModuleReference(
                    module.ModuleName,
                    module.ModuleVersion,
                    module.RequiredVersion,
                    original.MaximumVersion,
                    module.Guid));
                continue;
            }

            output.Add(module);
        }

        return output.ToArray();
    }

    private void CollectTransitiveRequiredModules(
        RequiredModuleReference module,
        ModuleDependencyVersionSource inheritedVersionSource,
        IReadOnlyDictionary<string, RequiredModuleDraft> sourceDrafts,
        HashSet<string> known,
        HashSet<string> discoveredIndex,
        HashSet<string> visited,
        List<(RequiredModuleReference Reference, ModuleDependencyVersionSource VersionSource)> discovered)
    {
        if (module is null || string.IsNullOrWhiteSpace(module.ModuleName))
            return;
        var normalizedModuleName = module.ModuleName.Trim();
        if (!visited.Add(normalizedModuleName))
            return;

        var required = GetRequiredModulesForInstalledModule(module);
        foreach (var dep in required ?? Array.Empty<RequiredModuleReference>())
        {
            if (dep is null || string.IsNullOrWhiteSpace(dep.ModuleName))
                continue;

            var depName = dep.ModuleName.Trim();
            if (ModulePipelinePlanningHelpers.ShouldSkipTransitiveRequiredDependencyModule(depName))
                continue;

            var childVersionSource = ResolveInheritedDependencyVersionSource(depName, sourceDrafts, inheritedVersionSource);
            if (!known.Contains(depName) && discoveredIndex.Add(depName))
            {
                discovered.Add((
                    new RequiredModuleReference(
                        depName,
                        dep.ModuleVersion,
                        dep.RequiredVersion,
                        dep.MaximumVersion,
                        dep.Guid),
                    childVersionSource));
            }

            CollectTransitiveRequiredModules(dep, childVersionSource, sourceDrafts, known, discoveredIndex, visited, discovered);
        }
    }

    private RequiredModuleReference[] OrderRequiredModulesByDependenciesFirst(IEnumerable<RequiredModuleReference> modules)
    {
        var byName = (modules ?? Array.Empty<RequiredModuleReference>())
            .Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.ModuleName))
            .GroupBy(static module => module.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);
        if (byName.Count < 2)
            return byName.Values.ToArray();

        var ordered = new List<RequiredModuleReference>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules ?? Array.Empty<RequiredModuleReference>())
        {
            if (module is null || string.IsNullOrWhiteSpace(module.ModuleName))
                continue;

            VisitRequiredModuleDependencyFirst(module.ModuleName.Trim(), byName, visiting, visited, ordered);
        }

        return ordered.ToArray();
    }

    private void VisitRequiredModuleDependencyFirst(
        string moduleName,
        IReadOnlyDictionary<string, RequiredModuleReference> modulesByName,
        HashSet<string> visiting,
        HashSet<string> visited,
        List<RequiredModuleReference> ordered)
    {
        if (string.IsNullOrWhiteSpace(moduleName) || visited.Contains(moduleName))
            return;
        if (!visiting.Add(moduleName))
            return;

        var current = modulesByName.TryGetValue(moduleName, out var reference)
            ? reference
            : new RequiredModuleReference(moduleName);
        var required = GetRequiredModulesForInstalledModule(current);
        foreach (var dep in required ?? Array.Empty<RequiredModuleReference>())
        {
            if (dep is null || string.IsNullOrWhiteSpace(dep.ModuleName))
                continue;

            var depName = dep.ModuleName.Trim();
            if (modulesByName.ContainsKey(depName))
                VisitRequiredModuleDependencyFirst(depName, modulesByName, visiting, visited, ordered);
        }

        visiting.Remove(moduleName);
        if (visited.Add(moduleName) && modulesByName.TryGetValue(moduleName, out var module))
            ordered.Add(module);
    }

    private IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(RequiredModuleReference reference)
    {
        if (reference is null || string.IsNullOrWhiteSpace(reference.ModuleName))
            return Array.Empty<RequiredModuleReference>();

        return _moduleDependencyMetadataProvider is IModuleDependencyReferenceMetadataProvider referenceProvider
            ? referenceProvider.GetRequiredModulesForInstalledModule(reference)
            : _moduleDependencyMetadataProvider.GetRequiredModulesForInstalledModule(reference.ModuleName.Trim());
    }

    private static ModuleDependencyVersionSource ResolveInheritedDependencyVersionSource(
        string moduleName,
        IReadOnlyDictionary<string, RequiredModuleDraft> sourceDrafts,
        ModuleDependencyVersionSource inheritedVersionSource)
    {
        if (!string.IsNullOrWhiteSpace(moduleName) &&
            sourceDrafts is not null &&
            sourceDrafts.TryGetValue(moduleName.Trim(), out var draft))
        {
            return draft.VersionSource;
        }

        return inheritedVersionSource;
    }

    private static bool ShouldPreferTransitiveDependencySourceMetadata(
        ModuleDependencyVersionSource versionSource,
        DependencyVersionSourceRepository? publishVersionSource)
        => versionSource == ModuleDependencyVersionSource.PSGallery ||
           versionSource == ModuleDependencyVersionSource.PublishRepository ||
           (versionSource == ModuleDependencyVersionSource.Auto && publishVersionSource is not null);

    private static bool HasExplicitDependencyConstraint(RequiredModuleReference reference)
        => HasExplicitVersionValue(reference.ModuleVersion) ||
           HasExplicitVersionValue(reference.RequiredVersion) ||
           HasExplicitVersionValue(reference.MaximumVersion) ||
           HasExplicitGuidValue(reference.Guid);

    private static bool HasExplicitGuidValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = (value ?? string.Empty).Trim();
        return !trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExplicitVersionValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = (value ?? string.Empty).Trim();
        return !trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase) &&
               !trimmed.Equals("Latest", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RequiredModuleSetResolution
    {
        public RequiredModuleReference[] RequiredModules { get; }
        public RequiredModuleReference[] RequiredModulesForPackaging { get; }

        public RequiredModuleSetResolution(
            RequiredModuleReference[] requiredModules,
            RequiredModuleReference[] requiredModulesForPackaging)
        {
            RequiredModules = requiredModules ?? Array.Empty<RequiredModuleReference>();
            RequiredModulesForPackaging = requiredModulesForPackaging ?? Array.Empty<RequiredModuleReference>();
        }
    }

    private RequiredModuleReference[] ResolveRequiredModulesFromSingleSource(
        IReadOnlyList<RequiredModuleDraft> list,
        IReadOnlyDictionary<string, (string? Version, string? Guid)> installedMetadata,
        bool resolveMissingModulesOnline,
        bool warnIfRequiredModulesOutdated,
        bool prerelease,
        string? repository,
        RepositoryCredential? credential,
        bool preferOnlineMetadata)
    {
        Func<IReadOnlyCollection<string>, IReadOnlyDictionary<string, (string? Version, string? Guid)>>? onlineLookup = null;
        if (resolveMissingModulesOnline || warnIfRequiredModulesOutdated || preferOnlineMetadata)
        {
            onlineLookup = candidates => _moduleDependencyMetadataProvider.ResolveLatestOnlineVersions(candidates, repository, credential, prerelease);
        }

        var resolver = new RequiredModuleResolutionEngine(_logger);
        return resolver.ResolveRequiredModules(
            ToRequiredModuleDraftDescriptors(list),
            installedMetadata,
            onlineLookup,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            preferOnlineMetadata);
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
                draft.Guid,
                draft.VersionSource))
            .ToArray();
    }

    private static DependencyVersionSourceRepository ResolveDependencyVersionSource(
        ModuleDependencyVersionSource source,
        DependencyVersionSourceRepository? publishVersionSource)
    {
        return source switch
        {
            ModuleDependencyVersionSource.Auto => publishVersionSource
                ?? new DependencyVersionSourceRepository(null, null, preferOnlineMetadata: false, allowOnlineLookup: true),
            ModuleDependencyVersionSource.Installed => new DependencyVersionSourceRepository(null, null, preferOnlineMetadata: false, allowOnlineLookup: false),
            ModuleDependencyVersionSource.PSGallery => new DependencyVersionSourceRepository("PSGallery", null, preferOnlineMetadata: true, allowOnlineLookup: true),
            ModuleDependencyVersionSource.PublishRepository => publishVersionSource
                ?? throw new InvalidOperationException("Module dependency VersionSource 'PublishRepository' requires one enabled New-ConfigurationPublish segment with -UseAsDependencyVersionSource."),
            _ => new DependencyVersionSourceRepository(null, null, preferOnlineMetadata: false, allowOnlineLookup: true)
        };
    }

    private readonly struct DependencyVersionSourceRepository : IEquatable<DependencyVersionSourceRepository>
    {
        public string? Repository { get; }
        public RepositoryCredential? Credential { get; }
        public bool PreferOnlineMetadata { get; }
        public bool AllowOnlineLookup { get; }

        public DependencyVersionSourceRepository(
            string? repository,
            RepositoryCredential? credential,
            bool preferOnlineMetadata,
            bool allowOnlineLookup)
        {
            Repository = string.IsNullOrWhiteSpace(repository) ? null : repository!.Trim();
            Credential = credential;
            PreferOnlineMetadata = preferOnlineMetadata;
            AllowOnlineLookup = allowOnlineLookup;
        }

        public bool Equals(DependencyVersionSourceRepository other)
            => string.Equals(Repository, other.Repository, StringComparison.OrdinalIgnoreCase) &&
               ReferenceEquals(Credential, other.Credential) &&
               PreferOnlineMetadata == other.PreferOnlineMetadata &&
               AllowOnlineLookup == other.AllowOnlineLookup;

        public override bool Equals(object? obj)
            => obj is DependencyVersionSourceRepository other && Equals(other);

        public override int GetHashCode()
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(Repository ?? string.Empty);
            hash = (hash * 397) ^ (Credential?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ PreferOnlineMetadata.GetHashCode();
            hash = (hash * 397) ^ AllowOnlineLookup.GetHashCode();
            return hash;
        }
    }

}
