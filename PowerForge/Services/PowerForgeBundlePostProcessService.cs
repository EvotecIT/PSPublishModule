using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Applies reusable archive/delete/metadata bundle finishing rules to an existing bundle directory.
/// </summary>
public sealed class PowerForgeBundlePostProcessService
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new bundle post-process service.
    /// </summary>
    public PowerForgeBundlePostProcessService(ILogger logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Applies the configured post-process rules to the bundle directory.
    /// </summary>
    public PowerForgeBundlePostProcessResult Run(PowerForgeBundlePostProcessRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var postProcess = request.PostProcess;
        if (postProcess is null)
        {
            return new PowerForgeBundlePostProcessResult
            {
                BundleRoot = ResolveBundleRoot(request.BundleRoot),
                CreatedUtc = DateTimeOffset.UtcNow.ToString("o")
            };
        }

        var projectRoot = string.IsNullOrWhiteSpace(request.ProjectRoot)
            ? ResolveBundleRoot(request.BundleRoot)
            : Path.GetFullPath(request.ProjectRoot);
        var bundleRoot = ResolveBundleRoot(request.BundleRoot);

        if (!request.AllowOutputOutsideProjectRoot)
            DotNetPublishPipelineRunner.EnsurePathWithinRoot(projectRoot, bundleRoot, "BundleRoot");

        var createdUtc = DateTimeOffset.UtcNow.ToString("o");
        var tokens = BuildTokens(request, projectRoot, bundleRoot, createdUtc);
        var archivePaths = request.SkipArchiveDirectories
            ? Array.Empty<string>()
            : ApplyArchiveRules(request.BundleId, bundleRoot, postProcess.ArchiveDirectories, tokens);
        var combinedDeletePatterns = (postProcess.DeletePatterns ?? Array.Empty<string>())
            .Concat(request.AdditionalDeletePatterns ?? Array.Empty<string>())
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var deletedPaths = ApplyDeletePatterns(bundleRoot, combinedDeletePatterns);
        var metadataPath = request.SkipMetadata
            ? null
            : WriteMetadata(projectRoot, request, bundleRoot, createdUtc, tokens);

        return new PowerForgeBundlePostProcessResult
        {
            BundleRoot = bundleRoot,
            CreatedUtc = createdUtc,
            ArchivePaths = archivePaths,
            DeletedPaths = deletedPaths,
            MetadataPath = metadataPath
        };
    }

    private static string ResolveBundleRoot(string bundleRoot)
    {
        var fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(bundleRoot)
            ? throw new InvalidOperationException("BundleRoot is required.")
            : bundleRoot);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Bundle root was not found: {fullPath}");

        return fullPath;
    }

    private Dictionary<string, string> BuildTokens(
        PowerForgeBundlePostProcessRequest request,
        string projectRoot,
        string bundleRoot,
        string createdUtc)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bundle"] = (request.BundleId ?? string.Empty).Trim(),
            ["bundleId"] = (request.BundleId ?? string.Empty).Trim(),
            ["target"] = (request.TargetName ?? string.Empty).Trim(),
            ["rid"] = (request.Runtime ?? string.Empty).Trim(),
            ["framework"] = (request.Framework ?? string.Empty).Trim(),
            ["style"] = (request.Style ?? string.Empty).Trim(),
            ["configuration"] = string.IsNullOrWhiteSpace(request.Configuration) ? "Release" : request.Configuration!.Trim(),
            ["projectRoot"] = projectRoot,
            ["output"] = bundleRoot,
            ["bundleOutput"] = bundleRoot,
            ["sourceOutput"] = string.IsNullOrWhiteSpace(request.SourceOutputPath) ? string.Empty : Path.GetFullPath(request.SourceOutputPath),
            ["zip"] = string.IsNullOrWhiteSpace(request.ZipPath) ? string.Empty : Path.GetFullPath(request.ZipPath),
            ["createdUtc"] = createdUtc
        };

        foreach (var entry in request.Tokens ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
                continue;

            tokens[entry.Key.Trim()] = entry.Value ?? string.Empty;
        }

        return tokens;
    }

    private string[] ApplyArchiveRules(
        string? bundleId,
        string bundleRoot,
        IReadOnlyList<DotNetPublishBundleArchiveRule>? rules,
        IReadOnlyDictionary<string, string> tokens)
    {
        var archives = new List<string>();
        foreach (var rule in rules ?? Array.Empty<DotNetPublishBundleArchiveRule>())
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Path))
                continue;

            var rootPath = DotNetPublishPipelineRunner.ResolvePath(bundleRoot, DotNetPublishPipelineRunner.ApplyTemplate(rule.Path!, tokens));
            DotNetPublishPipelineRunner.EnsurePathWithinRoot(bundleRoot, rootPath, $"Bundle '{bundleId ?? string.Empty}' archive path");

            if (!Directory.Exists(rootPath))
            {
                _logger.Warn($"Bundle '{bundleId ?? string.Empty}' archive path was not found: {rootPath}");
                continue;
            }

            IEnumerable<string> directories = rule.Mode == DotNetPublishBundleArchiveMode.ChildDirectories
                ? Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly)
                : new[] { rootPath };

            foreach (var directory in directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var directoryName = new DirectoryInfo(directory).Name;
                var archiveTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in tokens)
                    archiveTokens[entry.Key] = entry.Value;
                archiveTokens["name"] = directoryName;

                var archiveName = DotNetPublishPipelineRunner.ApplyTemplate(
                    string.IsNullOrWhiteSpace(rule.ArchiveNameTemplate) ? "{name}.zip" : rule.ArchiveNameTemplate!,
                    archiveTokens);
                if (!archiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    archiveName += ".zip";

                var archivePath = Path.Combine(Path.GetDirectoryName(directory)!, archiveName);
                DotNetPublishPipelineRunner.EnsurePathWithinRoot(bundleRoot, archivePath, $"Bundle '{bundleId ?? string.Empty}' archive output path");

                if (File.Exists(archivePath))
                    File.Delete(archivePath);

                ZipFile.CreateFromDirectory(directory, archivePath);
                archives.Add(archivePath);
                _logger.Info($"Bundle archive -> {archivePath}");

                if (rule.DeleteSource && Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        return archives.ToArray();
    }

    private string[] ApplyDeletePatterns(string bundleRoot, IReadOnlyList<string>? patterns)
    {
        var deleted = new List<string>();
        foreach (var pattern in (patterns ?? Array.Empty<string>())
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim()))
        {
            var matches = FindPatternMatches(bundleRoot, pattern);
            foreach (var match in matches.OrderByDescending(path => path.Length).ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (Directory.Exists(match))
                    {
                        Directory.Delete(match, recursive: true);
                        deleted.Add(match);
                    }
                    else if (File.Exists(match))
                    {
                        File.Delete(match);
                        deleted.Add(match);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to delete bundle post-process match '{match}'. Error: {ex.Message}");
                }
            }
        }

        return deleted.ToArray();
    }

    private static string[] FindPatternMatches(string bundleRoot, string pattern)
    {
        var normalizedPattern = (pattern ?? string.Empty)
            .Trim()
            .Replace('\\', '/');
        if (normalizedPattern.Length == 0)
            return Array.Empty<string>();

        var exactPath = DotNetPublishPipelineRunner.ResolvePath(bundleRoot, normalizedPattern);
        if ((File.Exists(exactPath) || Directory.Exists(exactPath)) && IsPathInside(bundleRoot, exactPath))
            return new[] { exactPath };

        return Directory.EnumerateFileSystemEntries(bundleRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = DotNetPublishPipelineRunner.GetRelativePath(bundleRoot, path).Replace('\\', '/');
                return PatternMatches(relative, normalizedPattern);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool PatternMatches(string relativePath, string pattern)
    {
        if (DotNetPublishPipelineRunner.WildcardMatch(relativePath, pattern))
            return true;

        if (pattern.StartsWith("**/", StringComparison.Ordinal))
            return DotNetPublishPipelineRunner.WildcardMatch(relativePath, pattern.Substring("**/".Length));

        return false;
    }

    private string? WriteMetadata(
        string projectRoot,
        PowerForgeBundlePostProcessRequest request,
        string bundleRoot,
        string createdUtc,
        IReadOnlyDictionary<string, string> tokens)
    {
        var metadata = request.PostProcess?.Metadata;
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Path))
            return null;

        var metadataPath = DotNetPublishPipelineRunner.ResolvePath(bundleRoot, DotNetPublishPipelineRunner.ApplyTemplate(metadata.Path!, tokens));
        DotNetPublishPipelineRunner.EnsurePathWithinRoot(bundleRoot, metadataPath, $"Bundle '{request.BundleId ?? string.Empty}' metadata path");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (metadata.IncludeStandardProperties)
        {
            payload["schemaVersion"] = 1;
            payload["bundleId"] = request.BundleId ?? string.Empty;
            payload["target"] = request.TargetName ?? string.Empty;
            payload["runtime"] = request.Runtime ?? string.Empty;
            payload["framework"] = request.Framework ?? string.Empty;
            payload["style"] = request.Style ?? string.Empty;
            payload["configuration"] = string.IsNullOrWhiteSpace(request.Configuration) ? "Release" : request.Configuration;
            payload["projectRoot"] = projectRoot;
            payload["outputPath"] = bundleRoot;
            payload["zipPath"] = request.ZipPath;
            payload["createdUtc"] = createdUtc;
        }

        foreach (var property in metadata.Properties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            payload[property.Key] = CoerceMetadataValue(DotNetPublishPipelineRunner.ApplyTemplate(property.Value ?? string.Empty, tokens));

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metadataPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _logger.Info($"Bundle metadata -> {metadataPath}");
        return metadataPath;
    }

    private static object? CoerceMetadataValue(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (bool.TryParse(trimmed, out var boolValue))
            return boolValue;
        if (int.TryParse(trimmed, out var intValue))
            return intValue;
        if (long.TryParse(trimmed, out var longValue))
            return longValue;
        return value;
    }

    private static bool IsPathInside(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var comparison = DotNetPublishPipelineRunner.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
                candidate,
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                comparison)
            || candidate.StartsWith(root, comparison);
    }
}
