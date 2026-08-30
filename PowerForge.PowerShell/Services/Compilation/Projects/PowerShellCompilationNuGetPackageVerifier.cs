using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NuGet.Packaging;

namespace PowerForge;

/// <summary>Authenticates an isolated NuGet package and its extracted payload against a reviewed closure identity.</summary>
internal static class PowerShellCompilationNuGetPackageVerifier
{
    internal static PowerShellCompilationProjectPackage Verify(
        string packageRoot,
        PowerShellCompilationProjectPackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Id) ||
            string.IsNullOrWhiteSpace(package.Version) ||
            string.IsNullOrWhiteSpace(package.ContentHash))
        {
            throw new InvalidDataException("A project dependency lock contains an incomplete package identity.");
        }

        var versionRoot = Path.Combine(packageRoot, package.Id.ToLowerInvariant(), package.Version.ToLowerInvariant());
        var packagePath = Path.Combine(versionRoot, package.Id.ToLowerInvariant() + "." + package.Version.ToLowerInvariant() + ".nupkg");
        var hashPath = Path.Combine(versionRoot, package.Id.ToLowerInvariant() + "." + package.Version.ToLowerInvariant() + ".nupkg.sha512");
        var metadataPath = Path.Combine(versionRoot, ".nupkg.metadata");
        if (!File.Exists(packagePath) || !File.Exists(hashPath) || !File.Exists(metadataPath))
            throw new FileNotFoundException($"Exact restored package '{package.Id}/{package.Version}' is incomplete in the isolated environment.", versionRoot);

        PowerShellCompilationPathSafety.EnsureNoLinks(
            packageRoot,
            versionRoot,
            $"Restored package '{package.Id}/{package.Version}' traverses a symbolic link or junction.");

        string rawArchiveHash;
        using (var stream = File.OpenRead(packagePath))
        using (var algorithm = SHA512.Create())
            rawArchiveHash = Convert.ToBase64String(algorithm.ComputeHash(stream));

        var recordedArchiveHash = NormalizeContentHash(File.ReadAllText(hashPath));
        if (!rawArchiveHash.Equals(recordedArchiveHash, StringComparison.Ordinal))
            throw new InvalidDataException($"Restored package archive '{package.Id}/{package.Version}' differs from its NuGet archive hash.");
        if (!string.IsNullOrWhiteSpace(package.ArchiveSha512) &&
            !rawArchiveHash.Equals(NormalizeContentHash(package.ArchiveSha512), StringComparison.Ordinal))
            throw new InvalidDataException($"Restored package archive '{package.Id}/{package.Version}' differs from the isolated environment evidence.");

        string canonicalContentHash;
        using (var stream = File.OpenRead(packagePath))
        using (var reader = new PackageArchiveReader(stream, leaveStreamOpen: false))
        {
            reader.ValidatePackageEntriesAsync(CancellationToken.None).GetAwaiter().GetResult();
            canonicalContentHash = NormalizeContentHash(
                reader.GetContentHash(CancellationToken.None, () => rawArchiveHash));
        }

        var reviewedContentHash = NormalizeContentHash(package.ContentHash);
        if (!canonicalContentHash.Equals(reviewedContentHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Restored package '{package.Id}/{package.Version}' does not match the reviewed NuGet content hash " +
                $"(expected {reviewedContentHash}, actual {canonicalContentHash}).");
        }

        using (var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath)))
        {
            if (!metadata.RootElement.TryGetProperty("contentHash", out var contentHashElement))
                throw new InvalidDataException($"Restored package '{package.Id}/{package.Version}' has no NuGet content identity.");
            var recordedContentHash = NormalizeContentHash(contentHashElement.GetString() ?? string.Empty);
            if (!recordedContentHash.Equals(canonicalContentHash, StringComparison.Ordinal))
                throw new InvalidDataException($"Restored package '{package.Id}/{package.Version}' metadata differs from its authenticated archive content.");
        }

        VerifyExtractedPayload(versionRoot, packagePath, hashPath, metadataPath);
        var extractedHash = ComputeExtractedPackageSha256(versionRoot, packagePath, hashPath, metadataPath);
        if (!string.IsNullOrWhiteSpace(package.ExtractedFilesSha256) &&
            !package.ExtractedFilesSha256.Equals(extractedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Extracted package payload '{package.Id}/{package.Version}' differs from the isolated environment evidence.");
        }

        return new PowerShellCompilationProjectPackage
        {
            Id = package.Id,
            Version = package.Version,
            ContentHash = reviewedContentHash,
            ArchiveSha512 = rawArchiveHash,
            ExtractedFilesSha256 = extractedHash
        };
    }

    internal static string NormalizeContentHash(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.StartsWith("sha512-", StringComparison.OrdinalIgnoreCase)) normalized = normalized.Substring(7);
        try
        {
            if (Convert.FromBase64String(normalized).Length != 64) throw new FormatException();
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("NuGet content identity is not a SHA-512 digest.", exception);
        }
        return normalized;
    }

    private static void VerifyExtractedPayload(
        string versionRoot,
        string packagePath,
        params string[] excludedPaths)
    {
        var excluded = excludedPaths.Append(packagePath).Select(Path.GetFullPath).ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        var extracted = Directory.EnumerateFiles(versionRoot, "*", SearchOption.AllDirectories)
            .Where(path => !excluded.Contains(Path.GetFullPath(path)))
            .Select(path => new
            {
                FullPath = path,
                RelativePath = FrameworkCompatibility.GetRelativePath(versionRoot, path).Replace('\\', '/')
            })
            .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(static entry => !string.IsNullOrEmpty(entry.Name))
            .Where(static entry => !IsNuGetContainerMetadata(entry.FullName))
            .OrderBy(static entry => NormalizeArchivePath(entry.FullName), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicate = entries.GroupBy(static entry => NormalizeArchivePath(entry.FullName), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"NuGet package archive contains duplicate entry '{duplicate.Key}'.");
        var archivePaths = entries.Select(static entry => NormalizeArchivePath(entry.FullName)).ToArray();
        var extractedPaths = extracted.Select(static file => file.RelativePath).ToArray();
        if (!archivePaths.SequenceEqual(extractedPaths, StringComparer.OrdinalIgnoreCase))
        {
            var missing = archivePaths.Except(extractedPaths, StringComparer.OrdinalIgnoreCase).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
            var unexpected = extractedPaths.Except(archivePaths, StringComparer.OrdinalIgnoreCase).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
            throw new InvalidDataException(
                "Extracted package payload inventory differs from its authenticated NuGet archive: " +
                "missing=[" + string.Join(",", missing) + "]; unexpected=[" + string.Join(",", unexpected) + "].");
        }

        foreach (var pair in entries.Zip(extracted, static (entry, file) => new { Entry = entry, File = file }))
        {
            if ((pair.Entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException($"NuGet package entry '{pair.Entry.FullName}' is a symbolic link.");
            PowerShellCompilationPathSafety.EnsureNoLinks(
                versionRoot,
                pair.File.FullPath,
                $"Extracted package entry '{pair.File.RelativePath}' traverses a symbolic link or junction.");
            using var expectedStream = pair.Entry.Open();
            using var actualStream = File.OpenRead(pair.File.FullPath);
            using var expectedHash = SHA256.Create();
            using var actualHash = SHA256.Create();
            if (!expectedHash.ComputeHash(expectedStream).SequenceEqual(actualHash.ComputeHash(actualStream)))
                throw new InvalidDataException($"Extracted package payload entry '{pair.File.RelativePath}' differs from its authenticated NuGet archive.");
        }
    }

    private static bool IsNuGetContainerMetadata(string relativePath)
        => relativePath.Equals("[Content_Types].xml", StringComparison.Ordinal) ||
           relativePath.Equals("_rels/.rels", StringComparison.Ordinal) ||
           relativePath.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal);

    private static string NormalizeArchivePath(string relativePath)
        => Uri.UnescapeDataString(relativePath).Replace('\\', '/');

    private static string ComputeExtractedPackageSha256(
        string versionRoot,
        params string[] excludedPaths)
    {
        var excluded = excludedPaths.Select(Path.GetFullPath).ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        var files = Directory.EnumerateFiles(versionRoot, "*", SearchOption.AllDirectories)
            .Where(path => !excluded.Contains(Path.GetFullPath(path)))
            .OrderBy(path => FrameworkCompatibility.GetRelativePath(versionRoot, path), StringComparer.Ordinal)
            .Select(path =>
            {
                PowerShellCompilationPathSafety.EnsureNoLinks(
                    versionRoot,
                    path,
                    "Extracted package payload traverses a symbolic link or junction.");
                return new
                {
                    path = FrameworkCompatibility.GetRelativePath(versionRoot, path).Replace('\\', '/'),
                    sha256 = PowerShellCompilationProjectManifestService.ComputeSha256(path),
                    size = new FileInfo(path).Length
                };
            })
            .ToArray();
        var canonical = JsonSerializer.Serialize(files);
        using var algorithm = SHA256.Create();
        return PowerShellCompilationProjectManifestService.ToHex(algorithm.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }
}
