using System.Net.Http;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    private static IReadOnlyList<ManagedModuleCatalogStore> CreateCatalogStores(
        ManagedModuleRepositoryClientOptions options,
        HttpClient httpClient)
    {
        if (options.DisableManagedModuleCatalog)
            return Array.Empty<ManagedModuleCatalogStore>();

        var paths = new[]
            {
                string.IsNullOrWhiteSpace(options.ManagedModuleCatalogPath)
                    ? ManagedModuleCatalogStore.GetDefaultPath(machine: false)
                    : options.ManagedModuleCatalogPath!,
                string.IsNullOrWhiteSpace(options.MachineManagedModuleCatalogPath)
                    ? ManagedModuleCatalogStore.GetDefaultPath(machine: true)
                    : options.MachineManagedModuleCatalogPath!
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => System.IO.Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new ManagedModuleCatalogStore(path, httpClient))
            .ToArray();

        return paths;
    }

    private async Task<IReadOnlyList<ManagedModuleVersionInfo>> GetVersionsWithCatalogAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<IReadOnlyList<ManagedModuleVersionInfo>>> liveQuery)
    {
        var catalogs = FindCatalogs(repository);
        if (catalogs.Count == 0)
            return await liveQuery(cancellationToken).ConfigureAwait(false);

        var readThroughCatalogs = catalogs
            .Where(static catalog => catalog.Catalog.Mode == ManagedModuleCatalogCacheMode.ReadThrough)
            .ToArray();
        if (readThroughCatalogs.Length > 0)
        {
            var liveVersions = await liveQuery(cancellationToken).ConfigureAwait(false);
            await WarmCatalogsAsync(readThroughCatalogs, packageId, credential, cancellationToken).ConfigureAwait(false);
            return liveVersions;
        }

        var cachedVersions = GetFirstCatalogVersions(repository, catalogs, packageId, includePrerelease);
        if (catalogs.Any(static catalog => catalog.Catalog.Mode == ManagedModuleCatalogCacheMode.Offline))
            return cachedVersions;
        if (catalogs.Any(static catalog => catalog.Catalog.Mode == ManagedModuleCatalogCacheMode.PreferCache) && cachedVersions.Count > 0)
            return cachedVersions;

        try
        {
            var liveVersions = await liveQuery(cancellationToken).ConfigureAwait(false);
            return liveVersions.Count > 0 ? liveVersions : cachedVersions;
        }
        catch (Exception ex) when (CanFallbackToCatalog(ex))
        {
            if (cachedVersions.Count > 0)
            {
                _logger.Verbose($"Managed module catalog served cached versions for '{packageId}' after live metadata failed: {ex.Message}");
                return cachedVersions;
            }

            throw;
        }
    }

    private async Task<ManagedModuleVersionInfo?> GetLatestVersionWithCatalogAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<ManagedModuleVersionInfo?>> liveQuery)
    {
        var catalogs = FindCatalogs(repository);
        if (catalogs.Count == 0)
            return await liveQuery(cancellationToken).ConfigureAwait(false);

        var readThroughCatalogs = catalogs
            .Where(static catalog => catalog.Catalog.Mode == ManagedModuleCatalogCacheMode.ReadThrough)
            .ToArray();
        if (readThroughCatalogs.Length > 0)
        {
            var liveVersion = await liveQuery(cancellationToken).ConfigureAwait(false);
            if (liveVersion is not null)
                await WarmCatalogsAsync(readThroughCatalogs, packageId, credential, cancellationToken).ConfigureAwait(false);
            return liveVersion;
        }

        var cachedVersion = GetFirstLatestCatalogVersion(repository, catalogs, packageId, includePrerelease);
        if (catalogs.Any(static catalog => catalog.Catalog.Mode == ManagedModuleCatalogCacheMode.Offline))
            return cachedVersion;
        if (catalogs.Any(static catalog => catalog.Catalog.Mode == ManagedModuleCatalogCacheMode.PreferCache) && cachedVersion is not null)
            return cachedVersion;

        try
        {
            var liveVersion = await liveQuery(cancellationToken).ConfigureAwait(false);
            return liveVersion ?? cachedVersion;
        }
        catch (Exception ex) when (CanFallbackToCatalog(ex))
        {
            if (cachedVersion is not null)
            {
                _logger.Verbose($"Managed module catalog served cached latest version for '{packageId}' after live metadata failed: {ex.Message}");
                return cachedVersion;
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<ManagedModuleVersionInfo>> SearchPackagesWithCatalogAsync(
        ManagedModuleRepository repository,
        string query,
        bool includePrerelease,
        RepositoryCredential? credential,
        int take,
        int skip,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<IReadOnlyList<ManagedModuleVersionInfo>>> liveQuery)
    {
        var catalogs = FindCatalogs(repository);
        if (catalogs.Count == 0)
            return await liveQuery(cancellationToken).ConfigureAwait(false);

        var readThroughCatalogs = catalogs
            .Where(static catalog => catalog.Catalog.Mode == ManagedModuleCatalogCacheMode.ReadThrough)
            .ToArray();
        if (readThroughCatalogs.Length > 0)
        {
            var liveMatches = await liveQuery(cancellationToken).ConfigureAwait(false);
            await WarmCatalogsAsync(readThroughCatalogs, liveMatches.Select(static match => match.Name), credential, cancellationToken).ConfigureAwait(false);
            return liveMatches;
        }

        var cachedMatches = SearchCatalogVersions(repository, catalogs, query, includePrerelease, take, skip);
        if (catalogs.Any(static catalog => catalog.Catalog.Mode == ManagedModuleCatalogCacheMode.Offline))
            return cachedMatches;
        if (catalogs.Any(static catalog => catalog.Catalog.Mode == ManagedModuleCatalogCacheMode.PreferCache) && cachedMatches.Count > 0)
            return cachedMatches;

        try
        {
            var liveMatches = await liveQuery(cancellationToken).ConfigureAwait(false);
            return liveMatches.Count > 0 ? liveMatches : cachedMatches;
        }
        catch (Exception ex) when (CanFallbackToCatalog(ex))
        {
            if (cachedMatches.Count > 0)
            {
                _logger.Verbose($"Managed module catalog served cached search results for '{query}' after live metadata failed: {ex.Message}");
                return cachedMatches;
            }

            throw;
        }
    }

    private IReadOnlyList<ManagedModuleCatalogMatch> FindCatalogs(ManagedModuleRepository repository)
        => _catalogStores
            .SelectMany(store => store.GetCatalogs()
                .Where(static item => item.Mode != ManagedModuleCatalogCacheMode.Off)
                .Where(item => CatalogMatches(repository, item))
                .Select(catalog => new ManagedModuleCatalogMatch(store, catalog)))
            .ToArray();

    private IReadOnlyList<ManagedModuleVersionInfo> GetFirstCatalogVersions(
        ManagedModuleRepository repository,
        IReadOnlyList<ManagedModuleCatalogMatch> catalogs,
        string packageId,
        bool includePrerelease)
        => catalogs
            .Select(catalog => GetCatalogVersions(repository, catalog.Catalog, packageId, includePrerelease))
            .FirstOrDefault(static versions => versions.Count > 0) ??
           Array.Empty<ManagedModuleVersionInfo>();

    private ManagedModuleVersionInfo? GetFirstLatestCatalogVersion(
        ManagedModuleRepository repository,
        IReadOnlyList<ManagedModuleCatalogMatch> catalogs,
        string packageId,
        bool includePrerelease)
        => catalogs
            .Select(catalog => GetLatestCatalogVersion(repository, catalog.Catalog, packageId, includePrerelease))
            .FirstOrDefault(static version => version is not null);

    private async Task WarmCatalogsAsync(
        IReadOnlyList<ManagedModuleCatalogMatch> catalogs,
        string packageId,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
        => await WarmCatalogsAsync(catalogs, new[] { packageId }, credential, cancellationToken).ConfigureAwait(false);

    private async Task WarmCatalogsAsync(
        IReadOnlyList<ManagedModuleCatalogMatch> catalogs,
        IEnumerable<string> packageIds,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var packages = packageIds
            .Where(static packageId => !string.IsNullOrWhiteSpace(packageId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packages.Length == 0)
            return;

        foreach (var catalog in catalogs)
        {
            try
            {
                await catalog.Store.UpdateCatalogAsync(
                        new ManagedModuleCatalogUpdateRequest
                        {
                            Name = catalog.Catalog.Name,
                            PackageNames = packages,
                            IncludePrerelease = catalog.Catalog.IncludePrerelease,
                            Credential = credential
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (CanFallbackToCatalog(ex))
            {
                _logger.Verbose($"Managed module catalog '{catalog.Catalog.Name}' read-through refresh skipped: {ex.Message}");
            }
        }
    }

    private IReadOnlyList<ManagedModuleVersionInfo> GetCatalogVersions(
        ManagedModuleRepository repository,
        ManagedModuleCatalog catalog,
        string packageId,
        bool includePrerelease)
    {
        var package = FindCatalogPackage(catalog, packageId);
        if (package is null || !IsCatalogPackageUsable(catalog, package))
            return Array.Empty<ManagedModuleVersionInfo>();

        return package.Versions
            .Where(version => includePrerelease || !version.IsPrerelease)
            .OrderBy(version => version.Version, ManagedModuleVersionComparer.Instance)
            .Select(version => CreateCatalogVersionInfo(repository, package, version))
            .ToArray();
    }

    private ManagedModuleVersionInfo? GetLatestCatalogVersion(
        ManagedModuleRepository repository,
        ManagedModuleCatalog catalog,
        string packageId,
        bool includePrerelease)
        => GetCatalogVersions(repository, catalog, packageId, includePrerelease)
            .Where(static version => version.Listed)
            .OrderBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .LastOrDefault();

    private IReadOnlyList<ManagedModuleVersionInfo> SearchCatalogVersions(
        ManagedModuleRepository repository,
        IReadOnlyList<ManagedModuleCatalogMatch> catalogs,
        string query,
        bool includePrerelease,
        int take,
        int skip)
        => catalogs
            .SelectMany(catalog => SearchCatalogVersions(repository, catalog.Catalog, query, includePrerelease))
            .GroupBy(static version => version.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static version => version.Name, StringComparer.OrdinalIgnoreCase)
            .Skip(Math.Max(0, skip))
            .Take(Math.Max(1, take))
            .ToArray();

    private IReadOnlyList<ManagedModuleVersionInfo> SearchCatalogVersions(
        ManagedModuleRepository repository,
        ManagedModuleCatalog catalog,
        string query,
        bool includePrerelease)
        => catalog.Packages
            .Where(package => IsCatalogPackageUsable(catalog, package))
            .Where(package => ManagedModuleSearchMatcher.IsMatch(query, package.Id))
            .Select(package => package.Versions
                .Where(version => includePrerelease || !version.IsPrerelease)
                .Where(static version => version.Listed)
                .OrderBy(version => version.Version, ManagedModuleVersionComparer.Instance)
                .Select(version => CreateCatalogVersionInfo(repository, package, version))
                .LastOrDefault())
            .Where(static version => version is not null)
            .Select(static version => version!)
            .ToArray();

    private static ManagedModuleCatalogPackage? FindCatalogPackage(ManagedModuleCatalog catalog, string packageId)
        => catalog.Packages.FirstOrDefault(package => string.Equals(package.Id, packageId, StringComparison.OrdinalIgnoreCase));

    private static ManagedModuleVersionInfo CreateCatalogVersionInfo(
        ManagedModuleRepository repository,
        ManagedModuleCatalogPackage package,
        ManagedModuleCatalogVersion version)
        => new()
        {
            Name = package.Id,
            Version = version.Version,
            RepositoryName = repository.Name,
            RepositorySource = repository.Source,
            PackageSource = version.CdnPackageSource ?? version.PackageSource,
            IsPrerelease = version.IsPrerelease || ManagedModuleVersionComparer.IsPrerelease(version.Version),
            Listed = version.Listed,
            License = version.License,
            RequireLicenseAcceptance = version.RequireLicenseAcceptance,
            Dependencies = version.Dependencies,
            Tags = package.Tags
        };

    private static bool IsCatalogPackageUsable(ManagedModuleCatalog catalog, ManagedModuleCatalogPackage package)
    {
        if (catalog.Mode == ManagedModuleCatalogCacheMode.Offline)
            return package.Versions.Count > 0;

        var refreshedAt = package.LastRefreshAtUtc ?? catalog.LastRefreshAtUtc;
        if (refreshedAt is null)
            return package.Versions.Count > 0;

        return package.Versions.Count > 0 && DateTimeOffset.UtcNow - refreshedAt.Value <= catalog.MaxStaleness;
    }

    private static bool CatalogMatches(ManagedModuleRepository repository, ManagedModuleCatalog catalog)
    {
        if (string.Equals(repository.Name, catalog.Name, StringComparison.OrdinalIgnoreCase))
            return true;

        var repositorySource = NormalizeCatalogSource(repository.Source);
        var catalogSource = NormalizeCatalogSource(catalog.Source);
        return string.Equals(repositorySource, catalogSource, StringComparison.OrdinalIgnoreCase) ||
               IsPowerShellGallerySource(repositorySource) && IsPowerShellGallerySource(catalogSource);
    }

    private static string NormalizeCatalogSource(string source)
        => source.Trim().TrimEnd('/');

    private static bool IsPowerShellGallerySource(string source)
        => string.Equals(source, ManagedModuleCatalogDefaults.PowerShellGalleryV3.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(source, ManagedModuleCatalogDefaults.PowerShellGalleryV2.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static bool CanFallbackToCatalog(Exception exception)
        => exception is not OperationCanceledException;

    private sealed class ManagedModuleCatalogMatch
    {
        public ManagedModuleCatalogMatch(ManagedModuleCatalogStore store, ManagedModuleCatalog catalog)
        {
            Store = store;
            Catalog = catalog;
        }

        public ManagedModuleCatalogStore Store { get; }

        public ManagedModuleCatalog Catalog { get; }
    }
}
