using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    private static async Task<PackageCopyResult> CopyPackageStreamWithHashAsync(
        Stream source,
        string destinationPath,
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

                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                bytesWritten += bytesRead;
            }
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return new PackageCopyResult(bytesWritten, FormatSha256(sha256.Hash));
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
}
