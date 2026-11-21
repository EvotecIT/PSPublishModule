namespace PowerForge;

internal sealed partial class ManagedModuleRestoreService
{
    private static async Task<Dictionary<string, PreparedPackage>> ResolveAsync(
        ManagedModuleRestoreRequirement[] requirements,
        ManagedModuleRepository repository,
        ManagedModuleRepositoryClient client,
        string cache,
        CancellationToken cancellationToken)
    {
        var selected = new Dictionary<string, PreparedPackage>(StringComparer.OrdinalIgnoreCase);
        var downloads = new Dictionary<string, PreparedPackage>(StringComparer.OrdinalIgnoreCase);
        var selections = new Dictionary<string, PackageCandidate>(StringComparer.OrdinalIgnoreCase);
        var listings = new Dictionary<string, IReadOnlyList<ManagedModuleVersionInfo>>(StringComparer.OrdinalIgnoreCase);
        string? conflict = null;

        foreach (var requirement in requirements)
            selected.Add(requirement.Name, await DownloadAsync(requirement.Name, requirement.Version).ConfigureAwait(false));

        if (!await SelectDependenciesAsync().ConfigureAwait(false))
            throw new InvalidOperationException(conflict ?? "Unable to resolve the module dependency set.");
        return selected;

        async Task<PreparedPackage> DownloadAsync(string name, string version)
        {
            var key = name + "/" + version;
            if (downloads.TryGetValue(key, out var package))
                return package;

            package = PreparePackage(await client.DownloadPackageAsync(repository, name, version, cache,
                cancellationToken: cancellationToken).ConfigureAwait(false));
            downloads.Add(key, package);
            return package;
        }

        async Task<bool> SelectDependenciesAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var constraints = selected.Values.SelectMany(package => package.Dependencies)
                .GroupBy(static dependency => dependency.Name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(static group => group.Any(constraint => constraint.Range.ExactVersion is not null))
                .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase);
            string? pendingName = null;
            var unresolved = new List<IGrouping<string, DependencyConstraint>>();
            foreach (var group in constraints)
            {
                if (selected.TryGetValue(group.Key, out var current))
                {
                    if (group.All(constraint => constraint.Matches(current)))
                        continue;

                    conflict = DescribeConflict(group.Key, group);
                    return false;
                }

                unresolved.Add(group);
            }

            if (unresolved.Count == 0)
            {
                foreach (var group in constraints)
                {
                    if (selections.TryGetValue(group.Key, out var candidate) && !candidate.IsEligible(group))
                    {
                        conflict = DescribeConflict(group.Key, group);
                        return false;
                    }
                }

                if (TryOrderPackages(requirements, selected, out _, out var cycle))
                    return true;
                conflict = $"Restore contains a dependency cycle involving '{cycle}'.";
                return false;
            }

            // A sibling can introduce a pin for a missing, prerelease or unlisted version.
            PackageCandidate[] candidates = Array.Empty<PackageCandidate>();
            foreach (var group in unresolved)
            {
                candidates = await GetCandidateVersionsAsync(group.Key, group.ToArray()).ConfigureAwait(false);
                if (candidates.Length == 0)
                    continue;
                pendingName = group.Key;
                break;
            }

            if (pendingName is null)
            {
                conflict = DescribeConflict(unresolved[0].Key, unresolved[0]);
                return false;
            }

            foreach (var candidate in candidates)
            {
                PreparedPackage package;
                try
                {
                    package = await DownloadAsync(pendingName, candidate.Version).ConfigureAwait(false);
                }
                catch (ManagedModuleRepositoryException ex) when (ex.Operation == "Download" && ex.StatusCode == 404)
                {
                    conflict = $"'{pendingName}' version '{candidate.Version}' is missing from the restore source.";
                    continue;
                }

                selected.Add(pendingName, package);
                selections.Add(pendingName, candidate);
                if (await SelectDependenciesAsync().ConfigureAwait(false))
                    return true;
                selections.Remove(pendingName);
                selected.Remove(pendingName);
            }

            return false;
        }

        async Task<PackageCandidate[]> GetCandidateVersionsAsync(string name, DependencyConstraint[] constraints)
        {
            var exact = constraints.Select(static constraint => constraint.Range.ExactVersion)
                .FirstOrDefault(static version => version is not null);
            if (exact is not null && repository.Kind != ManagedModuleRepositoryKind.LocalFolder)
                return constraints.All(constraint => constraint.Range.IsSatisfiedBy(exact))
                    ? new[] { new PackageCandidate(ValidateVersion(name, exact), true, ManagedModuleVersionComparer.IsPrerelease(exact)) }
                    : Array.Empty<PackageCandidate>();

            if (!listings.TryGetValue(name, out var versions))
            {
                versions = await client.GetVersionsAsync(repository, name, includePrerelease: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                listings.Add(name, versions);
            }

            return versions.Where(version => constraints.All(constraint => constraint.Range.IsSatisfiedBy(version.Version)))
                .Select(static version => new PackageCandidate(version.Version, version.Listed, version.IsPrerelease))
                .OrderByDescending(candidate => candidate.IsEligible(constraints))
                .ThenByDescending(static candidate => candidate.Version, ManagedModuleVersionComparer.Instance)
                .ToArray();
        }
    }

    private static string DescribeConflict(string name, IEnumerable<DependencyConstraint> constraints)
        => $"No compatible version of '{name}' satisfies the requirements: " +
           string.Join(", ", constraints.Select(static constraint => $"{constraint.Parent} requires {constraint.Description}")) + ".";

    private readonly struct PackageCandidate(string version, bool listed, bool isPrerelease)
    {
        public string Version { get; } = version;

        public bool IsEligible(IEnumerable<DependencyConstraint> constraints)
            => constraints.Any(static constraint => constraint.Range.ExactVersion is not null) ||
               (listed && (!isPrerelease || constraints.Any(static constraint => constraint.Range.AllowsPrerelease)));
    }

    private sealed class DependencyConstraint(string parent, string name, ManagedModuleVersionRange range, string? manifestGuid)
    {
        private readonly Guid? _guid = string.IsNullOrWhiteSpace(manifestGuid) ? null :
            Guid.TryParse(manifestGuid, out var parsed) ? parsed :
            throw new InvalidOperationException($"'{parent}' requires '{name}' with an invalid module GUID '{manifestGuid}'.");

        public string Parent { get; } = parent;
        public string Name { get; } = name;
        public ManagedModuleVersionRange Range { get; } = range;
        public string Description => _guid is { } requiredGuid ? $"{Range} with GUID {requiredGuid}" : Range.ToString();

        public bool Matches(PreparedPackage package)
            => Range.IsSatisfiedBy(package.Pin.Version) &&
               (_guid is null || _guid == package.Metadata.ModuleManifestGuid);
    }
}
