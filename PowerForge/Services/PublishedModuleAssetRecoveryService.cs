using System.IO.Compression;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Rebuilds staged module archives with the exact module payload already published to a NuGet gallery.
/// </summary>
internal sealed class PublishedModuleAssetRecoveryService
{
    private readonly ILogger _logger;
    private readonly NuGetV3PackageDownloader _downloader;

    internal PublishedModuleAssetRecoveryService(
        ILogger logger,
        NuGetV3PackageDownloader? downloader = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _downloader = downloader ?? new NuGetV3PackageDownloader();
    }

    internal string[] Restore(
        string serviceIndexUrl,
        string moduleName,
        string expectedVersion,
        IEnumerable<string> moduleAssetPaths,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceIndexUrl))
            throw new InvalidOperationException(
                "Verified GitHub recovery requires GitHub.PublishedModuleSource to restore the published module payload.");
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new InvalidOperationException(
                "Verified GitHub recovery requires the exact module name to restore the published module payload.");
        if (string.IsNullOrWhiteSpace(expectedVersion))
            throw new InvalidOperationException(
                "Verified GitHub recovery requires the exact module version to restore the published module payload.");
        if (moduleAssetPaths is null)
            throw new ArgumentNullException(nameof(moduleAssetPaths));

        var archivePaths = moduleAssetPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (archivePaths.Length == 0)
            return Array.Empty<string>();
        var unsupported = archivePaths
            .Where(path => !string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (unsupported.Length > 0)
        {
            throw new InvalidOperationException(
                "Verified GitHub recovery can restore only ZIP-based module assets: " +
                string.Join(", ", unsupported.Select(Path.GetFileName)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var packagePath = Path.Combine(
            Path.GetTempPath(),
            $"powerforge-published-module-{Guid.NewGuid():N}.nupkg");
        var rewrites = new List<ArchiveRewrite>(archivePaths.Length);
        try
        {
            _downloader.DownloadPackageAsync(
                    serviceIndexUrl,
                    moduleName,
                    expectedVersion,
                    packagePath,
                    new PrivateGalleryIndexOptions(),
                    cancellationToken)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            var payload = ReadPublishedPayload(packagePath, moduleName, expectedVersion);
            foreach (var archivePath in archivePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(archivePath))
                {
                    throw new FileNotFoundException(
                        $"The rebuilt module asset required for recovery was not found: {archivePath}",
                        archivePath);
                }

                var temporaryPath = archivePath + ".published-" + Guid.NewGuid().ToString("N") + ".tmp";
                RewriteArchive(archivePath, temporaryPath, moduleName, payload, cancellationToken);
                rewrites.Add(new ArchiveRewrite(archivePath, temporaryPath));
            }

            ReplaceArchives(rewrites, cancellationToken);
            foreach (var archivePath in archivePaths)
                _logger.Info($"Restored published module payload for GitHub recovery: {Path.GetFileName(archivePath)}");
            return archivePaths;
        }
        finally
        {
            TryDelete(packagePath);
            foreach (var rewrite in rewrites)
            {
                TryDelete(rewrite.TemporaryPath);
                TryDelete(rewrite.BackupPath);
            }
        }
    }

    private static IReadOnlyDictionary<string, PublishedEntry> ReadPublishedPayload(
        string packagePath,
        string moduleName,
        string expectedVersion)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var nuspec = archive.Entries.FirstOrDefault(entry =>
            !entry.FullName.Contains('/') &&
            entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspec is null)
            throw new InvalidOperationException("The published module package does not contain a root nuspec.");

        using (var stream = nuspec.Open())
        {
            var document = XDocument.Load(stream);
            var metadata = document.Root?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "metadata", StringComparison.OrdinalIgnoreCase));
            var id = metadata?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "id", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            var version = metadata?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "version", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            if (!string.Equals(id, moduleName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(version, expectedVersion, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Published module recovery returned '{id} {version}', expected '{moduleName} {expectedVersion}'.");
            }
        }

        var payload = new Dictionary<string, PublishedEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(static entry => !IsPackageMetadata(entry)))
        {
            var name = NormalizeEntryName(entry.FullName);
            if (payload.ContainsKey(name))
                throw new InvalidOperationException($"The published module package contains duplicate payload path '{name}'.");
            payload.Add(name, new PublishedEntry(ReadAllBytes(entry), entry.LastWriteTime, entry.ExternalAttributes));
        }

        if (!payload.ContainsKey(moduleName + ".psd1"))
            throw new InvalidOperationException(
                $"The published module package does not contain the expected '{moduleName}.psd1' payload.");
        return payload;
    }

    private static bool IsPackageMetadata(ZipArchiveEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Name))
            return true;
        var name = entry.FullName.Replace('\\', '/');
        return string.Equals(name, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, ".signature.p7s", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("_rels/", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("package/", StringComparison.OrdinalIgnoreCase) ||
               !name.Contains('/') && name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase);
    }

    private static void RewriteArchive(
        string sourcePath,
        string destinationPath,
        string moduleName,
        IReadOnlyDictionary<string, PublishedEntry> payload,
        CancellationToken cancellationToken)
    {
        using var source = ZipFile.OpenRead(sourcePath);
        var prefix = moduleName + "/";
        var manifestPath = prefix + moduleName + ".psd1";
        if (!source.Entries.Any(entry => string.Equals(
                entry.FullName.Replace('\\', '/'),
                manifestPath,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Module archive '{sourcePath}' does not contain the expected '{manifestPath}' root.");
        }

        using (var destination = ZipFile.Open(destinationPath, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = entry.FullName.Replace('\\', '/');
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;
                    var relativeName = NormalizeEntryName(name.Substring(prefix.Length));
                    if (payload.ContainsKey(relativeName))
                        continue;
                    if (!IsFullPackageExtra(relativeName))
                    {
                        throw new InvalidOperationException(
                            $"Module archive '{sourcePath}' contains unverified module payload '{relativeName}'.");
                    }
                }
                CopyEntry(entry, destination, name);
            }

            foreach (var item in payload.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = destination.CreateEntry(prefix + item.Key, CompressionLevel.Optimal);
                target.LastWriteTime = item.Value.LastWriteTime;
                target.ExternalAttributes = item.Value.ExternalAttributes;
                using var stream = target.Open();
                stream.Write(item.Value.Bytes, 0, item.Value.Bytes.Length);
            }
        }

        ValidateArchivePayload(destinationPath, prefix, payload);
    }

    private static void ValidateArchivePayload(
        string archivePath,
        string prefix,
        IReadOnlyDictionary<string, PublishedEntry> payload)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var actual = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name) &&
                            entry.FullName.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                entry => NormalizeEntryName(entry.FullName.Replace('\\', '/').Substring(prefix.Length)),
                ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);
        var unexpected = actual.Keys
            .Where(name => !payload.ContainsKey(name) && !IsFullPackageExtra(name))
            .ToArray();
        if (unexpected.Length > 0)
        {
            throw new InvalidOperationException(
                $"Recovered module archive '{archivePath}' has unexpected module payload: {string.Join(", ", unexpected)}.");
        }
        foreach (var item in payload)
        {
            if (!actual.TryGetValue(item.Key, out var bytes) || !bytes.SequenceEqual(item.Value.Bytes))
            {
                throw new InvalidOperationException(
                    $"Recovered module archive '{archivePath}' does not match published payload '{item.Key}'.");
            }
        }
    }

    private static bool IsFullPackageExtra(string relativeName)
        => relativeName.StartsWith("Modules/", StringComparison.OrdinalIgnoreCase) ||
           relativeName.StartsWith("Examples/", StringComparison.OrdinalIgnoreCase);

    private static void CopyEntry(ZipArchiveEntry source, ZipArchive destination, string name)
    {
        var target = destination.CreateEntry(name, CompressionLevel.Optimal);
        target.LastWriteTime = source.LastWriteTime;
        target.ExternalAttributes = source.ExternalAttributes;
        if (string.IsNullOrEmpty(source.Name))
            return;
        using var input = source.Open();
        using var output = target.Open();
        input.CopyTo(output);
    }

    private static void ReplaceArchives(IReadOnlyList<ArchiveRewrite> rewrites, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var rewrite in rewrites)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(rewrite.OriginalPath, rewrite.BackupPath, overwrite: false);
                File.Replace(rewrite.TemporaryPath, rewrite.OriginalPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                rewrite.Replaced = true;
            }
        }
        catch
        {
            foreach (var rewrite in rewrites.Where(static rewrite => rewrite.Replaced))
            {
                try
                {
                    File.Copy(rewrite.BackupPath, rewrite.OriginalPath, overwrite: true);
                }
                catch
                {
                    // Preserve the primary recovery failure. Backups remain until the outer cleanup.
                }
            }
            throw;
        }
    }

    private static string NormalizeEntryName(string name)
    {
        var normalized = name.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(':') ||
            segments.Any(segment =>
                string.IsNullOrEmpty(segment) ||
                string.Equals(segment, ".", StringComparison.Ordinal) ||
                string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Published module package contains unsafe payload path '{name}'.");
        }
        return normalized;
    }

    private static byte[] ReadAllBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup must not hide the recovery result.
        }
    }

    private sealed class ArchiveRewrite
    {
        internal ArchiveRewrite(string originalPath, string temporaryPath)
        {
            OriginalPath = originalPath;
            TemporaryPath = temporaryPath;
            BackupPath = originalPath + ".pre-recovery-" + Guid.NewGuid().ToString("N") + ".bak";
        }

        internal string OriginalPath { get; }

        internal string TemporaryPath { get; }

        internal string BackupPath { get; }

        internal bool Replaced { get; set; }
    }

    private sealed class PublishedEntry
    {
        internal PublishedEntry(byte[] bytes, DateTimeOffset lastWriteTime, int externalAttributes)
        {
            Bytes = bytes;
            LastWriteTime = lastWriteTime;
            ExternalAttributes = externalAttributes;
        }

        internal byte[] Bytes { get; }

        internal DateTimeOffset LastWriteTime { get; }

        internal int ExternalAttributes { get; }
    }
}
