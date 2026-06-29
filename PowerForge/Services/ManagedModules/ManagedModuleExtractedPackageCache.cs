namespace PowerForge;

internal sealed class ManagedModuleExtractedPackageCache
{
    private const string ExtractedCacheDirectoryName = ".x";
    private const string CacheVersion = "1";
    private const string PayloadDirectoryName = "p";
    private const string MetadataFileName = "m.txt";
    private const int SmallCopyFileThreshold = 8;
    private const int MaxCopyConcurrency = 4;
#if NET472
    private const int NetFrameworkParallelCopyFileThreshold = 1000;
#endif

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

        var lockStopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var cacheLock = ManagedModuleExtractedPackageCacheLock.Acquire(packageCacheDirectory, normalizedSha256, cancellationToken);
        lockStopwatch.Stop();
        var cacheLockWaitElapsed = lockStopwatch.Elapsed;
        var metadata = ReadValidMetadata(metadataPath, normalizedSha256);
        if (metadata is not null && Directory.Exists(payloadPath))
        {
            try
            {
                var copied = CopyDirectory(payloadPath, destinationPath, cancellationToken);
                if (copied.FileCount == metadata.Value.FileCount && copied.BytesWritten == metadata.Value.BytesWritten)
                {
                    stopwatch.Stop();
                    return new ManagedModuleArchiveExtractionResult(copied.FileCount, copied.BytesWritten, stopwatch.Elapsed, fromCache: true, cacheLockWaitElapsed);
                }

                _logger.Verbose($"Discarded extracted package cache for SHA256 {normalizedSha256} because materialized file counts did not match metadata.");
            }
            catch (Exception ex) when (IsRecoverableCacheMaterializationException(ex))
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
        return new ManagedModuleArchiveExtractionResult(extraction.FileCount, extraction.BytesWritten, stopwatch.Elapsed, fromCache: false, cacheLockWaitElapsed);
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

        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToArray();
        var copyDegree = ResolveCopyDegree(files.Length);
        if (copyDegree <= 1)
        {
            foreach (var file in files)
            {
                var copied = CopyFile(sourceRoot, destinationRoot, file, cancellationToken);
                fileCount++;
                bytesWritten += copied;
            }

            return new DirectoryCopyResult(fileCount, bytesWritten);
        }

        var options = new System.Threading.Tasks.ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = copyDegree
        };
        System.Threading.Tasks.Parallel.ForEach(files, options, file =>
        {
            var copied = CopyFile(sourceRoot, destinationRoot, file, cancellationToken);
            Interlocked.Increment(ref fileCount);
            Interlocked.Add(ref bytesWritten, copied);
        });

        return new DirectoryCopyResult(fileCount, bytesWritten);
    }

    private static int ResolveCopyDegree(int fileCount)
    {
        if (fileCount < SmallCopyFileThreshold)
            return 1;

#if NET472
        if (fileCount < NetFrameworkParallelCopyFileThreshold)
            return 1;
#endif

        return Math.Min(MaxCopyConcurrency, Math.Max(1, Environment.ProcessorCount));
    }

    private static long CopyFile(
        string sourceRoot,
        string destinationRoot,
        string file,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targetFile = Path.Combine(destinationRoot, file.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var sourceInfo = new FileInfo(file);
        File.Copy(file, targetFile, overwrite: false);
        return sourceInfo.Length;
    }

    private static void CreateDirectoryOnce(string path, HashSet<string> createdDirectories)
    {
        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (createdDirectories.Add(normalized))
            Directory.CreateDirectory(normalized);
    }

    private static bool IsRecoverableCacheMaterializationException(Exception ex)
    {
        if (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            return true;

        return ex is AggregateException aggregate &&
               aggregate.InnerExceptions.Count > 0 &&
               aggregate.InnerExceptions.All(IsRecoverableCacheMaterializationException);
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
