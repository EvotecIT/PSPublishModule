using System.IO.Compression;

namespace PowerForge;

internal sealed partial class PublishedNuGetAssetRecoveryService
{
    private static void RewriteReleaseZipFromPublishedPackages(
        IEnumerable<string> publishedPackagePaths,
        string owningPublishedPackagePath,
        string releaseZipPath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(releaseZipPath))
            throw new FileNotFoundException(
                $"The rebuilt release ZIP required for recovery was not found: {releaseZipPath}",
                releaseZipPath);

        var payload = ReadPublishedLibraryPayload(publishedPackagePaths);
        var requiredPayload = ReadPublishedLibraryPayload([owningPublishedPackagePath]);
        using var source = ZipFile.OpenRead(releaseZipPath);
        var matched = new Dictionary<string, PublishedPackageEntry>(StringComparer.OrdinalIgnoreCase);
        using (var destination = ZipFile.Open(destinationPath, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedName = NormalizeArchivePath(entry.FullName);
                if (!string.IsNullOrEmpty(entry.Name) && payload.TryGetValue(normalizedName, out var published))
                {
                    WriteEntry(destination, entry.FullName, published.Bytes, published.LastWriteTime, published.ExternalAttributes);
                    matched[normalizedName] = published;
                    continue;
                }

                CopyEntry(entry, destination);
            }
        }

        var missing = requiredPayload.Keys.Where(path => !matched.ContainsKey(path)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Release ZIP '{releaseZipPath}' is missing published package payload: {string.Join(", ", missing)}.");
        }
        ValidateReleaseZipPayload(destinationPath, matched);
    }

    private static IReadOnlyDictionary<string, PublishedPackageEntry> ReadPublishedLibraryPayload(
        IEnumerable<string> packagePaths)
    {
        var payload = new Dictionary<string, PublishedPackageEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var packagePath in packagePaths)
        {
            using var package = ZipFile.OpenRead(packagePath);
            foreach (var entry in package.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                var name = NormalizeArchivePath(entry.FullName);
                if (!name.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
                    continue;
                var releasePath = NormalizeArchivePath(name.Substring("lib/".Length));
                var published = new PublishedPackageEntry(ReadAllBytes(entry), entry.LastWriteTime, entry.ExternalAttributes);
                if (payload.TryGetValue(releasePath, out var existing))
                {
                    if (!existing.Bytes.SequenceEqual(published.Bytes))
                    {
                        throw new InvalidOperationException(
                            $"Published NuGet packages contain conflicting library payload '{releasePath}'.");
                    }
                    continue;
                }
                payload.Add(releasePath, published);
            }
        }

        if (payload.Count == 0)
            throw new InvalidOperationException("Published NuGet package contains no lib payload for release ZIP recovery.");
        return payload;
    }

    private static void ValidateReleaseZipPayload(
        string releaseZipPath,
        IReadOnlyDictionary<string, PublishedPackageEntry> payload)
    {
        using var archive = ZipFile.OpenRead(releaseZipPath);
        var entries = archive.Entries
            .Where(static entry => !string.IsNullOrEmpty(entry.Name))
            .ToDictionary(
                entry => NormalizeArchivePath(entry.FullName),
                ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);
        foreach (var item in payload)
        {
            if (!entries.TryGetValue(item.Key, out var bytes) || !bytes.SequenceEqual(item.Value.Bytes))
            {
                throw new InvalidOperationException(
                    $"Recovered release ZIP '{releaseZipPath}' does not match published package payload '{item.Key}'.");
            }
        }
    }

    private static string NormalizeArchivePath(string name)
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
            throw new InvalidOperationException($"Published package contains unsafe archive path '{name}'.");
        }
        return normalized;
    }

    private static void CopyEntry(ZipArchiveEntry source, ZipArchive destination)
    {
        var target = destination.CreateEntry(source.FullName, CompressionLevel.Optimal);
        target.LastWriteTime = source.LastWriteTime;
        target.ExternalAttributes = source.ExternalAttributes;
        if (string.IsNullOrEmpty(source.Name))
            return;
        using var input = source.Open();
        using var output = target.Open();
        input.CopyTo(output);
    }

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        byte[] bytes,
        DateTimeOffset lastWriteTime,
        int externalAttributes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = lastWriteTime;
        entry.ExternalAttributes = externalAttributes;
        using var output = entry.Open();
        output.Write(bytes, 0, bytes.Length);
    }

    private static byte[] ReadAllBytes(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var memory = new MemoryStream();
        input.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class PublishedPackageEntry
    {
        internal PublishedPackageEntry(byte[] bytes, DateTimeOffset lastWriteTime, int externalAttributes)
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
