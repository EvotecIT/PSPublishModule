namespace PowerForge;

public sealed partial class ManagedModuleInstallService
{
#if !NET472
    private async Task<ManagedModuleBufferedPackage> DownloadBufferedPackageForInstallAsync(
        ManagedModuleInstallRequest request,
        string version,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        var prefetchKey = TryCreateBufferedPackagePrefetchKey(request, version);
        if (prefetchKey is not null)
        {
            var prefetched = await context.TryTakeBufferedPackagePrefetchAsync(
                    prefetchKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (prefetched is not null)
                return prefetched;
        }

        return await _repositoryClient.DownloadPackageToMemoryAsync(
                request.Repository,
                request.Name,
                version,
                request.Credential,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ManagedModuleArchiveExtractionResult> ExtractBufferedPackageForInstallAsync(
        ManagedModuleBufferedPackage bufferedPackage,
        string stageModulePath,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        bufferedPackage.PackageStream.Position = 0;
        if (!context.IsDependencyInstall)
        {
            return _extractor.ExtractBufferedPackage(
                bufferedPackage.PackageStream,
                stageModulePath,
                bufferedPackage.Download.Metadata?.Id,
                cancellationToken);
        }

        return await _extractor.ExtractPackageAsync(
                bufferedPackage.PackageStream,
                stageModulePath,
                bufferedPackage.Download.Metadata?.Id,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool ShouldUseBufferedPackageExtraction(ManagedModuleInstallRequest request, bool ownsCache)
        => (ownsCache || request.PackageCacheDirectoryIsOperationLocal) &&
           request.Repository.Kind is ManagedModuleRepositoryKind.NuGetV2 or ManagedModuleRepositoryKind.NuGetV3;
#endif
}
