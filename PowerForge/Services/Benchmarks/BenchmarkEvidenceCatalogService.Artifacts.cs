using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

public sealed partial class BenchmarkEvidenceCatalogService
{
    private static string ResolveResultArtifactPath(
        string catalogPath,
        string resultPath,
        string? resultArtifactPath)
    {
        if (!string.IsNullOrWhiteSpace(resultArtifactPath))
            return BenchmarkJson.ResolveWritePath(resultArtifactPath!);
        // A root-relative website path such as /data/x.json, an absolute file path or a URL describes where
        // readers find the result, not where to write it. Resolving it would write to a drive root or another
        // unintended location, so the local destination must be given explicitly.
        if (Path.IsPathRooted(resultPath) || Uri.TryCreate(resultPath, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"Result path '{resultPath}' is absolute or root-relative. Supply ResultArtifactPath " +
                "(-ResultArtifactPath) with the local file to write, or use a result path relative to the catalog.");
        }

        string catalogDirectory = Path.GetDirectoryName(catalogPath)
                                  ?? throw new InvalidOperationException(
                                      $"Unable to determine the evidence catalog directory for '{catalogPath}'.");
        return BenchmarkJson.ResolveWritePath(Path.Combine(catalogDirectory, resultPath));
    }

    private static void ValidateArtifactDestinationOwnership(
        BenchmarkEvidenceCatalog? catalog,
        string catalogPath,
        string artifactPath,
        string artifactDestinationSha256,
        BenchmarkEvidenceEntry selectedEntry)
    {
        if (catalog is null)
            return;

        string? existingArtifactHash = File.Exists(artifactPath)
            ? BenchmarkJson.ComputeFileSha256(artifactPath)
            : null;
        foreach (BenchmarkEvidenceEntry existing in catalog.Entries)
        {
            if (SameLane(existing, selectedEntry))
                continue;

            bool persistedDestinationMatches =
                !string.IsNullOrWhiteSpace(existing.ArtifactDestinationSha256) &&
                string.Equals(
                    existing.ArtifactDestinationSha256,
                    artifactDestinationSha256,
                    StringComparison.OrdinalIgnoreCase);
            bool resolvedPathMatches =
                TryResolveExistingArtifactPath(
                    catalogPath,
                    existing.ResultPath,
                    out string? existingArtifactPath) &&
                string.Equals(
                    existingArtifactPath,
                    artifactPath,
                    FrameworkCompatibility.GetPathStringComparison(artifactPath));
            bool existingContentOwnsDestination =
                string.IsNullOrWhiteSpace(existing.ArtifactDestinationSha256) &&
                !string.IsNullOrWhiteSpace(existingArtifactHash) &&
                !string.IsNullOrWhiteSpace(existing.ResultSha256) &&
                string.Equals(
                    existingArtifactHash,
                    existing.ResultSha256,
                    StringComparison.OrdinalIgnoreCase);
            if (!persistedDestinationMatches &&
                !resolvedPathMatches &&
                !existingContentOwnsDestination)
                continue;

            throw new InvalidOperationException(
                $"Published benchmark lane '{selectedEntry.LaneKey}' " +
                $"resolves to the artifact destination already owned by " +
                $"'{existing.LaneKey}': '{artifactPath}'.");
        }
    }

    private static string ComputeArtifactDestinationSha256(
        string catalogPath,
        string artifactPath)
    {
        string catalogDirectory = Path.GetDirectoryName(catalogPath)
                                  ?? throw new InvalidOperationException(
                                      $"Unable to determine the evidence catalog directory for '{catalogPath}'.");
        string normalizedDestination = FrameworkCompatibility.GetRelativePath(
                catalogDirectory,
                artifactPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        // Case is folded only where the destination volume ignores it.
        if (BenchmarkFileUpdateLock.IsCaseInsensitivePath(catalogPath))
            normalizedDestination = normalizedDestination.ToLowerInvariant();
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedDestination));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string ResolveArtifactRelativePath(
        string catalogPath,
        string artifactPath,
        string resultPath)
    {
        string catalogDirectory = Path.GetDirectoryName(catalogPath)
                                  ?? throw new InvalidOperationException(
                                      $"Unable to determine the evidence catalog directory for '{catalogPath}'.");
        string relativePath = FrameworkCompatibility.GetRelativePath(
            catalogDirectory,
            artifactPath);
        if (!Path.IsPathRooted(relativePath) &&
            !relativePath.Equals("..", StringComparison.Ordinal) &&
            !relativePath.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) &&
            !relativePath.StartsWith(
                ".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            return NormalizeBundleArtifactRelativePath(relativePath, resultPath);
        }

        return Path.GetFileName(artifactPath);
    }

    private static bool TryResolveExistingArtifactPath(
        string catalogPath,
        string resultPath,
        out string? artifactPath)
    {
        try
        {
            artifactPath = ResolveResultArtifactPath(
                catalogPath,
                resultPath,
                resultArtifactPath: null);
            return true;
        }
        catch (InvalidOperationException)
        {
            artifactPath = null;
            return false;
        }
    }

    private static string ContentAddressedFilePath(string artifactPath, string sha256)
    {
        string directory = Path.GetDirectoryName(artifactPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(artifactPath);
        string hash = sha256.ToLowerInvariant();
        if (stem.EndsWith("." + hash, StringComparison.OrdinalIgnoreCase))
            return artifactPath;
        return Path.Combine(directory, stem + "." + hash + Path.GetExtension(artifactPath));
    }
}
