using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private ApprovedModuleResolution[] ResolveApprovedModuleResolutions(
        IReadOnlyList<RequiredModuleDraft> approvedDrafts,
        IReadOnlyList<RequiredModuleDraft> requiredDrafts,
        IReadOnlyList<RequiredModuleReference> resolvedRequiredModules,
        bool resolveMissingModulesOnline,
        bool warnIfRequiredModulesOutdated,
        bool prerelease,
        string? repository,
        RepositoryCredential? credential,
        DependencyVersionSourceRepository? publishVersionSource)
    {
        var requiredByName = BuildRequiredModuleDraftMap(requiredDrafts);
        var resolvedRequiredByName = (resolvedRequiredModules ?? Array.Empty<RequiredModuleReference>())
            .Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.ModuleName))
            .GroupBy(static module => module.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var output = new List<ApprovedModuleResolution>();
        foreach (var approved in approvedDrafts ?? Array.Empty<RequiredModuleDraft>())
        {
            if (approved is null || string.IsNullOrWhiteSpace(approved.ModuleName))
                continue;

            var effective = approved;
            RequiredModuleDraft? requiredDraft = null;
            var inheritsRequiredDeclaration = !HasApprovedModuleConstraint(approved) &&
                                             requiredByName.TryGetValue(approved.ModuleName, out requiredDraft);
            if (inheritsRequiredDeclaration)
            {
                effective = new RequiredModuleDraft(
                    approved.ModuleName,
                    requiredDraft!.ModuleVersion,
                    requiredDraft.MinimumVersion,
                    requiredDraft.RequiredVersion,
                    requiredDraft.Guid,
                    approved.VersionSource);
            }

            RequiredModuleReference constraint;
            if (inheritsRequiredDeclaration &&
                requiredDraft!.VersionSource == approved.VersionSource &&
                resolvedRequiredByName.TryGetValue(approved.ModuleName, out var resolvedRequired))
            {
                constraint = resolvedRequired;
            }
            else
            {
                constraint = ResolveRequiredModules(
                        new[] { effective },
                        resolveMissingModulesOnline,
                        warnIfRequiredModulesOutdated,
                        prerelease,
                        repository,
                        credential,
                        publishVersionSource)
                    .FirstOrDefault() ?? new RequiredModuleReference(effective.ModuleName);
            }

            var source = ResolveDependencyVersionSource(effective.VersionSource, publishVersionSource);
            var autoUsesResolvedSource = effective.VersionSource == ModuleDependencyVersionSource.Auto &&
                                         (!string.IsNullOrWhiteSpace(source.Repository) || source.Credential is not null);
            var effectiveRepository = effective.VersionSource == ModuleDependencyVersionSource.Auto && !autoUsesResolvedSource
                ? repository
                : source.Repository;
            var effectiveCredential = effective.VersionSource == ModuleDependencyVersionSource.Auto && !autoUsesResolvedSource
                ? credential
                : source.Credential;
            output.Add(new ApprovedModuleResolution(
                approved.ModuleName.Trim(),
                constraint,
                effective.VersionSource,
                effectiveRepository,
                effectiveCredential,
                prerelease,
                HasAutoOrLatestConstraint(effective)));
        }

        return output
            .GroupBy(static resolution => resolution.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .ToArray();
    }

    private static bool HasApprovedModuleConstraint(RequiredModuleDraft draft)
        => !string.IsNullOrWhiteSpace(draft.ModuleVersion) ||
           !string.IsNullOrWhiteSpace(draft.MinimumVersion) ||
           !string.IsNullOrWhiteSpace(draft.RequiredVersion) ||
           !string.IsNullOrWhiteSpace(draft.Guid);

    private static bool HasAutoOrLatestConstraint(RequiredModuleDraft draft)
        => IsAutoOrLatestConstraintValue(draft.ModuleVersion) ||
           IsAutoOrLatestConstraintValue(draft.MinimumVersion) ||
           IsAutoOrLatestConstraintValue(draft.RequiredVersion);

    private static bool IsAutoOrLatestConstraintValue(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Latest", StringComparison.OrdinalIgnoreCase));

    private static ModuleDependencySourceResolution[] ResolveDependencySourceResolutions(
        IEnumerable<RequiredModuleDraft> drafts,
        DependencyVersionSourceRepository? publishVersionSource)
    {
        return (drafts ?? Array.Empty<RequiredModuleDraft>())
            .Where(static draft => draft is not null && !string.IsNullOrWhiteSpace(draft.ModuleName))
            .Select(draft =>
            {
                var source = ResolveDependencyVersionSource(draft.VersionSource, publishVersionSource);
                return new ModuleDependencySourceResolution(
                    draft.ModuleName.Trim(),
                    draft.VersionSource,
                    source.Repository,
                    source.Credential,
                    draft.RequiredVersion,
                    string.IsNullOrWhiteSpace(draft.MinimumVersion) ? draft.ModuleVersion : draft.MinimumVersion,
                    draft.Guid);
            })
            .GroupBy(
                static resolution => $"{resolution.Name}|{resolution.RequiredVersion}|{resolution.MinimumVersion}|{resolution.Guid}|{resolution.VersionSource}|{resolution.Repository}",
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .ToArray();
    }

    private static RequiredModuleDraft[] CreateResolvedDependencyDrafts(
        IEnumerable<RequiredModuleReference> modules,
        IReadOnlyDictionary<string, ModuleDependencyVersionSource> versionSources)
    {
        return (modules ?? Array.Empty<RequiredModuleReference>())
            .Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.ModuleName))
            .Select(module => new RequiredModuleDraft(
                moduleName: module.ModuleName,
                moduleVersion: module.ModuleVersion,
                minimumVersion: module.ModuleVersion,
                requiredVersion: module.RequiredVersion,
                guid: module.Guid,
                versionSource: versionSources is not null && versionSources.TryGetValue(module.ModuleName, out var source)
                    ? source
                    : ModuleDependencyVersionSource.Installed))
            .ToArray();
    }

    private ApprovedModuleSourceLease ResolveApprovedModuleSources(ModulePipelinePlan plan)
    {
        var resolutions = plan.ApprovedModuleResolutions ?? Array.Empty<ApprovedModuleResolution>();
        if (resolutions.Length == 0)
            return new ApprovedModuleSourceLease(_logger, Array.Empty<ApprovedModuleSource>(), Array.Empty<string>());

        var sources = new List<ApprovedModuleSource>();
        var temporaryRoots = new List<string>();
        var installedCandidates = resolutions
            .Where(static resolution => resolution.VersionSource is ModuleDependencyVersionSource.Installed or ModuleDependencyVersionSource.Auto)
            .ToArray();

        var installed = _moduleDependencyMetadataProvider is IModuleDependencyVersionedMetadataProvider versionedProvider
            ? versionedProvider.GetInstalledModules(installedCandidates
                .Select(static resolution => (RequiredModuleReference)new ApprovedModuleInstalledReference(
                    resolution.Constraint,
                    resolution.MatchPrereleaseByBaseVersion))
                .ToArray())
            : new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var resolution in resolutions)
            {
                if (resolution.VersionSource is ModuleDependencyVersionSource.Installed or ModuleDependencyVersionSource.Auto &&
                    installed.TryGetValue(resolution.Name, out var installedModule) &&
                    !string.IsNullOrWhiteSpace(installedModule.ModuleBasePath))
                {
                    sources.Add(new ApprovedModuleSource(
                        resolution.Name,
                        installedModule.Version,
                        Path.GetFullPath(installedModule.ModuleBasePath!),
                        installedModule.Guid));
                    continue;
                }

                if (resolution.VersionSource == ModuleDependencyVersionSource.Installed)
                {
                    _logger.Warn($"Approved module '{resolution.Name}' could not be bound to an installed path matching {ModulePublisher.FormatRequiredModuleConstraint(resolution.Constraint)}.");
                    continue;
                }

                var repository = string.IsNullOrWhiteSpace(resolution.Repository) ? "PSGallery" : resolution.Repository!;
                var client = new PSResourceGetClient(_powerShellRunner, _logger);
                var candidates = client.Find(
                    new PSResourceFindOptions(
                        names: new[] { resolution.Name },
                        version: BuildApprovedModuleRepositoryQueryVersion(
                            resolution.Constraint,
                            resolution.MatchPrereleaseByBaseVersion),
                        prerelease: resolution.Prerelease || RequiredModuleRepositoryPublisher.AllowsPrerelease(resolution.Constraint),
                        repositories: new[] { repository },
                        credential: resolution.Credential),
                    timeout: TimeSpan.FromMinutes(2));
                var selected = SelectApprovedModuleRepositoryCandidate(
                    resolution.Constraint,
                    candidates,
                    resolution.Prerelease,
                    resolution.MatchPrereleaseByBaseVersion);
                if (selected is null)
                {
                    throw new InvalidOperationException(
                        $"Approved module '{resolution.Name}' has no version in repository '{repository}' matching {ModulePublisher.FormatRequiredModuleConstraint(resolution.Constraint)}.");
                }

                var selectedVersion = ModulePublisher.GetRepositoryVersionText(selected);
                var temporaryRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "approved-modules", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryRoot);
                temporaryRoots.Add(temporaryRoot);
                client.Save(
                    new PSResourceSaveOptions(
                        name: resolution.Name,
                        destinationPath: temporaryRoot,
                        version: selectedVersion,
                        repository: repository,
                        prerelease: resolution.Prerelease || RequiredModuleRepositoryPublisher.AllowsPrerelease(resolution.Constraint),
                        trustRepository: true,
                        skipDependencyCheck: false,
                        acceptLicense: true,
                        quiet: true,
                        credential: resolution.Credential),
                    timeout: TimeSpan.FromMinutes(10));

                var modulePath = RequiredModuleRepositoryPublisher.FindSavedModulePath(
                    temporaryRoot,
                    resolution.Name,
                    selectedVersion);
                if (modulePath is null)
                {
                    throw new InvalidOperationException(
                        $"Approved module '{resolution.Name}' {selectedVersion} was downloaded from '{repository}', but its manifest was not found in the temporary source.");
                }

                sources.Add(new ApprovedModuleSource(
                    resolution.Name,
                    selectedVersion,
                    modulePath,
                    selected.Guid,
                    moduleSearchRoot: temporaryRoot));
            }
        }
        catch
        {
            CleanupTemporaryApprovedModuleSources(_logger, temporaryRoots);
            throw;
        }

        return new ApprovedModuleSourceLease(_logger, sources.ToArray(), temporaryRoots.ToArray());
    }

    internal static string? BuildApprovedModuleRepositoryQueryVersion(
        RequiredModuleReference constraint,
        bool matchPrereleaseByBaseVersion)
        => matchPrereleaseByBaseVersion
            ? null
            : RequiredModuleRepositoryPublisher.BuildPSResourceGetVersionRange(constraint);

    internal static PSResourceInfo? SelectApprovedModuleRepositoryCandidate(
        RequiredModuleReference constraint,
        IEnumerable<PSResourceInfo> candidates,
        bool allowPrerelease = false,
        bool matchPrereleaseByBaseVersion = false)
    {
        var matchingCandidates = (candidates ?? Array.Empty<PSResourceInfo>())
            .Where(candidate => candidate is not null && ModuleGuidMatches(candidate.Guid, constraint.Guid))
            .ToArray();
        return RequiredModuleRepositoryPublisher.SelectRequiredModuleVersionForPublish(
            constraint,
            matchingCandidates,
            allowPrerelease,
            matchPrereleaseByBaseVersion);
    }

    private static bool ModuleGuidMatches(string? candidateGuid, string? requiredGuid)
    {
        if (string.IsNullOrWhiteSpace(requiredGuid) ||
            requiredGuid!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return System.Guid.TryParse(candidateGuid, out var candidate) &&
               System.Guid.TryParse(requiredGuid, out var required) &&
               candidate == required;
    }

    private sealed class ApprovedModuleSourceLease : IDisposable
    {
        private readonly string[] _temporaryRoots;
        private readonly ILogger _leaseLogger;

        internal ApprovedModuleSource[] Sources { get; }

        internal ApprovedModuleSourceLease(ILogger logger, ApprovedModuleSource[] sources, string[] temporaryRoots)
        {
            _leaseLogger = logger;
            Sources = sources ?? Array.Empty<ApprovedModuleSource>();
            _temporaryRoots = temporaryRoots ?? Array.Empty<string>();
        }

        public void Dispose()
            => CleanupTemporaryApprovedModuleSources(_leaseLogger, _temporaryRoots);
    }

    private static void CleanupTemporaryApprovedModuleSources(ILogger logger, IEnumerable<string> roots)
    {
        foreach (var root in roots ?? Array.Empty<string>())
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (Exception ex)
            {
                logger.Warn($"Failed to remove temporary approved-module source '{root}'. {ex.Message}");
            }
        }
    }
}
