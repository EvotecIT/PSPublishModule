using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace PowerForge;

/// <summary>
/// Captures screenshot bytes into a user-only private file and exposes bounded range content for App Store upload operations.
/// </summary>
internal sealed class AppStoreConnectScreenshotUploadSnapshot : IDisposable
{
    private bool _disposed;

    private AppStoreConnectScreenshotUploadSnapshot(
        string rootPath,
        string filePath,
        long length,
        string sha256,
        string md5)
    {
        RootPath = rootPath;
        FilePath = filePath;
        Length = length;
        Sha256 = sha256;
        Md5 = md5;
    }

    internal string RootPath { get; }

    internal string FilePath { get; }

    internal long Length { get; }

    internal string Sha256 { get; }

    internal string Md5 { get; }

    internal static AppStoreConnectScreenshotUploadSnapshot Capture(string sourcePath, string? expectedSha256)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
            throw new FileNotFoundException("Screenshot file was not found.", source);

        var root = Path.Combine(Path.GetTempPath(), "PowerForge", "appstore-screenshot-upload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        var snapshotPath = Path.Combine(root, "screenshot-bytes");
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
            using (var output = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.SequentialScan))
                input.CopyTo(output, 81920);
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(snapshotPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#endif

            var sha256 = ComputeHash(snapshotPath, SHA256.Create);
            if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                !sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Screenshot '{source}' changed after its immutable upload snapshot was captured.");
            }

            return new AppStoreConnectScreenshotUploadSnapshot(
                root,
                snapshotPath,
                new FileInfo(snapshotPath).Length,
                sha256,
                ComputeHash(snapshotPath, MD5.Create));
        }
        catch
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
            throw;
        }
    }

    internal HttpContent CreateRangeContent(long offset, long length)
    {
        if (offset < 0 || length < 0 || offset > Length || length > Length - offset)
            throw new EndOfStreamException("Captured screenshot bytes ended before the upload operation range.");
        return new RangedFileContent(FilePath, offset, length);
    }

    internal void ValidateUnchanged()
    {
        var currentSha256 = ComputeHash(FilePath, SHA256.Create);
        if (!currentSha256.Equals(Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The private screenshot upload snapshot changed while App Store Connect was reading it; discard the upload result and retry from approved bytes.");
        }
    }

    private static string ComputeHash(string filePath, Func<HashAlgorithm> createHash)
    {
        using var stream = File.OpenRead(filePath);
        using var hash = createHash();
        return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }

    private sealed class RangedFileContent : HttpContent
    {
        private readonly string _filePath;
        private readonly long _offset;
        private readonly long _length;

        internal RangedFileContent(string filePath, long offset, long length)
        {
            _filePath = filePath;
            _offset = offset;
            _length = length;
            Headers.ContentLength = length;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            using var input = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            input.Seek(_offset, SeekOrigin.Begin);
            var buffer = new byte[81920];
            var remaining = _length;
            while (remaining > 0)
            {
                var read = await input.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, remaining)).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException("Captured screenshot bytes ended during the upload operation range.");
                await stream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                remaining -= read;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }
}
