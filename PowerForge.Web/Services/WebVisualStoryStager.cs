using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;

namespace PowerForge.Web;

/// <summary>Validates producer output and stages a portable visual-story bundle.</summary>
public static class WebVisualStoryStager
{
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "svg", "gif", "apng", "png", "html", "text", "txt"
    };

    private static readonly HashSet<string> SupportedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "animated", "completed", "transcript", "source", "html"
    };

    /// <summary>Validates and stages a producer-emitted bundle.</summary>
    /// <param name="options">Staging options.</param>
    /// <returns>Normalized staged bundle details.</returns>
    public static WebVisualStoryStageResult Stage(WebVisualStoryStageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ManifestPath))
            throw new ArgumentException("A visual-story manifest path is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("A visual-story output path is required.", nameof(options));
        if (options.MaximumArtifactBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumArtifactBytes must be positive.");

        var manifestPath = Path.GetFullPath(options.ManifestPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Visual-story manifest was not found.", manifestPath);

        var sourceRoot = Path.GetDirectoryName(manifestPath)
                         ?? throw new InvalidOperationException("Visual-story manifest has no parent directory.");
        var outputRoot = Path.GetFullPath(options.OutputPath);
        VisualStoryPathGuard.EnsureContainedPath(
            sourceRoot,
            manifestPath,
            "manifest",
            allowRoot: false);
        VisualStoryPathGuard.EnsureContainedPath(
            outputRoot,
            outputRoot,
            "output",
            allowRoot: true);
        var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(
                         File.ReadAllText(manifestPath),
                         WebJson.Options)
                     ?? throw new InvalidOperationException("Visual-story manifest is empty or invalid.");

        ValidateBundle(bundle);
        ValidateCompletedArtifact(bundle);

        var resolved = new List<(WebVisualStoryArtifact Artifact, string SourcePath, string FileName, long Bytes, string Sha256)>();
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in bundle.Artifacts)
        {
            ValidateArtifact(artifact);
            var sourcePath = VisualStoryPathGuard.ResolveRelativePath(sourceRoot, artifact.Path, "artifact");
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Visual-story artifact was not found: {artifact.Path}", sourcePath);

            var info = new FileInfo(sourcePath);
            if (info.Length > options.MaximumArtifactBytes)
                throw new InvalidOperationException(
                    $"Visual-story artifact exceeds the {options.MaximumArtifactBytes}-byte limit: {artifact.Path}");

            var extension = Path.GetExtension(sourcePath).TrimStart('.');
            var normalizedFormat = NormalizeFormat(artifact.Format);
            ValidateArtifactFormat(artifact, sourcePath, normalizedFormat, extension);
            if (IsCompletedArtifact(artifact))
            {
                ValidateCompletedPng(sourcePath, artifact.Path);
            }

            var fileName = Path.GetFileName(sourcePath);
            if (!fileNames.Add(fileName))
                throw new InvalidOperationException($"Visual-story artifact file names must be unique: {fileName}");

            resolved.Add((artifact, sourcePath, fileName, info.Length, ComputeSha256(sourcePath)));
        }

        Directory.CreateDirectory(outputRoot);
        foreach (var item in resolved)
        {
            var destination = Path.Combine(outputRoot, item.FileName);
            var samePath = string.Equals(
                Path.GetFullPath(item.SourcePath),
                Path.GetFullPath(destination),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (!samePath && File.Exists(destination) && !options.Overwrite)
                throw new IOException($"Visual-story artifact already exists: {destination}");
            if (!samePath)
                File.Copy(item.SourcePath, destination, overwrite: options.Overwrite);
            item.Artifact.Path = item.FileName.Replace('\\', '/');
            item.Artifact.Format = NormalizeFormat(item.Artifact.Format);
            item.Artifact.MediaType ??= GetMediaType(item.Artifact.Format);
            item.Artifact.Bytes = item.Bytes;
            item.Artifact.Sha256 = item.Sha256;
        }

        var stagedManifestPath = Path.Combine(outputRoot, "visual-story.json");
        File.WriteAllText(
            stagedManifestPath,
            JsonSerializer.Serialize(bundle, WebJson.Options));

        return new WebVisualStoryStageResult
        {
            ManifestPath = stagedManifestPath,
            Bundle = bundle,
            ArtifactCount = resolved.Count,
            TotalBytes = resolved.Sum(static item => item.Bytes)
        };
    }

    /// <summary>Loads and validates a staged visual-story manifest without executing anything.</summary>
    /// <param name="manifestPath">Manifest path.</param>
    /// <returns>Validated bundle.</returns>
    public static WebVisualStoryBundle Load(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("A visual-story manifest path is required.", nameof(manifestPath));

        var fullPath = Path.GetFullPath(manifestPath);
        var manifestRoot = Path.GetDirectoryName(fullPath)
                           ?? throw new InvalidOperationException("Visual-story manifest has no parent directory.");
        VisualStoryPathGuard.EnsureContainedPath(
            manifestRoot,
            fullPath,
            "manifest",
            allowRoot: false);
        var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(
                         File.ReadAllText(fullPath),
                         WebJson.Options)
                     ?? throw new InvalidOperationException("Visual-story manifest is empty or invalid.");
        ValidateBundle(bundle);
        ValidateCompletedArtifact(bundle);
        foreach (var artifact in bundle.Artifacts)
        {
            ValidateArtifact(artifact);
            var artifactPath = VisualStoryPathGuard.ResolveRelativePath(
                manifestRoot,
                artifact.Path,
                "artifact");
            if (!File.Exists(artifactPath))
                throw new FileNotFoundException($"Visual-story artifact was not found: {artifact.Path}", artifactPath);
            var info = new FileInfo(artifactPath);
            var normalizedFormat = NormalizeFormat(artifact.Format);
            var extension = Path.GetExtension(artifactPath).TrimStart('.');
            ValidateArtifactFormat(artifact, artifactPath, normalizedFormat, extension);
            if (IsCompletedArtifact(artifact))
            {
                ValidateCompletedPng(artifactPath, artifact.Path);
            }
            if (artifact.Bytes is not null && artifact.Bytes.Value != info.Length)
                throw new InvalidOperationException($"Visual-story artifact size does not match its manifest: {artifact.Path}");
            if (!string.IsNullOrWhiteSpace(artifact.Sha256) &&
                !string.Equals(artifact.Sha256, ComputeSha256(artifactPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Visual-story artifact digest does not match its manifest: {artifact.Path}");
        }
        return bundle;
    }

    private static void ValidateBundle(WebVisualStoryBundle bundle)
    {
        if (bundle.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported visual-story schema version: {bundle.SchemaVersion}");
        Require(bundle.Id, "id");
        Require(bundle.Title, "title");
        Require(bundle.Alt, "alt");
        Require(bundle.Outcome, "outcome");
        if (bundle.Artifacts is null || bundle.Artifacts.Length == 0)
            throw new InvalidOperationException("Visual-story manifest must declare artifacts.");
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
            return extension.Equals("png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals("apng", StringComparison.OrdinalIgnoreCase);
        return format.Equals(extension, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateArtifactFormat(
        WebVisualStoryArtifact artifact,
        string path,
        string normalizedFormat,
        string extension)
    {
        if (!FormatMatchesExtension(normalizedFormat, extension))
        {
            throw new InvalidOperationException(
                $"Visual-story artifact format '{artifact.Format}' does not match '{artifact.Path}'.");
        }

        if (IsCompletedArtifact(artifact) &&
            !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Visual-story completed artifact must use a .png file: {artifact.Path}");
        }
    }

    private static bool IsCompletedArtifact(WebVisualStoryArtifact artifact)
        => string.Equals(artifact.Role, "completed", StringComparison.OrdinalIgnoreCase);

    private static void ValidateCompletedPng(string path, string displayPath)
    {
        try
        {
            var info = new MagickImageInfo(path);
            if (info.Format != MagickFormat.Png || info.Width == 0 || info.Height == 0)
            {
                throw new InvalidOperationException($"Visual-story completed artifact is not a decodable PNG: {displayPath}");
            }
            if ((ulong)info.Width * info.Height > 100_000_000UL)
            {
                throw new InvalidOperationException(
                    $"Visual-story completed PNG exceeds the 100-megapixel safety limit: {displayPath}");
            }

            using var image = new MagickImage(path);
            if (image.Format != MagickFormat.Png || image.Width == 0 || image.Height == 0)
            {
                throw new InvalidOperationException($"Visual-story completed artifact is not a decodable PNG: {displayPath}");
            }
        }
        catch (MagickException ex)
        {
            throw new InvalidOperationException(
                $"Visual-story completed artifact is not a decodable PNG: {displayPath}",
                ex);
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

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Visual-story {name} is required.");
    }
}
