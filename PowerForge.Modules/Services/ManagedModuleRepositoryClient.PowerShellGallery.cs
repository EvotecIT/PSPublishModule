using System.Net.Http;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    private async Task<ManagedModuleDownloadResult> DownloadNuGetPackageWithPowerShellGalleryCdnAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        string destinationDirectory,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (!ShouldUsePowerShellGalleryV2ReadApi(repository))
            return await DownloadNuGetPackageAsync(repository, packageId, version, destinationDirectory, credential, cancellationToken).ConfigureAwait(false);

        return await DownloadPowerShellGalleryPackageWithCdnFallbackAsync(
                repository,
                packageId,
                version,
                destinationDirectory,
                credential,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ManagedModuleDownloadResult> DownloadPowerShellGalleryPackageWithCdnFallbackAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        string destinationDirectory,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var packageUri = BuildPowerShellGalleryCdnPackageUri(packageId, version);
        var result = await TryDownloadHttpPackageAsync(
                repository,
                packageUri,
                packageId,
                version,
                destinationDirectory,
                credential,
                cancellationToken)
            .ConfigureAwait(false);
        if (result is not null)
            return result;

        var fallback = CreatePowerShellGalleryV2Fallback(repository);
        return await DownloadNuGetV2PackageDirectAsync(fallback, packageId, version, destinationDirectory, credential, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedModuleDownloadResult?> TryDownloadHttpPackageAsync(
        ManagedModuleRepository repository,
        Uri packageUri,
        string packageId,
        string version,
        string destinationDirectory,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        return await DownloadHttpPackageToFileAsync(repository, packageUri, packageId, version,
            destinationDirectory, credential, allowMissing: true, cancellationToken).ConfigureAwait(false);
    }

    private static Uri BuildPowerShellGalleryCdnPackageUri(string packageId, string version)
    {
        var lowerId = packageId.Trim().ToLowerInvariant();
        var lowerVersion = version.Trim().ToLowerInvariant();
        return new Uri(
            "https://cdn.powershellgallery.com/packages/" +
            $"{Uri.EscapeDataString(lowerId)}.{Uri.EscapeDataString(lowerVersion)}.nupkg");
    }

    private static bool ShouldUsePowerShellGalleryCdn(ManagedModuleRepository repository)
        => IsPowerShellGalleryV3Index(repository.Source) || IsPowerShellGalleryV2Endpoint(repository.Source);

    private static bool IsPowerShellGalleryV2Endpoint(string source)
    {
        var normalized = source.Trim().TrimEnd('/');
        return normalized.Equals("https://www.powershellgallery.com/api/v2", StringComparison.OrdinalIgnoreCase);
    }
}
