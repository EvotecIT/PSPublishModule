using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Validates producer output and stages a portable visual-story bundle.</summary>
public static partial class WebVisualStoryStager
{
    internal const long DefaultMaximumArtifactBytes = 25L * 1024L * 1024L;
    internal const long DefaultMaximumTotalArtifactBytes = 100L * 1024L * 1024L;
    internal const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumArtifactCount = 64;
    private const string StagedManifestFileName = "visual-story.json";
    private static readonly JsonSerializerOptions ManifestJsonOptions = CreateManifestJsonOptions();
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "svg", "gif", "apng", "png", "html", "text", "txt"
    };

    private static readonly HashSet<string> SupportedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "animated", "completed", "transcript", "source", "html"
    };

    private static readonly HashSet<string> AnimatedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "svg", "gif", "apng"
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
        if (options.MaximumTotalArtifactBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumTotalArtifactBytes must be positive.");

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
        bundle.ResourceLimits = new WebVisualStoryResourceLimits
        {
            MaximumArtifactBytes = options.MaximumArtifactBytes,
            MaximumTotalArtifactBytes = options.MaximumTotalArtifactBytes
        };

        ValidateBundle(bundle);
        ValidateCompletedArtifact(bundle);

        var outputPathComparison = GetFileSystemPathComparison(outputRoot);
        var preserveExistingOutput = SamePath(sourceRoot, outputRoot, outputPathComparison);
        if (!options.Overwrite && Directory.Exists(outputRoot) && !preserveExistingOutput)
            throw new IOException($"Visual-story output directory already exists: {outputRoot}");

        var stagedManifestPath = Path.Combine(outputRoot, StagedManifestFileName);
        if (!options.Overwrite && File.Exists(stagedManifestPath))
            throw new IOException($"Visual-story manifest already exists: {stagedManifestPath}");

        var resolved = new List<(WebVisualStoryArtifact Artifact, string SourcePath, string RelativePath, string DestinationPath, long Bytes, string Sha256)>();
        var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var portablePaths = new Dictionary<string, (string DeclaredPath, bool IsDirectory)>(
            StringComparer.OrdinalIgnoreCase);
        var totalArtifactBytes = 0L;
        foreach (var artifact in bundle.Artifacts)
        {
            ValidateArtifact(artifact);
            var sourcePath = VisualStoryPathGuard.ResolveRelativePath(sourceRoot, artifact.Path, "artifact");
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Visual-story artifact was not found: {artifact.Path}", sourcePath);

            var info = new FileInfo(sourcePath);
            totalArtifactBytes = ReserveArtifactBytes(
                totalArtifactBytes,
                info.Length,
                options.MaximumArtifactBytes,
                options.MaximumTotalArtifactBytes,
                artifact.Path);

            var extension = Path.GetExtension(sourcePath).TrimStart('.');
            var normalizedFormat = NormalizeFormat(artifact.Format);
            ValidateArtifactFormat(artifact, sourcePath, normalizedFormat, extension);
            var sha256 = ComputeSha256(sourcePath);
            ValidateDeclaredIntegrity(artifact, info.Length, sha256);
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath).Replace('\\', '/');
            VisualStoryPortablePathValidator.Validate(relativePath);
            ValidateReservedStagedPath(relativePath);
            if (!relativePaths.Add(relativePath))
                throw new InvalidOperationException($"Visual-story artifact paths must be unique: {relativePath}");
            ValidatePortablePathTopology(relativePath, portablePaths);
            var destinationPath = VisualStoryPathGuard.ResolveRelativePath(outputRoot, relativePath, "staged artifact");
            resolved.Add((artifact, sourcePath, relativePath, destinationPath, info.Length, sha256));
        }

        if (!options.Overwrite)
        {
            var collisionPathComparison = GetFileSystemPathComparison(outputRoot);
            var collision = resolved.FirstOrDefault(item =>
                !SamePath(item.SourcePath, item.DestinationPath, collisionPathComparison) &&
                File.Exists(item.DestinationPath));
            if (collision.Artifact is not null)
                throw new IOException($"Visual-story artifact already exists: {collision.DestinationPath}");
        }

        var previousPaths = Array.Empty<string>();
        if (options.Overwrite && File.Exists(stagedManifestPath))
        {
            try
            {
                previousPaths = LoadDeclaredArtifactPaths(stagedManifestPath, outputRoot);
            }
            catch (InvalidOperationException)
            {
                preserveExistingOutput = false;
            }
        }
        var currentPathByIdentity = resolved.ToDictionary(
            static item => item.RelativePath,
            static item => item.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var declaredArtifactPaths = resolved
            .Select(static item => item.RelativePath)
            .ToHashSet(StringComparer.Ordinal);

        var stagingRoot = CreateSiblingPath(outputRoot, "stage");
        try
        {
            if (preserveExistingOutput && Directory.Exists(outputRoot))
                CopyDirectoryContents(outputRoot, stagingRoot);
            else
                Directory.CreateDirectory(stagingRoot);

            foreach (var previousPath in previousPaths)
            {
                var stillCurrent = currentPathByIdentity.TryGetValue(previousPath, out var currentPath);
                if (stillCurrent && string.Equals(previousPath, currentPath, StringComparison.Ordinal))
                    continue;

                var replacedPath = VisualStoryPathGuard.ResolveRelativePath(
                    stagingRoot,
                    previousPath,
                    stillCurrent ? "case-replaced staged artifact" : "obsolete staged artifact");
                DeleteFileForReplacement(replacedPath);
                DeleteEmptyParents(Path.GetDirectoryName(replacedPath), stagingRoot);
            }

            foreach (var item in resolved)
            {
                var temporaryDestination = VisualStoryPathGuard.ResolveRelativePath(
                    stagingRoot,
                    item.RelativePath,
                    "temporary staged artifact");
                EnsureDirectoryCasing(stagingRoot, item.RelativePath);
                DeleteFileForReplacement(temporaryDestination);
                File.Copy(item.SourcePath, temporaryDestination, overwrite: false);
                var stagedInfo = new FileInfo(temporaryDestination);
                var stagedSha256 = ComputeSha256(temporaryDestination);
                if (stagedInfo.Length != item.Bytes ||
                    !string.Equals(stagedSha256, item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Visual-story artifact changed while it was being staged: {item.RelativePath}");
                }
                var stagedFormat = NormalizeFormat(item.Artifact.Format);
                ValidateArtifactContent(
                    item.Artifact,
                    temporaryDestination,
                    item.RelativePath,
                    stagedFormat,
                    stagingRoot,
                    declaredArtifactPaths);
                item.Artifact.Role = item.Artifact.Role.Trim().ToLowerInvariant();
                item.Artifact.Path = item.RelativePath;
                item.Artifact.Format = stagedFormat;
                item.Artifact.MediaType = GetMediaType(item.Artifact.Format);
                item.Artifact.Bytes = item.Bytes;
                item.Artifact.Sha256 = item.Sha256;
            }

            File.WriteAllText(
                Path.Combine(stagingRoot, StagedManifestFileName),
                SerializeBoundedManifest(bundle));
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

    private static void ValidatePortablePathTopology(
        string relativeArtifactPath,
        Dictionary<string, (string DeclaredPath, bool IsDirectory)> portablePaths)
    {
        var segments = relativeArtifactPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var prefix = string.Empty;
        for (var index = 0; index < segments.Length; index++)
        {
            prefix = prefix.Length == 0 ? segments[index] : prefix + "/" + segments[index];
            var portableIdentity = prefix.Normalize(NormalizationForm.FormC);
            var isDirectory = index < segments.Length - 1;
            if (portablePaths.TryGetValue(portableIdentity, out var declared))
            {
                if (declared.IsDirectory != isDirectory)
                {
                    throw new InvalidOperationException(
                        $"Visual-story artifact paths cannot use the same portable path as both a file and directory: {declared.DeclaredPath} and {prefix}");
                }
                if (!string.Equals(prefix, declared.DeclaredPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Visual-story artifact paths must use consistent casing and Unicode normalization: {declared.DeclaredPath} and {prefix}");
                }
            }
            else
            {
                portablePaths.Add(portableIdentity, (prefix, isDirectory));
            }
        }
    }

    internal static void ValidatePortablePathTopologyForTesting(params string[] relativeArtifactPaths)
    {
        var portablePaths = new Dictionary<string, (string DeclaredPath, bool IsDirectory)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var relativeArtifactPath in relativeArtifactPaths)
            ValidatePortablePathTopology(relativeArtifactPath, portablePaths);
    }

    internal static long ReserveArtifactBytes(
        long currentTotalBytes,
        long artifactBytes,
        long maximumArtifactBytes,
        long maximumTotalArtifactBytes,
        string displayPath)
    {
        if (artifactBytes > maximumArtifactBytes)
        {
            throw new InvalidOperationException(
                $"Visual-story artifact exceeds the {maximumArtifactBytes}-byte limit: {displayPath}");
        }

        var nextTotalBytes = checked(currentTotalBytes + artifactBytes);
        if (nextTotalBytes > maximumTotalArtifactBytes)
        {
            throw new InvalidOperationException(
                $"Visual-story artifacts exceed the {maximumTotalArtifactBytes}-byte aggregate limit.");
        }
        return nextTotalBytes;
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
        var maximumArtifactBytes = bundle.ResourceLimits?.MaximumArtifactBytes
                                   ?? DefaultMaximumArtifactBytes;
        var maximumTotalArtifactBytes = bundle.ResourceLimits?.MaximumTotalArtifactBytes
                                        ?? DefaultMaximumTotalArtifactBytes;
        var totalArtifactBytes = 0L;
        var declaredArtifactPaths = bundle.Artifacts
            .Select(static artifact => artifact.Path.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var artifact in bundle.Artifacts)
        {
            ValidateArtifact(artifact);
            var artifactPath = VisualStoryPathGuard.ResolveRelativePath(
                manifestRoot,
                artifact.Path,
                "artifact");
            var portableRelativePath = Path.GetRelativePath(manifestRoot, artifactPath).Replace('\\', '/');
            VisualStoryPortablePathValidator.Validate(portableRelativePath);
            if (!File.Exists(artifactPath))
                throw new FileNotFoundException($"Visual-story artifact was not found: {artifact.Path}", artifactPath);
            var info = new FileInfo(artifactPath);
            totalArtifactBytes = ReserveArtifactBytes(
                totalArtifactBytes,
                info.Length,
                maximumArtifactBytes,
                maximumTotalArtifactBytes,
                artifact.Path);
            var normalizedFormat = NormalizeFormat(artifact.Format);
            var extension = Path.GetExtension(artifactPath).TrimStart('.');
            ValidateArtifactFormat(artifact, artifactPath, normalizedFormat, extension);
            if (artifact.Bytes is not null && artifact.Bytes.Value != info.Length)
                throw new InvalidOperationException($"Visual-story artifact size does not match its manifest: {artifact.Path}");
            if (!string.IsNullOrWhiteSpace(artifact.Sha256) &&
                !string.Equals(artifact.Sha256, ComputeSha256(artifactPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Visual-story artifact digest does not match its manifest: {artifact.Path}");
            ValidateArtifactContent(
                artifact,
                artifactPath,
                artifact.Path,
                normalizedFormat,
                manifestRoot,
                declaredArtifactPaths);
        }
        return bundle;
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
                    DeleteDirectoryTree(outputRoot);
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
                DeleteDirectoryTree(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteDirectoryTree(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
        }
        Directory.Delete(path, recursive: true);
    }

    private static void DeleteEmptyParents(string? directory, string root)
    {
        var comparison = GetFileSystemPathComparison(root);
        while (!string.IsNullOrWhiteSpace(directory) && !SamePath(directory, root, comparison))
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

    private static void DeleteFileForReplacement(string path)
    {
        if (!File.Exists(path))
            return;

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        File.Delete(path);
    }

    private static void EnsureDirectoryCasing(string root, string relativeArtifactPath)
    {
        var relativeDirectory = Path.GetDirectoryName(relativeArtifactPath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(relativeDirectory))
            return;

        var current = root;
        foreach (var segment in relativeDirectory.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var exact = Directory.EnumerateDirectories(current)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), segment, StringComparison.Ordinal));
            if (exact is not null)
            {
                current = exact;
                continue;
            }

            var insensitive = Directory.EnumerateDirectories(current)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), segment, StringComparison.OrdinalIgnoreCase));
            var desired = Path.Combine(current, segment);
            if (insensitive is not null)
            {
                var temporary = Path.Combine(current, ".powerforge-case-" + Guid.NewGuid().ToString("N"));
                Directory.Move(insensitive, temporary);
                Directory.Move(temporary, desired);
            }
            else
            {
                Directory.CreateDirectory(desired);
            }
            current = desired;
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
                       ReadBoundedManifestText(path),
                       ManifestJsonOptions)
                   ?? throw new InvalidOperationException("Visual-story manifest is empty or invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Visual-story manifest does not match the published schema.", ex);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidOperationException("Visual-story manifest must use valid UTF-8.", ex);
        }
    }

    private static string ReadBoundedManifestText(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        var bytes = new byte[MaximumManifestBytes + 1];
        var totalBytes = 0;
        while (totalBytes < bytes.Length)
        {
            var bytesRead = stream.Read(bytes, totalBytes, bytes.Length - totalBytes);
            if (bytesRead == 0)
                break;
            totalBytes += bytesRead;
        }
        if (totalBytes > MaximumManifestBytes || stream.ReadByte() >= 0)
        {
            throw new InvalidOperationException(
                $"Visual-story manifest exceeds the {MaximumManifestBytes}-byte safety limit.");
        }
        var offset = totalBytes >= 3 &&
                     bytes[0] == 0xEF &&
                     bytes[1] == 0xBB &&
                     bytes[2] == 0xBF
            ? 3
            : 0;
        return StrictUtf8.GetString(bytes, offset, totalBytes - offset);
    }

    private static string SerializeBoundedManifest(WebVisualStoryBundle bundle)
    {
        var manifest = JsonSerializer.Serialize(bundle, WebJson.Options);
        if (StrictUtf8.GetByteCount(manifest) > MaximumManifestBytes)
        {
            throw new InvalidOperationException(
                $"Visual-story manifest exceeds the {MaximumManifestBytes}-byte safety limit after staging.");
        }
        return manifest;
    }
}
