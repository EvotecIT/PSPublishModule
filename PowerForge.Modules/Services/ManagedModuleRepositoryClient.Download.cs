using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    private const long MaximumBufferedPackageBytes = 128L * 1024 * 1024;

    private static async Task<PackageCopyResult> CopyPackageStreamWithHashAsync(
        Stream source,
        string destinationPath,
        long maxPackageBytes,
        CancellationToken cancellationToken,
        long? attemptStarted = null)
    {
        var buffer = new byte[PackageCopyBufferSize];
        // Read only one byte here so a slow first chunk is reflected in the body rate.
        int? firstRead = attemptStarted.HasValue
            ? await ReadPackageBytesAsync(source, buffer, 1, cancellationToken).ConfigureAwait(false)
            : null;
        long? firstByte = firstRead > 0 ? Stopwatch.GetTimestamp() : null;

        using var sha256 = SHA256.Create();
        long bytesWritten = 0;

        long bodyEnd;
        using (var destination = CreatePackageFileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            try
            {
                while (true)
                {
                    var bytesRead = firstRead ?? (attemptStarted.HasValue
                        ? await ReadPackageBytesAsync(source, buffer, buffer.Length, cancellationToken).ConfigureAwait(false)
                        : await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false));
                    firstRead = null;
                    if (bytesRead == 0) break;
                    if (maxPackageBytes > 0 && bytesWritten + bytesRead > maxPackageBytes)
                        throw new InvalidOperationException($"Package download exceeded the managed module package size limit of {maxPackageBytes} bytes.");
                    await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                    bytesWritten += bytesRead;
                }
                bodyEnd = firstByte.HasValue ? Stopwatch.GetTimestamp() : 0;
            }
            catch
            {
                // Cleanup only after this attempt successfully opened its destination.
                destination.Dispose();
                TryDeleteFile(destinationPath);
                throw;
            }
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return new PackageCopyResult(bytesWritten, FormatSha256(sha256.Hash), attemptStarted, firstByte, bodyEnd);
    }

    private static async Task<Stream> OpenPackageResponseStreamAsync(HttpContent content, CancellationToken cancellationToken)
    {
        try { return await ReadContentStreamAsync(content, cancellationToken).ConfigureAwait(false); }
        catch (IOException ex) { throw new HttpRequestException("Unable to open the package response body.", ex); }
    }

    private static async Task<int> ReadPackageBytesAsync(Stream source, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        try { return await source.ReadAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false); }
        catch (IOException ex)
        {
            throw new HttpRequestException("Package response body transfer failed.", ex);
        }
    }

    private Task<ManagedModuleDownloadResult?> DownloadHttpPackageToFileAsync(
        ManagedModuleRepository repository,
        Uri packageUri,
        string packageId,
        string version,
        string destinationDirectory,
        RepositoryCredential? credential,
        bool allowMissing,
        CancellationToken cancellationToken)
    {
        var destinationPath = BuildDestinationPath(destinationDirectory, repository, packageId, version);
        return SendWithPolicyAsync<ManagedModuleDownloadResult?>(
            () => CreateRequest(HttpMethod.Get, packageUri, credential, "application/octet-stream"),
            async (response, attemptStarted, readToken) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    if (allowMissing) return null;
                    throw CreateRepositoryHttpException(repository, "Download", response.StatusCode,
                        $"Unable to download package '{packageId}' version '{version}'.");
                }
                using var source = await OpenPackageResponseStreamAsync(response.Content, readToken).ConfigureAwait(false);
                var copy = await CopyPackageStreamWithHashAsync(source, destinationPath, _options.MaxPackageBytes, readToken, attemptStarted).ConfigureAwait(false);
                try
                {
                    return new ManagedModuleDownloadResult
                    {
                        Name = packageId,
                        Version = version,
                        RepositoryName = repository.Name,
                        Source = packageUri.ToString(),
                        PackagePath = destinationPath,
                        BytesWritten = copy.BytesWritten,
                        TimeToFirstByte = copy.TimeToFirstByte,
                        BodyTransferElapsed = copy.BodyTransferElapsed,
                        PackageSha256 = copy.Sha256,
                        Metadata = ReadDownloadedPackageMetadata(packageId, version, destinationPath)
                    };
                }
                catch
                {
                    TryDeleteFile(destinationPath);
                    throw;
                }
            }, cancellationToken);
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
    {
        if (ShouldUsePowerShellGalleryCdn(repository))
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
        }

        return await DownloadHttpPackageToMemoryAsync(
                repository,
                BuildNuGetV2PackageUri(repository.Source, packageId, version),
                packageId,
                version,
                credential,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ManagedModuleBufferedPackage?> TryDownloadHttpPackageToMemoryAsync(
        ManagedModuleRepository repository,
        Uri packageUri,
        string packageId,
        string version,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        return await SendWithPolicyAsync<ManagedModuleBufferedPackage?>(
            () => CreateRequest(HttpMethod.Get, packageUri, credential, "application/octet-stream"),
            async (response, attemptStarted, readToken) => !response.IsSuccessStatusCode ? null :
                await ReadHttpPackageToMemoryAsync(repository, packageUri, packageId, version, response, attemptStarted, readToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedModuleBufferedPackage> DownloadHttpPackageToMemoryAsync(
        ManagedModuleRepository repository,
        Uri packageUri,
        string packageId,
        string version,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        return await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, packageUri, credential, "application/octet-stream"),
            async (response, attemptStarted, readToken) =>
            {
                if (!response.IsSuccessStatusCode)
                    throw CreateRepositoryHttpException(repository, "Download", response.StatusCode,
                        $"Unable to download package '{packageId}' version '{version}'.");
                return await ReadHttpPackageToMemoryAsync(repository, packageUri, packageId, version, response, attemptStarted, readToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedModuleBufferedPackage> ReadHttpPackageToMemoryAsync(
        ManagedModuleRepository repository,
        Uri packageUri,
        string packageId,
        string version,
        HttpResponseMessage response,
        long attemptStarted,
        CancellationToken cancellationToken)
    {
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && _options.MaxPackageBytes > 0 && contentLength.Value > _options.MaxPackageBytes)
            throw new InvalidOperationException(
                $"Package download exceeded the managed module package size limit of {_options.MaxPackageBytes} bytes.");
        if (contentLength.HasValue && contentLength.Value > MaximumBufferedPackageBytes)
            throw new ManagedModuleBufferedPackageTooLargeException(
                $"Package '{packageId}' version '{version}' is {contentLength.Value} bytes, which exceeds the in-memory extraction cap of {MaximumBufferedPackageBytes} bytes.");

        BufferedPackageCopyResult packageCopy;
        using (var source = await OpenPackageResponseStreamAsync(response.Content, cancellationToken).ConfigureAwait(false))
        {
            packageCopy = await CopyPackageStreamToMemoryWithHashAsync(
                    source,
                    _options.MaxPackageBytes,
                    MaximumBufferedPackageBytes,
                    contentLength,
                    packageId,
                    version,
                    attemptStarted,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var metadata = ReadDownloadedPackageMetadata(packageId, version, packageCopy.Stream, packageUri.ToString());
        var download = new ManagedModuleDownloadResult
        {
            Name = packageId,
            Version = version,
            RepositoryName = repository.Name,
            Source = packageUri.ToString(),
            PackagePath = string.Empty,
            BytesWritten = packageCopy.Copy.BytesWritten,
            TimeToFirstByte = packageCopy.Copy.TimeToFirstByte,
            BodyTransferElapsed = packageCopy.Copy.BodyTransferElapsed,
            PackageSha256 = packageCopy.Copy.Sha256,
            Metadata = metadata
        };

        return new ManagedModuleBufferedPackage(download, packageCopy.Stream);
    }

    private static async Task<BufferedPackageCopyResult> CopyPackageStreamToMemoryWithHashAsync(
        Stream source,
        long maxPackageBytes,
        long maxBufferedBytes,
        long? expectedBytes,
        string packageId,
        string version,
        long attemptStarted,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[PackageCopyBufferSize];
        var bytesRead = await ReadPackageBytesAsync(source, buffer, 1, cancellationToken).ConfigureAwait(false);
        long? firstByte = bytesRead > 0 ? Stopwatch.GetTimestamp() : null;

        using var sha256 = SHA256.Create();
        var destination = CreateBufferedPackageStream(expectedBytes, maxPackageBytes, maxBufferedBytes);
        long bytesWritten = 0;

        try
        {
            while (bytesRead > 0)
            {
                var nextSize = bytesWritten + bytesRead;
                if (maxPackageBytes > 0 && nextSize > maxPackageBytes)
                    throw new InvalidOperationException(
                        $"Package download exceeded the managed module package size limit of {maxPackageBytes} bytes.");
                if (maxBufferedBytes > 0 && nextSize > maxBufferedBytes)
                    throw new ManagedModuleBufferedPackageTooLargeException(
                        $"Package '{packageId}' version '{version}' exceeded the in-memory extraction cap of {maxBufferedBytes} bytes.");

                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                bytesWritten += bytesRead;
                bytesRead = await ReadPackageBytesAsync(source, buffer, buffer.Length, cancellationToken).ConfigureAwait(false);
            }

            var bodyEnd = Stopwatch.GetTimestamp();
            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            destination.Position = 0;
            var copy = new PackageCopyResult(bytesWritten, FormatSha256(sha256.Hash), attemptStarted, firstByte, bodyEnd);
            return new BufferedPackageCopyResult(destination, copy);
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

    private static MemoryStream CreateBufferedPackageStream(
        long? expectedBytes,
        long maxPackageBytes,
        long maxBufferedBytes)
    {
        if (!expectedBytes.HasValue || expectedBytes.Value <= 0)
            return new MemoryStream();
        if (maxPackageBytes > 0 && expectedBytes.Value > maxPackageBytes)
            return new MemoryStream();
        if (maxBufferedBytes > 0 && expectedBytes.Value > maxBufferedBytes)
            return new MemoryStream();
        if (expectedBytes.Value > int.MaxValue)
            return new MemoryStream();

        return new MemoryStream((int)expectedBytes.Value);
    }

    private static string FormatSha256(byte[]? hash)
    {
        if (hash is null || hash.Length == 0)
            return string.Empty;

        return string.Concat(hash.Select(static value => value.ToString("x2")));
    }

    private readonly struct PackageCopyResult
    {
        public PackageCopyResult(long bytesWritten, string sha256, long? attemptStarted, long? firstByte, long bodyEnd)
        {
            BytesWritten = bytesWritten;
            Sha256 = sha256;
            TimeToFirstByte = firstByte.HasValue && attemptStarted.HasValue
                ? GetElapsed(attemptStarted.Value, firstByte.Value)
                : null;
            BodyTransferElapsed = firstByte.HasValue ? GetElapsed(firstByte.Value, bodyEnd) : null;
        }

        public long BytesWritten { get; }

        public string Sha256 { get; }
        public TimeSpan? TimeToFirstByte { get; }
        public TimeSpan? BodyTransferElapsed { get; }

        private static TimeSpan GetElapsed(long start, long end)
            => TimeSpan.FromTicks((long)((end - start) * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
    }

#if !NET472
    private readonly struct BufferedPackageCopyResult
    {
        public BufferedPackageCopyResult(MemoryStream stream, PackageCopyResult copy)
        {
            Stream = stream;
            Copy = copy;
        }

        public MemoryStream Stream { get; }

        public PackageCopyResult Copy { get; }
    }
#endif
}

#if !NET472
internal sealed class ManagedModuleBufferedPackageTooLargeException : Exception
{
    public ManagedModuleBufferedPackageTooLargeException(string message)
        : base(message)
    {
    }
}

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
