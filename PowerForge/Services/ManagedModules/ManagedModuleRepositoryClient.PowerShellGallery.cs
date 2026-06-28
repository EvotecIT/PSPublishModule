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

#if NET472
        var netFrameworkFallback = CreatePowerShellGalleryV2Fallback(repository);
        return await DownloadNuGetV2PackageAsync(netFrameworkFallback, packageId, version, destinationDirectory, credential, cancellationToken).ConfigureAwait(false);
#else
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
        return await DownloadNuGetV2PackageAsync(fallback, packageId, version, destinationDirectory, credential, cancellationToken).ConfigureAwait(false);
#endif
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
        var destinationPath = BuildDestinationPath(destinationDirectory, packageId, version);
        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, packageUri, credential, "application/octet-stream"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        PackageCopyResult packageCopy;
        using (var source = await ReadContentStreamAsync(response.Content, cancellationToken).ConfigureAwait(false))
        {
            packageCopy = await CopyPackageStreamWithHashAsync(source, destinationPath, cancellationToken).ConfigureAwait(false);
        }

        return new ManagedModuleDownloadResult
        {
            Name = packageId,
            Version = version,
            RepositoryName = repository.Name,
            Source = packageUri.ToString(),
            PackagePath = destinationPath,
            BytesWritten = packageCopy.BytesWritten,
            PackageSha256 = packageCopy.Sha256,
            Metadata = _packageReader.ReadMetadata(destinationPath)
        };
    }

    private static Uri BuildPowerShellGalleryCdnPackageUri(string packageId, string version)
    {
        var lowerId = packageId.Trim().ToLowerInvariant();
        var lowerVersion = version.Trim().ToLowerInvariant();
        return new Uri(
            "https://cdn.powershellgallery.com/packages/" +
            $"{Uri.EscapeDataString(lowerId)}.{Uri.EscapeDataString(lowerVersion)}.nupkg");
    }
}
