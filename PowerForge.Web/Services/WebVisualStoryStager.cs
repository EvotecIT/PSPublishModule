using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageMagick;

namespace PowerForge.Web;

/// <summary>Validates producer output and stages a portable visual-story bundle.</summary>
public static class WebVisualStoryStager
{
    private const string StagedManifestFileName = "visual-story.json";
    private static readonly JsonSerializerOptions ManifestJsonOptions = CreateManifestJsonOptions();

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
        var bundle = DeserializeManifest(manifestPath);

        ValidateBundle(bundle);
        ValidateCompletedArtifact(bundle);

        var stagedManifestPath = Path.Combine(outputRoot, StagedManifestFileName);
        if (!options.Overwrite && File.Exists(stagedManifestPath))
            throw new IOException($"Visual-story manifest already exists: {stagedManifestPath}");

        var resolved = new List<(WebVisualStoryArtifact Artifact, string SourcePath, string RelativePath, string DestinationPath, long Bytes, string Sha256)>();
        var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            var sha256 = ComputeSha256(sourcePath);
            ValidateDeclaredIntegrity(artifact, info.Length, sha256);
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath).Replace('\\', '/');
            ValidateReservedStagedPath(relativePath);
            if (!relativePaths.Add(relativePath))
                throw new InvalidOperationException($"Visual-story artifact paths must be unique: {relativePath}");
            var destinationPath = VisualStoryPathGuard.ResolveRelativePath(outputRoot, relativePath, "staged artifact");
            resolved.Add((artifact, sourcePath, relativePath, destinationPath, info.Length, sha256));
        }

        if (!options.Overwrite)
        {
            var collision = resolved.FirstOrDefault(item =>
                !SamePath(item.SourcePath, item.DestinationPath) && File.Exists(item.DestinationPath));
            if (collision.Artifact is not null)
                throw new IOException($"Visual-story artifact already exists: {collision.DestinationPath}");
        }

        var previousPaths = options.Overwrite && File.Exists(stagedManifestPath)
            ? LoadDeclaredArtifactPaths(stagedManifestPath, outputRoot)
            : Array.Empty<string>();
        var currentPaths = resolved.Select(static item => item.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stagingRoot = CreateSiblingPath(outputRoot, "stage");
        try
        {
            if (Directory.Exists(outputRoot))
                CopyDirectoryContents(outputRoot, stagingRoot);
            else
                Directory.CreateDirectory(stagingRoot);

            foreach (var item in resolved)
            {
                var temporaryDestination = VisualStoryPathGuard.ResolveRelativePath(
                    stagingRoot,
                    item.RelativePath,
                    "temporary staged artifact");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(temporaryDestination)
                    ?? throw new InvalidOperationException("Visual-story artifact has no destination directory."));
                File.Copy(item.SourcePath, temporaryDestination, overwrite: true);
                item.Artifact.Role = item.Artifact.Role.Trim().ToLowerInvariant();
                item.Artifact.Path = item.RelativePath;
                item.Artifact.Format = NormalizeFormat(item.Artifact.Format);
                item.Artifact.MediaType ??= GetMediaType(item.Artifact.Format);
                item.Artifact.Bytes = item.Bytes;
                item.Artifact.Sha256 = item.Sha256;
            }

            foreach (var previousPath in previousPaths.Where(path => !currentPaths.Contains(path)))
            {
                var obsoletePath = VisualStoryPathGuard.ResolveRelativePath(
                    stagingRoot,
                    previousPath,
                    "obsolete staged artifact");
                if (File.Exists(obsoletePath))
                    File.Delete(obsoletePath);
                DeleteEmptyParents(Path.GetDirectoryName(obsoletePath), stagingRoot);
            }

            File.WriteAllText(
                Path.Combine(stagingRoot, StagedManifestFileName),
                JsonSerializer.Serialize(bundle, WebJson.Options));
            PromoteStagedDirectory(stagingRoot, outputRoot);
        }
        catch
        {
            TryDeleteDirectory(stagingRoot);
            throw;
        }

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
        var bundle = DeserializeManifest(fullPath);
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
        if (bundle.SchemaVersion is null)
            throw new InvalidOperationException("Visual-story schemaVersion is required.");
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
        if (string.Equals(artifact.Role, "transcript", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(NormalizeFormat(artifact.Format), "text", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Visual-story transcript artifacts must use the text format.");
        }
    }

    private static void ValidateReservedStagedPath(string relativePath)
    {
        var firstSeparator = relativePath.IndexOf('/');
        var firstSegment = firstSeparator < 0
            ? relativePath
            : relativePath.Substring(0, firstSeparator);
        if (string.Equals(firstSegment, StagedManifestFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Visual-story artifact path conflicts with the reserved staged manifest: {relativePath}");
        }
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

    private static void ValidateDeclaredIntegrity(
        WebVisualStoryArtifact artifact,
        long actualBytes,
        string actualSha256)
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
        foreach (var artifact in bundle.Artifacts)
        {
            ValidateArtifact(artifact);
            var fullPath = VisualStoryPathGuard.ResolveRelativePath(outputRoot, artifact.Path, "existing staged artifact");
            paths.Add(Path.GetRelativePath(outputRoot, fullPath).Replace('\\', '/'));
        }
        return paths.ToArray();
    }

    private static string CreateSiblingPath(string outputRoot, string role)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        var parent = Path.GetDirectoryName(normalizedRoot)
                     ?? throw new InvalidOperationException("Visual-story output must have a parent directory.");
        Directory.CreateDirectory(parent);
        return Path.Combine(
            parent,
            "." + Path.GetFileName(normalizedRoot) + ".pf-story-" + role + "-" + Guid.NewGuid().ToString("N"));
    }

    private static void CopyDirectoryContents(string sourceRoot, string destinationRoot)
    {
        var source = new DirectoryInfo(sourceRoot);
        if ((source.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Visual-story output cannot be a symbolic link: {sourceRoot}");
        Directory.CreateDirectory(destinationRoot);

        foreach (var file in source.EnumerateFiles())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Visual-story output cannot contain symbolic links: {file.FullName}");
            file.CopyTo(Path.Combine(destinationRoot, file.Name), overwrite: true);
        }
        foreach (var directory in source.EnumerateDirectories())
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Visual-story output cannot contain symbolic links: {directory.FullName}");
            CopyDirectoryContents(directory.FullName, Path.Combine(destinationRoot, directory.Name));
        }
    }

    internal static void PromoteStagedDirectory(string stagingRoot, string outputRoot)
    {
        var backupRoot = CreateSiblingPath(outputRoot, "backup");
        var movedExistingOutput = false;
        try
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Move(outputRoot, backupRoot);
                movedExistingOutput = true;
            }
            Directory.Move(stagingRoot, outputRoot);
        }
        catch
        {
            if (movedExistingOutput && Directory.Exists(backupRoot))
            {
                if (Directory.Exists(outputRoot))
                    Directory.Delete(outputRoot, recursive: true);
                Directory.Move(backupRoot, outputRoot);
            }
            throw;
        }

        TryDeleteDirectory(backupRoot);
    }

    private static void TryDeleteDirectory(string path)
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

    private static bool SamePath(string left, string right)
        => string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void DeleteEmptyParents(string? directory, string root)
    {
        while (!string.IsNullOrWhiteSpace(directory) && !SamePath(directory, root))
        {
            if (!Directory.Exists(directory) ||
                Directory.EnumerateFileSystemEntries(directory).Any())
            {
                break;
            }
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Visual-story {name} is required.");
    }

    private static JsonSerializerOptions CreateManifestJsonOptions()
    {
        return new JsonSerializerOptions(WebJson.Options)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
    }

    private static WebVisualStoryBundle DeserializeManifest(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<WebVisualStoryBundle>(
                       File.ReadAllText(path),
                       ManifestJsonOptions)
                   ?? throw new InvalidOperationException("Visual-story manifest is empty or invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Visual-story manifest does not match the published schema.", ex);
        }
    }
}
