namespace PowerForge;

public sealed partial class ManagedModuleInstallService
{
#if !NET472
    private const int MaximumDependencyPackagePrefetchCount = 64;

    private void StartDependencyPackagePrefetch(
        ManagedModuleInstallRequest request,
        ManagedModulePackageMetadata? metadata,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        if (!ShouldUseDependencyPackagePrefetch(request) || cancellationToken.IsCancellationRequested)
            return;

        var dependencyTrustPolicy = ResolveDependencyTrustPolicy(request.TrustPolicy);
        var dependencies = SelectDependencies(metadata)
            .Take(MaximumDependencyPackagePrefetchCount)
            .ToArray();
        foreach (var dependency in dependencies)
        {
            StartDependencyPackagePrefetchCore(
                request,
                dependency,
                dependencyTrustPolicy,
                context,
                cancellationToken);
        }
    }

    private void StartDependencyPackagePrefetchCore(
        ManagedModuleInstallRequest request,
        ManagedModuleDependencyInfo dependency,
        ManagedModuleTrustPolicy? dependencyTrustPolicy,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        var range = ManagedModuleVersionRange.Parse(dependency.VersionRange);
        if (dependencyTrustPolicy is null && HasSatisfiedDependency(request, dependency.Id, range, context))
            return;

        _ = StartDependencyPackagePrefetchCoreAsync(
            request,
            dependency,
            range,
            context,
            cancellationToken);
    }

    private async Task StartDependencyPackagePrefetchCoreAsync(
        ManagedModuleInstallRequest request,
        ManagedModuleDependencyInfo dependency,
        ManagedModuleVersionRange range,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var versionSelection = await ResolveDependencyVersionAsync(
                    request,
                    dependency.Id,
                    range,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
            var prefetchKey = TryCreateBufferedPackagePrefetchKey(
                request.Repository,
                dependency.Id,
                versionSelection.Version,
                request.Credential);
            if (prefetchKey is null)
                return;

            context.StartBufferedPackagePrefetch(
                prefetchKey,
                token => DownloadPrefetchedPackageAsync(
                    request,
                    dependency.Id,
                    versionSelection.Version,
                    token),
                cancellationToken);
        }
        catch
        {
            // Prefetch is opportunistic. The normal install path reports actionable errors.
        }
    }

    private static bool ShouldUseDependencyPackagePrefetch(ManagedModuleInstallRequest request)
        => !request.SkipDependencyCheck &&
           !request.SaveAsNupkg &&
           !request.AuthenticodeCheck &&
           request.Credential is null &&
           (string.IsNullOrWhiteSpace(request.PackageCacheDirectory) || request.PackageCacheDirectoryIsOperationLocal) &&
           request.Repository.Kind is ManagedModuleRepositoryKind.NuGetV2 or ManagedModuleRepositoryKind.NuGetV3;

    private async Task<ManagedModuleBufferedPackage> DownloadPrefetchedPackageAsync(
        ManagedModuleInstallRequest request,
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        using var requestScope = _repositoryClient.BeginRequestScope();
        var package = await _repositoryClient.DownloadPackageToMemoryAsync(
                request.Repository,
                packageId,
                version,
                request.Credential,
                cancellationToken)
            .ConfigureAwait(false);
        package.Download.RequestCount = Math.Max(1, requestScope.Count);
        package.Download.RedirectCount = requestScope.RedirectCount;
        return package;
    }

    private static string? TryCreateBufferedPackagePrefetchKey(
        ManagedModuleInstallRequest request,
        string version)
        => TryCreateBufferedPackagePrefetchKey(
            request.Repository,
            request.Name,
            version,
            request.Credential);

    private static string? TryCreateBufferedPackagePrefetchKey(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        RepositoryCredential? credential)
    {
        if (credential is not null ||
            repository.Kind is not (ManagedModuleRepositoryKind.NuGetV2 or ManagedModuleRepositoryKind.NuGetV3))
        {
            return null;
        }

        return string.Join(
            "|",
            "buffered-package",
            repository.Kind.ToString(),
            NormalizeDependencyVersionCacheValue(repository.Source),
            NormalizeDependencyVersionCacheValue(packageId),
            NormalizeDependencyVersionCacheValue(version));
    }
#else
    private static void StartDependencyPackagePrefetch(
        ManagedModuleInstallRequest request,
        ManagedModulePackageMetadata? metadata,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
    }
#endif
}
