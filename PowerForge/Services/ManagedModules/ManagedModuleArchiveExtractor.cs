using System.IO.Compression;

namespace PowerForge;

internal sealed class ManagedModuleArchiveExtractor
{
    private const int CopyBufferSize = 1024 * 1024;
    private static readonly string[] PackageMetadataPrefixes = { "_rels/", "package/services/metadata/" };
    private static readonly string[] PackageMetadataFiles = { "[Content_Types].xml", ".signature.p7s" };

    public ManagedModuleArchiveExtractionResult ExtractPackage(string packagePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Package path is required.", nameof(packagePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path is required.", nameof(destinationPath));

        Directory.CreateDirectory(destinationPath);
        var destinationRoot = Path.GetFullPath(destinationPath);
        var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            destinationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        };
        var fileCount = 0;
        long bytesWritten = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var normalized = Normalize(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized) || IsPackageMetadata(normalized))
                continue;
            if (!IsSafePath(normalized))
                throw new InvalidOperationException($"Package contains unsafe path '{entry.FullName}'.");

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInsideDirectory(destinationRoot, targetPath))
                throw new InvalidOperationException($"Package entry '{entry.FullName}' escapes the destination directory.");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                EnsureDirectory(targetPath, createdDirectories);
                continue;
            }

            EnsureDirectory(Path.GetDirectoryName(targetPath)!, createdDirectories);
            using var source = entry.Open();
            using var destination = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.SequentialScan);
            source.CopyTo(destination, CopyBufferSize);
            fileCount++;
            bytesWritten += destination.Length;
        }

        stopwatch.Stop();
        return new ManagedModuleArchiveExtractionResult(fileCount, bytesWritten, stopwatch.Elapsed, fromCache: false, cacheLockWaitElapsed: TimeSpan.Zero);
    }

#if !NET472
    public async Task<ManagedModuleArchiveExtractionResult> ExtractPackageAsync(
        Stream packageStream,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (packageStream is null)
            throw new ArgumentNullException(nameof(packageStream));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path is required.", nameof(destinationPath));

        Directory.CreateDirectory(destinationPath);
        var destinationRoot = Path.GetFullPath(destinationPath);
        var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            destinationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        };
        var fileCount = 0;
        long bytesWritten = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = Normalize(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized) || IsPackageMetadata(normalized))
                continue;
            if (!IsSafePath(normalized))
                throw new InvalidOperationException($"Package contains unsafe path '{entry.FullName}'.");

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInsideDirectory(destinationRoot, targetPath))
                throw new InvalidOperationException($"Package entry '{entry.FullName}' escapes the destination directory.");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                EnsureDirectory(targetPath, createdDirectories);
                continue;
            }

            EnsureDirectory(Path.GetDirectoryName(targetPath)!, createdDirectories);
            using var source = entry.Open();
            await using var destination = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, CopyBufferSize, cancellationToken).ConfigureAwait(false);
            fileCount++;
            bytesWritten += destination.Length;
        }

        stopwatch.Stop();
        return new ManagedModuleArchiveExtractionResult(fileCount, bytesWritten, stopwatch.Elapsed, fromCache: false, cacheLockWaitElapsed: TimeSpan.Zero);
    }
#endif

    private static string Normalize(string path)
        => path.Replace('\\', '/').Trim('/');

    private static void EnsureDirectory(string path, HashSet<string> createdDirectories)
    {
        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (createdDirectories.Add(normalized))
            Directory.CreateDirectory(normalized);
    }

    private static bool IsPackageMetadata(string normalizedPath)
    {
        if (normalizedPath.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            return true;
        if (PackageMetadataFiles.Any(file => normalizedPath.Equals(file, StringComparison.OrdinalIgnoreCase)))
            return true;

        return PackageMetadataPrefixes.Any(prefix => normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafePath(string normalizedPath)
    {
        if (normalizedPath.StartsWith("/", StringComparison.Ordinal) ||
            normalizedPath.Contains(":", StringComparison.Ordinal))
            return false;

        var parts = normalizedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(static part => part != "." && part != "..");
    }

    private static bool IsInsideDirectory(string directory, string path)
    {
        var root = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class ManagedModuleArchiveExtractionResult
{
    public ManagedModuleArchiveExtractionResult(int fileCount, long bytesWritten, TimeSpan elapsed, bool fromCache, TimeSpan cacheLockWaitElapsed)
    {
        FileCount = fileCount;
        BytesWritten = bytesWritten;
        Elapsed = elapsed;
        FromCache = fromCache;
        CacheLockWaitElapsed = cacheLockWaitElapsed;
    }

    public int FileCount { get; }

    public long BytesWritten { get; }

    public TimeSpan Elapsed { get; }

    public bool FromCache { get; }

    public TimeSpan CacheLockWaitElapsed { get; }
}
