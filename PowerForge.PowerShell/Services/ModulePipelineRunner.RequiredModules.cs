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
