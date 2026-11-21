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
            DependencyConstraint[] pendingConstraints = Array.Empty<DependencyConstraint>();
            foreach (var group in constraints)
            {
                if (selected.TryGetValue(group.Key, out var current))
                {
                    if (group.All(constraint => constraint.Range.IsSatisfiedBy(current.Pin.Version)))
                        continue;

                    conflict = DescribeConflict(group.Key, group);
                    return false;
                }

                if (pendingName is null)
                {
                    pendingName = group.Key;
                    pendingConstraints = group.ToArray();
                }
            }

            if (pendingName is null)
            {
                if (TryOrderPackages(requirements, selected, out _, out var cycle))
                    return true;
                conflict = $"Restore contains a dependency cycle involving '{cycle}'.";
                return false;
            }

            var candidates = (await GetCandidateVersionsAsync(pendingName, pendingConstraints).ConfigureAwait(false)).ToArray();
            if (candidates.Length == 0)
            {
                conflict = DescribeConflict(pendingName, pendingConstraints);
                return false;
            }

            foreach (var version in candidates)
            {
                PreparedPackage package;
                try
                {
                    package = await DownloadAsync(pendingName, version).ConfigureAwait(false);
                }
                catch (ManagedModuleRepositoryException ex) when (ex.Operation == "Download" && ex.StatusCode == 404)
                {
                    conflict = $"'{pendingName}' version '{version}' is missing from the restore source.";
                    continue;
                }

                selected.Add(pendingName, package);
                if (await SelectDependenciesAsync().ConfigureAwait(false))
                    return true;
                selected.Remove(pendingName);
            }

            return false;
        }

        async Task<IEnumerable<string>> GetCandidateVersionsAsync(string name, DependencyConstraint[] constraints)
        {
            var exact = constraints.Select(static constraint => constraint.Range.ExactVersion)
                .FirstOrDefault(static version => version is not null);
            if (exact is not null && repository.Kind != ManagedModuleRepositoryKind.LocalFolder)
                return constraints.All(constraint => constraint.Range.IsSatisfiedBy(exact))
                    ? new[] { ValidateVersion(name, exact) }
                    : Array.Empty<string>();

            if (!listings.TryGetValue(name, out var versions))
            {
                versions = await client.GetVersionsAsync(repository, name, includePrerelease: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                listings.Add(name, versions);
            }

            var allowPrerelease = constraints.Any(static constraint => constraint.Range.AllowsPrerelease);
            return versions.Where(version => exact is not null || (version.Listed && (allowPrerelease || !version.IsPrerelease)))
                .Select(static version => version.Version)
                .Where(version => constraints.All(constraint => constraint.Range.IsSatisfiedBy(version)))
                .OrderByDescending(static version => version, ManagedModuleVersionComparer.Instance);
        }
    }

    private static string DescribeConflict(string name, IEnumerable<DependencyConstraint> constraints)
        => $"No compatible version of '{name}' satisfies the requirements: " +
           string.Join(", ", constraints.Select(static constraint => $"{constraint.Parent} requires {constraint.Range}")) + ".";

    private sealed class DependencyConstraint(string parent, string name, ManagedModuleVersionRange range)
    {
        public string Parent { get; } = parent;
        public string Name { get; } = name;
        public ManagedModuleVersionRange Range { get; } = range;
    }
}
