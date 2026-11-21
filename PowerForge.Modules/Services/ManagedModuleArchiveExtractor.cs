using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge;

internal sealed class ManagedModuleArchiveExtractor
{
    private const int CopyBufferSize = 1024 * 1024;
    private const int ParallelExtractionFileThreshold = 8;
    private const int MaximumParallelExtractionFiles = 256;
    private const int MaximumParallelExtraction = 4;
    private const long ParallelExtractionByteThreshold = 64L * 1024 * 1024;
    private static readonly string[] PackageMetadataPrefixes = { "_rels/", "package/services/metadata/" };
    private static readonly string[] PackageMetadataFiles = { "[Content_Types].xml", ".signature.p7s" };

    public ManagedModuleArchiveExtractionResult ExtractPackage(
        string packagePath,
        string destinationPath,
        string? packageId = null)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Package path is required.", nameof(packagePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path is required.", nameof(destinationPath));

        using var archive = ZipFile.OpenRead(packagePath);
        return ExtractArchive(archive, destinationPath, packageId, CancellationToken.None);
    }

#if !NET472
    public ManagedModuleArchiveExtractionResult ExtractBufferedPackage(
        MemoryStream packageStream,
        string destinationPath,
        string? packageId,
        CancellationToken cancellationToken)
    {
        ValidateBufferedPackageArguments(packageStream, destinationPath);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        if (ShouldExtractBufferedPackageInParallel(archive) && packageStream.TryGetBuffer(out var packageBuffer))
        {
            return ExtractBufferedPackageInParallel(
                packageBuffer,
                checked((int)packageStream.Length),
                archive,
                destinationPath,
                packageId,
                cancellationToken);
        }

        return ExtractArchive(archive, destinationPath, packageId, cancellationToken);
    }

    public Task<ManagedModuleArchiveExtractionResult> ExtractPackageAsync(
        Stream packageStream,
        string destinationPath,
        CancellationToken cancellationToken)
        => ExtractPackageAsync(packageStream, destinationPath, packageId: null, cancellationToken);

    public async Task<ManagedModuleArchiveExtractionResult> ExtractPackageAsync(
        Stream packageStream,
        string destinationPath,
        string? packageId,
        CancellationToken cancellationToken)
    {
        ValidateBufferedPackageArguments(packageStream, destinationPath);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        return await ExtractArchiveAsync(archive, destinationPath, packageId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ManagedModuleArchiveExtractionResult> ExtractArchiveAsync(
        ZipArchive archive,
        string destinationPath,
        string? packageId,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationPath);
        var destinationRoot = Path.GetFullPath(destinationPath);
        var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            destinationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        };
        var fileCount = 0;
        long bytesWritten = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var packageRoot = ResolvePackageRootFolder(archive, packageId);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = Normalize(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized) || IsPackageMetadata(normalized))
                continue;
            if (!IsSafePath(normalized))
                throw new InvalidOperationException($"Package contains unsafe path '{entry.FullName}'.");

            var extractionPath = TrimPackageRootFolder(normalized, packageRoot);
            if (string.IsNullOrWhiteSpace(extractionPath) || IsPackageMetadata(extractionPath))
                continue;

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, extractionPath.Replace('/', Path.DirectorySeparatorChar)));
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

    private static void ValidateBufferedPackageArguments(Stream packageStream, string destinationPath)
    {
        if (packageStream is null)
            throw new ArgumentNullException(nameof(packageStream));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path is required.", nameof(destinationPath));
    }

    private static bool ShouldExtractBufferedPackageInParallel(ZipArchive archive)
    {
        var fileCount = 0;
        long uncompressedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var normalized = Normalize(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized) ||
                IsPackageMetadata(normalized) ||
                entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                continue;
            }

            fileCount++;
            if (fileCount > MaximumParallelExtractionFiles)
                return false;
            if (uncompressedBytes < ParallelExtractionByteThreshold)
                uncompressedBytes += Math.Min(entry.Length, ParallelExtractionByteThreshold - uncompressedBytes);
        }

        return fileCount >= ParallelExtractionFileThreshold &&
               uncompressedBytes >= ParallelExtractionByteThreshold &&
               Environment.ProcessorCount > 1;
    }

    private static ManagedModuleArchiveExtractionResult ExtractBufferedPackageInParallel(
        ArraySegment<byte> packageBuffer,
        int packageLength,
        ZipArchive archive,
        string destinationPath,
        string? packageId,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var destinationRoot = Path.GetFullPath(destinationPath);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            destinationRoot
        };
        var files = new List<BufferedArchiveEntry>();
        var packageRoot = ResolvePackageRootFolder(archive, packageId);

        for (var index = 0; index < archive.Entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.Entries[index];
            var normalized = Normalize(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized) || IsPackageMetadata(normalized))
                continue;
            if (!IsSafePath(normalized))
                throw new InvalidOperationException($"Package contains unsafe path '{entry.FullName}'.");

            var extractionPath = TrimPackageRootFolder(normalized, packageRoot);
            if (string.IsNullOrWhiteSpace(extractionPath) || IsPackageMetadata(extractionPath))
                continue;

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, extractionPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInsideDirectory(destinationRoot, targetPath))
                throw new InvalidOperationException($"Package entry '{entry.FullName}' escapes the destination directory.");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                directories.Add(targetPath);
                continue;
            }

            directories.Add(Path.GetDirectoryName(targetPath)!);
            files.Add(new BufferedArchiveEntry(index, targetPath));
        }

        foreach (var directory in directories)
            Directory.CreateDirectory(directory);

        long bytesWritten = 0;
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Min(MaximumParallelExtraction, Math.Max(1, Environment.ProcessorCount))
        };
        Parallel.ForEachAsync(files, options, (file, token) =>
        {
            using var packageCopy = new MemoryStream(
                packageBuffer.Array!,
                packageBuffer.Offset,
                packageLength,
                writable: false,
                publiclyVisible: true);
            using var archiveCopy = new ZipArchive(packageCopy, ZipArchiveMode.Read, leaveOpen: false);
            using var source = archiveCopy.Entries[file.ArchiveIndex].Open();
            using var destination = new FileStream(
                file.TargetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.SequentialScan);
            CopyStream(source, destination, token);
            Interlocked.Add(ref bytesWritten, destination.Length);
            return ValueTask.CompletedTask;
        }).GetAwaiter().GetResult();

        stopwatch.Stop();
        return new ManagedModuleArchiveExtractionResult(files.Count, bytesWritten, stopwatch.Elapsed, fromCache: false, cacheLockWaitElapsed: TimeSpan.Zero);
    }
#endif

    private static ManagedModuleArchiveExtractionResult ExtractArchive(
        ZipArchive archive,
        string destinationPath,
        string? packageId,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationPath);
        var destinationRoot = Path.GetFullPath(destinationPath);
        var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            destinationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        };
        var fileCount = 0;
        long bytesWritten = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var packageRoot = ResolvePackageRootFolder(archive, packageId);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = Normalize(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized) || IsPackageMetadata(normalized))
                continue;
            if (!IsSafePath(normalized))
                throw new InvalidOperationException($"Package contains unsafe path '{entry.FullName}'.");

            var extractionPath = TrimPackageRootFolder(normalized, packageRoot);
            if (string.IsNullOrWhiteSpace(extractionPath) || IsPackageMetadata(extractionPath))
                continue;

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, extractionPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInsideDirectory(destinationRoot, targetPath))
                throw new InvalidOperationException($"Package entry '{entry.FullName}' escapes the destination directory.");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                EnsureDirectory(targetPath, createdDirectories);
                continue;
            }

            EnsureDirectory(Path.GetDirectoryName(targetPath)!, createdDirectories);
            using var source = entry.Open();
            using var destination = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.SequentialScan);
            CopyStream(source, destination, cancellationToken);
            fileCount++;
            bytesWritten += destination.Length;
        }

        stopwatch.Stop();
        return new ManagedModuleArchiveExtractionResult(fileCount, bytesWritten, stopwatch.Elapsed, fromCache: false, cacheLockWaitElapsed: TimeSpan.Zero);
    }

    private static void CopyStream(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                destination.Write(buffer, 0, bytesRead);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private readonly struct BufferedArchiveEntry
    {
        public BufferedArchiveEntry(int archiveIndex, string targetPath)
        {
            ArchiveIndex = archiveIndex;
            TargetPath = targetPath;
        }

        public int ArchiveIndex { get; }

        public string TargetPath { get; }
    }

    private static string Normalize(string path)
        => path.Replace('\\', '/').Trim('/');

    private static string? ResolvePackageRootFolder(ZipArchive archive, string? packageId)
    {
        packageId = string.IsNullOrWhiteSpace(packageId) ? ReadPackageId(archive) : packageId!.Trim();
        if (string.IsNullOrWhiteSpace(packageId))
            return null;

        var root = packageId!.Trim();
        var moduleManifestPath = root + "/" + root + ".psd1";
        return archive.Entries.Any(entry => Normalize(entry.FullName).Equals(moduleManifestPath, StringComparison.OrdinalIgnoreCase))
            ? root
            : null;
    }

    private static string? ReadPackageId(ZipArchive archive)
    {
        var nuspec = archive.Entries
            .Where(static entry => Normalize(entry.FullName).EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static entry => Normalize(entry.FullName).Count(static ch => ch == '/'))
            .ThenBy(static entry => Normalize(entry.FullName), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (nuspec is null)
            return null;

        using var stream = nuspec.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true
        });
        var document = XDocument.Load(reader);
        return document.Descendants()
            .FirstOrDefault(static element => element.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();
    }

    private static string TrimPackageRootFolder(string normalizedPath, string? packageRoot)
    {
        if (string.IsNullOrWhiteSpace(packageRoot))
            return normalizedPath;

        if (normalizedPath.Equals(packageRoot, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var prefix = packageRoot + "/";
        return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? normalizedPath.Substring(prefix.Length)
            : normalizedPath;
    }

    private static void EnsureDirectory(string path, HashSet<string> createdDirectories)
    {
        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (createdDirectories.Add(normalized))
            Directory.CreateDirectory(normalized);
    }

    private static bool IsPackageMetadata(string normalizedPath)
    {
        if (normalizedPath.IndexOf('/') < 0 && normalizedPath.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
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
