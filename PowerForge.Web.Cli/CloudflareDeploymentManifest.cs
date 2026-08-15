using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal sealed class CloudflareDeploymentManifest
{
    public int SchemaVersion { get; set; } = CloudflareDeploymentManifestStore.SchemaVersion;
    public string HashAlgorithm { get; set; } = CloudflareDeploymentManifestStore.HashAlgorithm;
    public string BaseUrl { get; set; } = string.Empty;
    public string CachePolicyFingerprint { get; set; } = string.Empty;
    public CloudflareDeploymentManifestEntry[] Files { get; set; } = Array.Empty<CloudflareDeploymentManifestEntry>();
}

internal sealed class CloudflareDeploymentManifestEntry
{
    public string Path { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class CloudflareDeploymentManifestCreateResult
{
    public string ManifestPath { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string CachePolicyFingerprint { get; set; } = string.Empty;
    public int ArtifactFileCount { get; set; }
    public int UrlPathCount { get; set; }
    public long ContentBytes { get; set; }
    public long ManifestBytes { get; set; }
    public long ElapsedMilliseconds { get; set; }
}

internal static class CloudflareDeploymentManifestStore
{
    internal const int SchemaVersion = 1;
    internal const string HashAlgorithm = "sha256";
    internal const long MaxManifestBytes = 32L * 1024L * 1024L;
    internal const int MaxManifestEntries = 250_000;

    internal static CloudflareDeploymentManifestCreateResult CreateFromTar(
        string artifactPath,
        string baseUrl,
        string outputPath,
        IReadOnlyCollection<string>? htmlPaths = null,
        CloudflareSitePolicySpec? cloudflare = null)
    {
        var resolvedArtifact = Path.GetFullPath(artifactPath ?? string.Empty);
        if (!File.Exists(resolvedArtifact))
            throw new FileNotFoundException($"Deployment artifact was not found: {resolvedArtifact}", resolvedArtifact);

        var resolvedOutput = Path.GetFullPath(outputPath ?? string.Empty);
        if (resolvedArtifact.Equals(resolvedOutput, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Deployment manifest output must differ from the deployment artifact path.");

        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        var cachePolicyFingerprint = ComputeCachePolicyFingerprint(normalizedBaseUrl, htmlPaths, cloudflare);
        var entries = new Dictionary<string, CloudflareDeploymentManifestEntry>(StringComparer.Ordinal);
        var archiveFiles = new Dictionary<string, ArchiveFileFingerprint>(StringComparer.Ordinal);
        var hardLinks = new Dictionary<string, string>(StringComparer.Ordinal);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using (var artifact = new FileStream(
                   resolvedArtifact,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   bufferSize: 1024 * 1024,
                   FileOptions.SequentialScan))
        using (var reader = new TarReader(artifact, leaveOpen: false))
        using (var sha256 = SHA256.Create())
        {
            TarEntry? entry;
            while ((entry = reader.GetNextEntry(copyData: false)) is not null)
            {
                if (entry.EntryType == TarEntryType.Directory || IsTarMetadata(entry.EntryType))
                    continue;

                var archivePath = NormalizeArchivePath(entry.Name);
                if (entry.EntryType == TarEntryType.HardLink)
                {
                    var targetPath = NormalizeArchivePath(entry.LinkName);
                    if (archiveFiles.ContainsKey(archivePath) || !hardLinks.TryAdd(archivePath, targetPath))
                        throw new InvalidDataException($"Deployment artifact contains duplicate file path '{entry.Name}'.");
                    continue;
                }

                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile))
                    throw new InvalidDataException($"Deployment artifact entry '{entry.Name}' has unsupported type '{entry.EntryType}'. Archive files with dereferenced links before creating the manifest.");

                var hashBytes = entry.DataStream is null
                    ? entry.Length == 0
                        ? SHA256.HashData(Array.Empty<byte>())
                        : throw new InvalidDataException($"Deployment artifact entry '{entry.Name}' has no content stream for its declared {entry.Length} bytes.")
                    : sha256.ComputeHash(entry.DataStream);
                var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                if (hardLinks.ContainsKey(archivePath) || !archiveFiles.TryAdd(archivePath, new ArchiveFileFingerprint(entry.Length, hash)))
                    throw new InvalidDataException($"Deployment artifact contains duplicate file path '{entry.Name}'.");
            }
        }

        foreach (var archivePath in hardLinks.Keys)
            ResolveHardLink(archivePath, archiveFiles, hardLinks);

        long contentBytes = 0;
        foreach (var file in archiveFiles.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            contentBytes = checked(contentBytes + file.Value.Length);
            foreach (var urlPath in BuildUrlPaths(file.Key))
            {
                var manifestEntry = new CloudflareDeploymentManifestEntry
                {
                    Path = urlPath,
                    Length = file.Value.Length,
                    Sha256 = file.Value.Sha256
                };
                if (!entries.TryAdd(urlPath, manifestEntry))
                    throw new InvalidDataException($"Deployment artifact maps more than one file to URL path '{urlPath}'.");
            }

            if (entries.Count > MaxManifestEntries)
                throw new InvalidDataException($"Deployment manifest exceeds the {MaxManifestEntries} URL-path safety limit.");
        }

        var manifest = new CloudflareDeploymentManifest
        {
            BaseUrl = normalizedBaseUrl,
            CachePolicyFingerprint = cachePolicyFingerprint,
            Files = entries.Values.OrderBy(value => value.Path, StringComparer.Ordinal).ToArray()
        };

        var outputDirectory = Path.GetDirectoryName(resolvedOutput);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var temporaryOutput = resolvedOutput + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, manifest, WebCliJson.Context.CloudflareDeploymentManifest);
                stream.Flush(flushToDisk: true);
            }

            var manifestBytes = new FileInfo(temporaryOutput).Length;
            if (manifestBytes is <= 0 or > MaxManifestBytes)
                throw new InvalidDataException($"Deployment manifest must be between 1 and {MaxManifestBytes} bytes; generated {manifestBytes} bytes.");

            File.Move(temporaryOutput, resolvedOutput, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryOutput))
                File.Delete(temporaryOutput);
        }

        stopwatch.Stop();
        return new CloudflareDeploymentManifestCreateResult
        {
            ManifestPath = resolvedOutput,
            BaseUrl = normalizedBaseUrl,
            CachePolicyFingerprint = manifest.CachePolicyFingerprint,
            ArtifactFileCount = archiveFiles.Count,
            UrlPathCount = manifest.Files.Length,
            ContentBytes = contentBytes,
            ManifestBytes = new FileInfo(resolvedOutput).Length,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
        };
    }

    internal static CloudflareDeploymentManifest LoadRequired(string manifestPath)
    {
        var resolvedPath = Path.GetFullPath(manifestPath ?? string.Empty);
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"Deployment manifest was not found: {resolvedPath}", resolvedPath);

        var length = new FileInfo(resolvedPath).Length;
        if (length is <= 0 or > MaxManifestBytes)
            throw new InvalidDataException($"Deployment manifest must be between 1 and {MaxManifestBytes} bytes.");

        CloudflareDeploymentManifest? manifest;
        using (var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            manifest = JsonSerializer.Deserialize(stream, WebCliJson.Context.CloudflareDeploymentManifest);

        if (manifest is null)
            throw new InvalidDataException("Deployment manifest is empty.");
        Validate(manifest);
        return manifest;
    }

    internal static void Validate(CloudflareDeploymentManifest manifest)
    {
        if (manifest.SchemaVersion != SchemaVersion)
            throw new InvalidDataException($"Unsupported deployment manifest schema version '{manifest.SchemaVersion}'. Expected {SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(manifest.HashAlgorithm) ||
            !manifest.HashAlgorithm.Equals(HashAlgorithm, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported deployment manifest hash algorithm '{manifest.HashAlgorithm}'. Expected {HashAlgorithm}.");
        if (!string.IsNullOrEmpty(manifest.CachePolicyFingerprint) && !IsValidPolicyFingerprint(manifest.CachePolicyFingerprint))
            throw new InvalidDataException("Deployment manifest has an invalid cache-policy fingerprint.");

        manifest.BaseUrl = NormalizeBaseUrl(manifest.BaseUrl);
        if (manifest.Files is null || manifest.Files.Length > MaxManifestEntries)
            throw new InvalidDataException($"Deployment manifest exceeds the {MaxManifestEntries} URL-path safety limit.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Files)
        {
            if (entry is null)
                throw new InvalidDataException("Deployment manifest contains a null file entry.");
            ValidateUrlPath(entry.Path);
            if (!seen.Add(entry.Path))
                throw new InvalidDataException($"Deployment manifest contains duplicate URL path '{entry.Path}'.");
            if (entry.Length < 0)
                throw new InvalidDataException($"Deployment manifest path '{entry.Path}' has a negative length.");
            if (string.IsNullOrWhiteSpace(entry.Sha256) ||
                entry.Sha256.Length != 64 ||
                entry.Sha256.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException($"Deployment manifest path '{entry.Path}' has an invalid SHA-256 value.");
        }
    }

    internal static Uri ResolveUrl(string normalizedBaseUrl, string relativePath)
    {
        ValidateUrlPath(relativePath);
        var baseUri = new Uri(NormalizeBaseUrl(normalizedBaseUrl), UriKind.Absolute);
        var target = new Uri(baseUri, relativePath);
        if (!target.Scheme.Equals(baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !target.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
            target.Port != baseUri.Port ||
            !target.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal))
            throw new InvalidDataException($"Deployment manifest path '{relativePath}' escapes configured site base '{baseUri}'.");
        return target;
    }

    internal static string NormalizeBaseUrl(string baseUrl)
    {
        var value = (baseUrl ?? string.Empty).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("Deployment manifest BaseUrl must be an absolute HTTP or HTTPS URL without credentials, query, or fragment.");

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.AbsoluteUri;
    }

    internal static string ComputeCachePolicyFingerprint(
        string baseUrl,
        IReadOnlyCollection<string>? htmlPaths,
        CloudflareSitePolicySpec? cloudflare)
    {
        // Hash the effective managed rules with a stable description name so any cache-affecting
        // configuration or engine change invalidates objects created under the previous policy.
        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        var uri = new Uri(normalizedBaseUrl, UriKind.Absolute);
        var rules = CloudflareCachePolicyBuilder.BuildManagedRules(
            uri.Host,
            "cache-fingerprint",
            htmlPaths,
            uri.AbsolutePath,
            cloudflare?.Cache);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rules))).ToLowerInvariant();
    }

    internal static bool IsValidPolicyFingerprint(string? fingerprint) =>
        !string.IsNullOrWhiteSpace(fingerprint) &&
        fingerprint.Length == 64 &&
        fingerprint.All(Uri.IsHexDigit);

    private static ArchiveFileFingerprint ResolveHardLink(
        string archivePath,
        Dictionary<string, ArchiveFileFingerprint> archiveFiles,
        IReadOnlyDictionary<string, string> hardLinks)
    {
        if (archiveFiles.TryGetValue(archivePath, out var existing))
            return existing;

        var chain = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = archivePath;
        while (!archiveFiles.TryGetValue(current, out existing))
        {
            if (!visited.Add(current))
                throw new InvalidDataException($"Deployment artifact contains a hard-link cycle at '{current}'.");
            if (!hardLinks.TryGetValue(current, out var targetPath))
                throw new InvalidDataException($"Deployment artifact hard link '{archivePath}' targets unavailable file '{current}'.");
            chain.Add(current);
            current = targetPath;
        }

        for (var index = chain.Count - 1; index >= 0; index--)
            archiveFiles.Add(chain[index], existing);
        return existing;
    }

    private sealed record ArchiveFileFingerprint(long Length, string Sha256);

    private static string NormalizeArchivePath(string rawPath)
    {
        var path = rawPath ?? string.Empty;
        while (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];

        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.Contains("\\", StringComparison.Ordinal))
            throw new InvalidDataException($"Deployment artifact contains unsafe file path '{rawPath}'.");

        var segments = path.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".."))
            throw new InvalidDataException($"Deployment artifact contains unsafe file path '{rawPath}'.");

        return string.Join("/", segments.Select(Uri.EscapeDataString));
    }

    private static IEnumerable<string> BuildUrlPaths(string archivePath)
    {
        yield return archivePath;

        const string indexName = "index.html";
        if (!archivePath.Equals(indexName, StringComparison.Ordinal) &&
            !archivePath.EndsWith("/" + indexName, StringComparison.Ordinal))
            yield break;

        var lastSlash = archivePath.LastIndexOf('/');
        yield return lastSlash < 0 ? string.Empty : archivePath[..(lastSlash + 1)];
    }

    private static void ValidateUrlPath(string path)
    {
        if (path is null ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.Contains("\\", StringComparison.Ordinal) ||
            path.Contains("?", StringComparison.Ordinal) ||
            path.Contains("#", StringComparison.Ordinal) ||
            Uri.TryCreate(path, UriKind.Absolute, out _))
            throw new InvalidDataException($"Deployment manifest contains unsafe URL path '{path}'.");

        if (path.Length == 0)
            return;

        var segments = path.Split('/', StringSplitOptions.None);
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length == 0)
            {
                if (index == segments.Length - 1)
                    continue;
                throw new InvalidDataException($"Deployment manifest contains unsafe URL path '{path}'.");
            }

            string decoded;
            for (var offset = 0; offset < segment.Length; offset++)
            {
                if (segment[offset] != '%')
                    continue;
                if (offset + 2 >= segment.Length || !Uri.IsHexDigit(segment[offset + 1]) || !Uri.IsHexDigit(segment[offset + 2]))
                    throw new InvalidDataException($"Deployment manifest contains invalid URL encoding in '{path}'.");
                offset += 2;
            }

            try
            {
                decoded = Uri.UnescapeDataString(segment);
            }
            catch (UriFormatException ex)
            {
                throw new InvalidDataException($"Deployment manifest contains invalid URL encoding in '{path}'.", ex);
            }

            if (decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\\'))
                throw new InvalidDataException($"Deployment manifest contains unsafe URL path '{path}'.");
        }
    }

    private static bool IsTarMetadata(TarEntryType entryType) => entryType is
        TarEntryType.DirectoryList or
        TarEntryType.LongLink or
        TarEntryType.LongPath or
        TarEntryType.MultiVolume or
        TarEntryType.RenamedOrSymlinked or
        TarEntryType.TapeVolume or
        TarEntryType.GlobalExtendedAttributes or
        TarEntryType.ExtendedAttributes;
}
