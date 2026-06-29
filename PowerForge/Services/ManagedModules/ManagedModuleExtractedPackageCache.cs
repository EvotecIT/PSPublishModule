namespace PowerForge;

internal sealed class ManagedModuleExtractedPackageCache
{
    private const string ExtractedCacheDirectoryName = ".x";
    private const string CacheVersion = "1";
    private const string PayloadDirectoryName = "p";
    private const string MetadataFileName = "m.txt";
    private const int CopyBufferSize = 1024 * 1024;

    private readonly ILogger _logger;

    public ManagedModuleExtractedPackageCache(ILogger logger)
    {
        _logger = logger ?? new NullLogger();
    }

    public ManagedModuleArchiveExtractionResult MaterializePackage(
        string packagePath,
        string packageSha256,
        string packageCacheDirectory,
        string destinationPath,
        ManagedModuleArchiveExtractor extractor,
        CancellationToken cancellationToken)
    {
        var normalizedSha256 = ManagedModulePackageIntegrity.NormalizeSha256(packageSha256);
        if (normalizedSha256 is null)
            return extractor.ExtractPackage(packagePath, destinationPath);

        var cacheRoot = GetCacheRoot(packageCacheDirectory, normalizedSha256);
        var payloadPath = Path.Combine(cacheRoot, PayloadDirectoryName);
        var metadataPath = Path.Combine(cacheRoot, MetadataFileName);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var cacheLock = ManagedModuleExtractedPackageCacheLock.Acquire(packageCacheDirectory, normalizedSha256, cancellationToken);
        var metadata = ReadValidMetadata(metadataPath, normalizedSha256);
        if (metadata is not null && Directory.Exists(payloadPath))
        {
            try
            {
                var copied = CopyDirectory(payloadPath, destinationPath, cancellationToken);
                if (copied.FileCount == metadata.Value.FileCount && copied.BytesWritten == metadata.Value.BytesWritten)
                {
                    stopwatch.Stop();
                    return new ManagedModuleArchiveExtractionResult(copied.FileCount, copied.BytesWritten, stopwatch.Elapsed, fromCache: true);
                }

                _logger.Verbose($"Discarded extracted package cache for SHA256 {normalizedSha256} because materialized file counts did not match metadata.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                _logger.Verbose($"Discarded extracted package cache for SHA256 {normalizedSha256} because materialization failed: {ex.Message}");
            }

            DeleteDirectoryQuietly(destinationPath);
            DeleteDirectoryQuietly(cacheRoot);
        }
        else if (Directory.Exists(cacheRoot))
        {
            DeleteDirectoryQuietly(cacheRoot);
        }

        var extraction = extractor.ExtractPackage(packagePath, destinationPath);
        TryPopulateCache(destinationPath, cacheRoot, normalizedSha256, extraction, cancellationToken);
        stopwatch.Stop();
        return new ManagedModuleArchiveExtractionResult(extraction.FileCount, extraction.BytesWritten, stopwatch.Elapsed, fromCache: false);
    }

    private static string GetCacheRoot(string packageCacheDirectory, string packageSha256)
        => Path.Combine(
            Path.GetFullPath(packageCacheDirectory),
            ExtractedCacheDirectoryName,
            CacheVersion,
            packageSha256.Substring(0, 32));

    private static ExtractedPackageCacheMetadata? ReadValidMetadata(string metadataPath, string packageSha256)
    {
        if (!File.Exists(metadataPath))
            return null;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(metadataPath))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
        }

        if (!values.TryGetValue("format", out var format) || !string.Equals(format, CacheVersion, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!values.TryGetValue("sha256", out var sha256) || !string.Equals(sha256, packageSha256, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!values.TryGetValue("fileCount", out var fileCountValue) || !int.TryParse(fileCountValue, out var fileCount))
            return null;
        if (!values.TryGetValue("bytes", out var bytesValue) || !long.TryParse(bytesValue, out var bytesWritten))
            return null;

        return new ExtractedPackageCacheMetadata(fileCount, bytesWritten);
    }

    private void TryPopulateCache(
        string materializedPath,
        string cacheRoot,
        string packageSha256,
        ManagedModuleArchiveExtractionResult extraction,
        CancellationToken cancellationToken)
    {
        var extractedRoot = Path.GetDirectoryName(Path.GetDirectoryName(cacheRoot)!)!;
        var stagingRoot = Path.Combine(extractedRoot, ".staging", Guid.NewGuid().ToString("N"));
        var stagingPayload = Path.Combine(stagingRoot, PayloadDirectoryName);
        var stagingMetadata = Path.Combine(stagingRoot, MetadataFileName);

        try
        {
            DeleteDirectoryQuietly(cacheRoot);
            CopyDirectory(materializedPath, stagingPayload, cancellationToken);
            Directory.CreateDirectory(stagingRoot);
            File.WriteAllLines(stagingMetadata, new[]
            {
                "format=" + CacheVersion,
                "sha256=" + packageSha256,
                "fileCount=" + extraction.FileCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "bytes=" + extraction.BytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

            Directory.CreateDirectory(Path.GetDirectoryName(cacheRoot)!);
            Directory.Move(stagingRoot, cacheRoot);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
        {
            _logger.Verbose($"Unable to populate extracted package cache for SHA256 {packageSha256}: {ex.Message}");
            DeleteDirectoryQuietly(stagingRoot);
            DeleteDirectoryQuietly(cacheRoot);
        }
    }

    private static DirectoryCopyResult CopyDirectory(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        var sourceRoot = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var destinationRoot = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(destinationRoot);
        var createdDirectories = new HashSet<string>(StringComparer.Ordinal)
        {
            destinationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        };

        var fileCount = 0;
        long bytesWritten = 0;
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetDirectory = Path.Combine(destinationRoot, directory.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            CreateDirectoryOnce(targetDirectory, createdDirectories);
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetFile = Path.Combine(destinationRoot, file.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            CreateDirectoryOnce(Path.GetDirectoryName(targetFile)!, createdDirectories);
            var sourceInfo = new FileInfo(file);
#if NETFRAMEWORK
            // Windows PowerShell 5.1/net472 benefits from the platform copy path in warm-cache save diagnostics.
            File.Copy(file, targetFile, overwrite: false);
#else
            using (var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan))
            using (var destination = new FileStream(targetFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.SequentialScan))
            {
                source.CopyTo(destination, CopyBufferSize);
            }
#endif
            fileCount++;
            bytesWritten += sourceInfo.Length;
        }

        return new DirectoryCopyResult(fileCount, bytesWritten);
    }

    private static void CreateDirectoryOnce(string path, HashSet<string> createdDirectories)
    {
        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (createdDirectories.Add(normalized))
            Directory.CreateDirectory(normalized);
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly struct DirectoryCopyResult
    {
        public DirectoryCopyResult(int fileCount, long bytesWritten)
        {
            FileCount = fileCount;
            BytesWritten = bytesWritten;
        }

        public int FileCount { get; }

        public long BytesWritten { get; }
    }

    private readonly struct ExtractedPackageCacheMetadata
    {
        public ExtractedPackageCacheMetadata(int fileCount, long bytesWritten)
        {
            FileCount = fileCount;
            BytesWritten = bytesWritten;
        }

        public int FileCount { get; }

        public long BytesWritten { get; }
    }
}
