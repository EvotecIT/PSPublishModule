namespace PowerForge;

public sealed partial class ManagedModuleInstallService
{
#if !NET472
    private async Task<ManagedModuleBufferedPackage> DownloadBufferedPackageForInstallAsync(
        ManagedModuleInstallRequest request,
        string version,
        CancellationToken cancellationToken)
        => await _repositoryClient.DownloadPackageToMemoryAsync(
                request.Repository,
                request.Name,
                version,
                request.Credential,
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<ManagedModuleArchiveExtractionResult> ExtractBufferedPackageForInstallAsync(
        ManagedModuleBufferedPackage bufferedPackage,
        string stageModulePath,
        CancellationToken cancellationToken)
    {
        bufferedPackage.PackageStream.Position = 0;
        return await _extractor.ExtractPackageAsync(
                bufferedPackage.PackageStream,
                stageModulePath,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool ShouldUseBufferedPackageExtraction(ManagedModuleInstallRequest request, bool ownsCache)
        => ownsCache &&
           request.Repository.Kind is ManagedModuleRepositoryKind.NuGetV2 or ManagedModuleRepositoryKind.NuGetV3;
#endif
}
