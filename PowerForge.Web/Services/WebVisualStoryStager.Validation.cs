using System.Security.Cryptography;
using System.Text;
using ImageMagick;

namespace PowerForge.Web;

/// <summary>Contains artifact validation responsibilities for visual-story staging.</summary>
public static partial class WebVisualStoryStager
{
    private static void ValidateBundle(WebVisualStoryBundle bundle)
    {
        if (bundle.SchemaVersion is null)
            throw new InvalidOperationException("Visual-story schemaVersion is required.");
        if (bundle.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported visual-story schema version: {bundle.SchemaVersion}");
        Require(bundle.Id, "id");
        Require(bundle.Title, "title");
        Require(bundle.Alt, "alt");
        Require(bundle.Outcome, "outcome");
        if (bundle.ResourceLimits is not null)
        {
            if (bundle.ResourceLimits.MaximumArtifactBytes <= 0)
                throw new InvalidOperationException("Visual-story resourceLimits.maximumArtifactBytes must be positive.");
            if (bundle.ResourceLimits.MaximumTotalArtifactBytes <= 0)
                throw new InvalidOperationException("Visual-story resourceLimits.maximumTotalArtifactBytes must be positive.");
        }
        if (bundle.Artifacts is null || bundle.Artifacts.Length == 0)
            throw new InvalidOperationException("Visual-story manifest must declare artifacts.");
        if (bundle.Artifacts.Length > MaximumArtifactCount)
            throw new InvalidOperationException($"Visual-story manifest exceeds the {MaximumArtifactCount}-artifact safety limit.");
    }

    private static void ValidateArtifact(WebVisualStoryArtifact artifact)
    {
        if (artifact is null)
            throw new InvalidOperationException("Visual-story artifacts cannot contain null entries.");
        Require(artifact.Role, "artifact role");
        Require(artifact.Format, "artifact format");
        Require(artifact.Path, "artifact path");
        if (!SupportedRoles.Contains(artifact.Role))
            throw new InvalidOperationException($"Unsupported visual-story artifact role: {artifact.Role}");
        if (!SupportedFormats.Contains(NormalizeFormat(artifact.Format)))
            throw new InvalidOperationException($"Unsupported visual-story artifact format: {artifact.Format}");
        if (string.Equals(artifact.Role, "transcript", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(NormalizeFormat(artifact.Format), "text", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Visual-story transcript artifacts must use the text format.");
        }
        if (string.Equals(artifact.Role, "animated", StringComparison.OrdinalIgnoreCase) &&
            !AnimatedFormats.Contains(NormalizeFormat(artifact.Format)))
        {
            throw new InvalidOperationException("Visual-story animated artifacts must use the svg, gif, or apng format.");
        }
    }

    private static void ValidateReservedStagedPath(string relativePath)
    {
        var firstSeparator = relativePath.IndexOf('/');
        var firstSegment = firstSeparator < 0 ? relativePath : relativePath.Substring(0, firstSeparator);
        if (string.Equals(firstSegment, StagedManifestFileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Visual-story artifact path conflicts with the reserved staged manifest: {relativePath}");
    }

    private static void ValidateCompletedArtifact(WebVisualStoryBundle bundle)
    {
        var completed = bundle.Artifacts.Where(static artifact =>
                string.Equals(artifact.Role, "completed", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (completed.Length != 1 ||
            !string.Equals(NormalizeFormat(completed[0].Format), "png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A visual story must declare exactly one completed PNG artifact so the promised outcome is always visible.");
        }
    }

    private static string NormalizeFormat(string format)
        => string.Equals(format, "txt", StringComparison.OrdinalIgnoreCase)
            ? "text"
            : format.Trim().ToLowerInvariant();

    private static bool FormatMatchesExtension(string format, string extension)
    {
        if (format == "text")
            return extension.Equals("txt", StringComparison.OrdinalIgnoreCase);
        if (format == "apng")
        {
            return extension.Equals("png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals("apng", StringComparison.OrdinalIgnoreCase);
        }
        return format.Equals(extension, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateArtifactFormat(
        WebVisualStoryArtifact artifact,
        string path,
        string normalizedFormat,
        string extension)
    {
        if (!FormatMatchesExtension(normalizedFormat, extension))
            throw new InvalidOperationException($"Visual-story artifact format '{artifact.Format}' does not match '{artifact.Path}'.");
        if (IsCompletedArtifact(artifact) &&
            !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Visual-story completed artifact must use a .png file: {artifact.Path}");
        }
    }

    private static bool IsCompletedArtifact(WebVisualStoryArtifact artifact)
        => string.Equals(artifact.Role, "completed", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnimatedArtifact(WebVisualStoryArtifact artifact)
        => string.Equals(artifact.Role, "animated", StringComparison.OrdinalIgnoreCase);

    private static void ValidateArtifactContent(
        WebVisualStoryArtifact artifact,
        string path,
        string displayPath,
        string normalizedFormat)
    {
        if (IsAnimatedArtifact(artifact))
        {
            WebVisualStoryAnimatedArtifactValidator.Validate(path, displayPath, normalizedFormat);
            return;
        }
        switch (normalizedFormat)
        {
            case "svg":
                WebVisualStoryAnimatedArtifactValidator.ValidateSvg(path, displayPath, requireAnimation: false);
                break;
            case "apng":
                WebVisualStoryAnimatedArtifactValidator.Validate(path, displayPath, normalizedFormat);
                break;
            case "gif":
                WebVisualStoryAnimatedArtifactValidator.ValidateGif(path, displayPath, requireMultipleFrames: false);
                break;
            case "png":
                ValidatePng(path, displayPath);
                break;
            case "html":
            case "text":
                ValidateUtf8Text(path, displayPath);
                break;
        }
    }

    private static void ValidateUtf8Text(string path, string displayPath)
    {
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(File.ReadAllBytes(path));
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidOperationException($"Visual-story text artifact must contain valid UTF-8 text: {displayPath}", ex);
        }
    }

    private static void ValidatePng(string path, string displayPath)
    {
        try
        {
            var info = new MagickImageInfo(path);
            if (info.Format != MagickFormat.Png || info.Width == 0 || info.Height == 0)
                throw new InvalidOperationException($"Visual-story artifact is not a decodable PNG: {displayPath}");
            if ((ulong)info.Width * info.Height > 100_000_000UL)
                throw new InvalidOperationException($"Visual-story completed PNG exceeds the 100-megapixel safety limit: {displayPath}");
            using var image = new MagickImage(path);
            if (image.Format != MagickFormat.Png || image.Width == 0 || image.Height == 0)
                throw new InvalidOperationException($"Visual-story artifact is not a decodable PNG: {displayPath}");
        }
        catch (MagickException ex)
        {
            throw new InvalidOperationException($"Visual-story artifact is not a decodable PNG: {displayPath}", ex);
        }
    }

    private static string GetMediaType(string format)
        => format switch
        {
            "svg" => "image/svg+xml",
            "gif" => "image/gif",
            "apng" or "png" => "image/png",
            "html" => "text/html",
            _ => "text/plain"
        };

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ValidateDeclaredIntegrity(WebVisualStoryArtifact artifact, long actualBytes, string actualSha256)
    {
        if (artifact.Bytes is not null && artifact.Bytes.Value != actualBytes)
            throw new InvalidOperationException($"Visual-story artifact size does not match its manifest: {artifact.Path}");
        if (!string.IsNullOrWhiteSpace(artifact.Sha256) &&
            !string.Equals(artifact.Sha256, actualSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Visual-story artifact digest does not match its manifest: {artifact.Path}");
        }
    }

    private static string[] LoadDeclaredArtifactPaths(string manifestPath, string outputRoot)
    {
        var bundle = DeserializeManifest(manifestPath);
        ValidateBundle(bundle);
        var paths = new List<string>(bundle.Artifacts.Length);
        var portablePaths = new Dictionary<string, (string DeclaredPath, bool IsDirectory)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in bundle.Artifacts)
        {
            ValidateArtifact(artifact);
            VisualStoryPortablePathValidator.Validate(artifact.Path);
            ValidateReservedStagedPath(artifact.Path.Replace('\\', '/'));
            ValidatePortablePathTopology(artifact.Path.Replace('\\', '/'), portablePaths);
            string fullPath;
            try
            {
                fullPath = VisualStoryPathGuard.ResolveRelativePath(
                    outputRoot,
                    artifact.Path,
                    "existing staged artifact");
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidOperationException(
                    $"Existing visual-story manifest contains an invalid artifact path: {artifact.Path}",
                    ex);
            }
            paths.Add(Path.GetRelativePath(outputRoot, fullPath).Replace('\\', '/'));
        }
        return paths.ToArray();
    }
}
