using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    private static async Task<PackageCopyResult> CopyPackageStreamWithHashAsync(
        Stream source,
        string destinationPath,
        long maxPackageBytes,
        CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var buffer = new byte[PackageCopyBufferSize];
        long bytesWritten = 0;

        using (var destination = CreatePackageFileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    break;

                if (maxPackageBytes > 0 && bytesWritten + bytesRead > maxPackageBytes)
                {
                    destination.Dispose();
                    TryDeleteFile(destinationPath);
                    throw new InvalidOperationException(
                        $"Package download exceeded the managed module package size limit of {maxPackageBytes} bytes.");
                }

                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                bytesWritten += bytesRead;
            }
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return new PackageCopyResult(bytesWritten, FormatSha256(sha256.Hash));
    }

#if !NET472
    internal async Task<ManagedModuleBufferedPackage> DownloadPackageToMemoryAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        RepositoryCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        if (repository is null)
            throw new ArgumentNullException(nameof(repository));
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package id is required.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version is required.", nameof(version));

        packageId = ManagedModulePackageIdentity.RequireSafeId(packageId, nameof(packageId));
        version = ManagedModulePackageIdentity.RequireSafeVersion(version, nameof(version));
        return repository.Kind switch
        {
            ManagedModuleRepositoryKind.NuGetV3 => await DownloadNuGetPackageToMemoryAsync(repository, packageId, version, credential, cancellationToken).ConfigureAwait(false),
            ManagedModuleRepositoryKind.NuGetV2 => await DownloadNuGetV2PackageToMemoryAsync(repository, packageId, version, credential, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Repository kind '{repository.Kind}' does not support in-memory package delivery.")
        };
    }

    private async Task<ManagedModuleBufferedPackage> DownloadNuGetPackageToMemoryAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (ShouldUsePowerShellGalleryV2ReadApi(repository))
        {
            var cdn = await TryDownloadHttpPackageToMemoryAsync(
                    repository,
                    BuildPowerShellGalleryCdnPackageUri(packageId, version),
                    packageId,
                    version,
                    credential,
                    cancellationToken)
                .ConfigureAwait(false);
            if (cdn is not null)
                return cdn;

            var fallback = CreatePowerShellGalleryV2Fallback(repository);
            return await DownloadNuGetV2PackageToMemoryAsync(fallback, packageId, version, credential, cancellationToken).ConfigureAwait(false);
        }

        var packageBase = await ResolvePackageBaseAddressAsync(repository, credential, cancellationToken).ConfigureAwait(false);
        return await DownloadHttpPackageToMemoryAsync(
                repository,
                BuildPackageUri(packageBase, packageId, version),
                packageId,
                version,
                credential,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ManagedModuleBufferedPackage> DownloadNuGetV2PackageToMemoryAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
        => await DownloadHttpPackageToMemoryAsync(
                repository,
                BuildNuGetV2PackageUri(repository.Source, packageId, version),
                packageId,
                version,
                credential,
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<ManagedModuleBufferedPackage?> TryDownloadHttpPackageToMemoryAsync(
        ManagedModuleRepository repository,
        Uri packageUri,
        string packageId,
        string version,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, packageUri, credential, "application/octet-stream"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        return await ReadHttpPackageToMemoryAsync(repository, packageUri, packageId, version, response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedModuleBufferedPackage> DownloadHttpPackageToMemoryAsync(
        ManagedModuleRepository repository,
        Uri packageUri,
        string packageId,
        string version,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, packageUri, credential, "application/octet-stream"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateRepositoryHttpException(repository, "Download", response.StatusCode, $"Unable to download package '{packageId}' version '{version}'.");

        return await ReadHttpPackageToMemoryAsync(repository, packageUri, packageId, version, response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedModuleBufferedPackage> ReadHttpPackageToMemoryAsync(
        ManagedModuleRepository repository,
        Uri packageUri,
        string packageId,
        string version,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        BufferedPackageCopyResult packageCopy;
        using (var source = await ReadContentStreamAsync(response.Content, cancellationToken).ConfigureAwait(false))
        {
            packageCopy = await CopyPackageStreamToMemoryWithHashAsync(source, _options.MaxPackageBytes, cancellationToken).ConfigureAwait(false);
        }

        var metadata = ReadDownloadedPackageMetadata(packageId, version, packageCopy.Stream, packageUri.ToString());
        var download = new ManagedModuleDownloadResult
        {
            Name = packageId,
            Version = version,
            RepositoryName = repository.Name,
            Source = packageUri.ToString(),
            PackagePath = string.Empty,
            BytesWritten = packageCopy.BytesWritten,
            PackageSha256 = packageCopy.Sha256,
            Metadata = metadata
        };

        return new ManagedModuleBufferedPackage(download, packageCopy.Stream);
    }

    private static async Task<BufferedPackageCopyResult> CopyPackageStreamToMemoryWithHashAsync(
        Stream source,
        long maxPackageBytes,
        CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var buffer = new byte[PackageCopyBufferSize];
        var destination = new MemoryStream();
        long bytesWritten = 0;

        try
        {
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    break;

                if (maxPackageBytes > 0 && bytesWritten + bytesRead > maxPackageBytes)
                    throw new InvalidOperationException(
                        $"Package download exceeded the managed module package size limit of {maxPackageBytes} bytes.");

                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                bytesWritten += bytesRead;
            }

            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            destination.Position = 0;
            return new BufferedPackageCopyResult(destination, bytesWritten, FormatSha256(sha256.Hash));
        }
        catch
        {
            destination.Dispose();
            throw;
        }
    }
#endif

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup after refusing an oversized package payload.
        }
    }

    private static string FormatSha256(byte[]? hash)
    {
        if (hash is null || hash.Length == 0)
            return string.Empty;

        return string.Concat(hash.Select(static value => value.ToString("x2")));
    }

    private readonly struct PackageCopyResult
    {
        public PackageCopyResult(long bytesWritten, string sha256)
        {
            BytesWritten = bytesWritten;
            Sha256 = sha256;
        }

        public long BytesWritten { get; }

        public string Sha256 { get; }
    }

#if !NET472
    private readonly struct BufferedPackageCopyResult
    {
        public BufferedPackageCopyResult(MemoryStream stream, long bytesWritten, string sha256)
        {
            Stream = stream;
            BytesWritten = bytesWritten;
            Sha256 = sha256;
        }

        public MemoryStream Stream { get; }

        public long BytesWritten { get; }

        public string Sha256 { get; }
    }
#endif
}

#if !NET472
internal sealed class ManagedModuleBufferedPackage : IDisposable
{
    public ManagedModuleBufferedPackage(ManagedModuleDownloadResult download, MemoryStream packageStream)
    {
        Download = download;
        PackageStream = packageStream;
    }

    public ManagedModuleDownloadResult Download { get; }

    public MemoryStream PackageStream { get; }

    public void Dispose()
        => PackageStream.Dispose();
}
#endif
