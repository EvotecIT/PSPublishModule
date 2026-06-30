using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class ExternalAssetPreparationService
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger _logger;
    private readonly Func<Uri, string, TimeSpan, string> _downloadFile;

    public ExternalAssetPreparationService(ILogger logger)
        : this(logger, DownloadFile)
    {
    }

    internal ExternalAssetPreparationService(
        ILogger logger,
        Func<Uri, string, TimeSpan, string> downloadFile)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _downloadFile = downloadFile ?? throw new ArgumentNullException(nameof(downloadFile));
    }

    public ExternalAssetPreparationResult Prepare(
        string projectRoot,
        ConfigurationExternalAssetSegment segment,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("Project root is required.", nameof(projectRoot));
        if (segment is null)
            throw new ArgumentNullException(nameof(segment));

        var configuration = segment.Configuration ?? new ExternalAssetConfiguration();
        var name = RequireValue(configuration.Name, "External asset Name is required.");
        var outputRoot = ResolveProjectPath(projectRoot, RequireValue(configuration.OutputPath, $"External asset '{name}' requires OutputPath."));
        EnsureSameOrChildPath(projectRoot, outputRoot, $"External asset '{name}' OutputPath");
        var manifestPath = string.IsNullOrWhiteSpace(configuration.ManifestPath)
            ? Path.Combine(outputRoot, "manifest.json")
            : ResolveProjectPath(projectRoot, configuration.ManifestPath!);
        EnsureSameOrChildPath(projectRoot, manifestPath, $"External asset '{name}' ManifestPath");
        var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? outputRoot;
        var files = configuration.Files ?? Array.Empty<ExternalAssetFileConfiguration>();

        if (files.Length == 0)
            throw new InvalidOperationException($"External asset '{name}' must declare at least one file.");

        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(manifestDirectory);

        var fileResults = new List<ExternalAssetFilePreparationResult>();
        var manifestFiles = new List<ExternalAssetManifestFile>();
        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(5);
        var outputPathComparison = GetPathStringComparison(outputRoot);
        var preparedOutputPaths = new HashSet<string>(GetPathStringComparer(outputPathComparison));

        foreach (var file in files)
        {
            var resolvedFile = ResolveFile(outputRoot, manifestPath, outputPathComparison, file);
            var comparableOutputPath = NormalizePathForComparison(resolvedFile.TargetPath);
            if (!preparedOutputPaths.Add(comparableOutputPath))
                throw new InvalidOperationException($"External asset file '{resolvedFile.OutputRelativePath}' resolves to an output path already used by another file entry.");

            var result = PrepareFile(projectRoot, manifestDirectory, configuration.SkipDownload, resolvedFile, effectiveTimeout);
            fileResults.Add(result);
            manifestFiles.Add(new ExternalAssetManifestFile
            {
                Runtime = result.Runtime,
                Path = result.ManifestPath,
                Sha256 = result.Sha256,
                Architecture = result.Architecture
            });
        }

        var manifest = new ExternalAssetManifest
        {
            Name = name,
            Version = NormalizeOptionalValue(configuration.Version),
            Source = NormalizeOptionalValue(configuration.Source),
            License = NormalizeOptionalValue(configuration.License),
            Files = manifestFiles.ToArray()
        };

        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJsonOptions) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        _logger.Info($"Prepared external asset '{name}' ({fileResults.Count} file(s)) -> {outputRoot}");

        return new ExternalAssetPreparationResult(
            name,
            outputRoot,
            manifestPath,
            fileResults.ToArray());
    }

    private ExternalAssetFilePreparationResult PrepareFile(
        string projectRoot,
        string manifestDirectory,
        bool skipDownload,
        ResolvedExternalAssetFile file,
        TimeSpan timeout)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath) ?? string.Empty);
        if (skipDownload)
        {
            if (!File.Exists(file.TargetPath))
                throw new FileNotFoundException($"External asset file '{file.TargetPath}' is missing and SkipDownload was specified.", file.TargetPath);
        }
        else
        {
            MaterializeSource(projectRoot, file.Source, file.TargetPath, timeout);
        }

        var sha256 = ComputeSha256(file.TargetPath);
        ValidateSha256(file.TargetPath, sha256, file.ExpectedSha256);
        var manifestPath = FrameworkCompatibility.GetRelativePath(manifestDirectory, file.TargetPath)
            .Replace('\\', '/');

        return new ExternalAssetFilePreparationResult(
            file.Runtime,
            file.Architecture,
            file.TargetPath,
            manifestPath,
            sha256);
    }

    private static ResolvedExternalAssetFile ResolveFile(
        string outputRoot,
        string generatedManifestPath,
        StringComparison pathComparison,
        ExternalAssetFileConfiguration? file)
    {
        if (file is null)
            throw new InvalidOperationException("External asset file configuration is required.");

        var runtime = RequireValue(file.Runtime, "External asset file Runtime is required.");
        var fileName = RequireValue(file.FileName, $"External asset file for runtime '{runtime}' requires FileName.");
        var source = RequireValue(file.Uri, $"External asset file '{fileName}' requires Uri.");
        var outputRelativePath = NormalizeOutputRelativePath(file.Path, fileName);
        var targetPath = ResolveOutputPath(outputRoot, outputRelativePath, pathComparison);
        if (SamePath(targetPath, generatedManifestPath, pathComparison))
            throw new InvalidOperationException($"External asset file '{outputRelativePath}' cannot use the generated manifest path '{generatedManifestPath}'.");

        return new ResolvedExternalAssetFile(
            runtime,
            NormalizeOptionalValue(file.Architecture),
            source,
            outputRelativePath,
            targetPath,
            file.Sha256);
    }

    private void MaterializeSource(string projectRoot, string source, string targetPath, TimeSpan timeout)
    {
        if (TryCreateHttpUri(source, out var httpUri))
        {
            _logger.Info($"Downloading external asset file '{Path.GetFileName(targetPath)}' from {httpUri}");
            _downloadFile(httpUri!, targetPath, timeout);
            return;
        }

        var sourcePath = ResolveSourcePath(projectRoot, source);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"External asset source file was not found: {sourcePath}", sourcePath);

        if (SamePath(sourcePath, targetPath, GetPathStringComparison(Path.GetDirectoryName(targetPath) ?? projectRoot)))
            return;

        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static string ResolveProjectPath(string projectRoot, string path)
    {
        var cleaned = PathValueResolver.Clean(path);
        return Path.GetFullPath(Path.IsPathRooted(cleaned)
            ? cleaned
            : Path.Combine(projectRoot, cleaned));
    }

    private static string ResolveSourcePath(string projectRoot, string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
            return uri.LocalPath;

        return ResolveProjectPath(projectRoot, source);
    }

    private static string ResolveOutputPath(string outputRoot, string outputRelativePath, StringComparison pathComparison)
    {
        var full = Path.GetFullPath(Path.Combine(outputRoot, outputRelativePath));
        if (!IsSameOrChildPath(outputRoot, full, pathComparison))
            throw new InvalidOperationException($"External asset output path '{outputRelativePath}' escapes output root '{outputRoot}'.");

        return full;
    }

    private static string NormalizeOutputRelativePath(string? path, string fileName)
    {
        var value = string.IsNullOrWhiteSpace(path) ? fileName : path!.Trim();
        var cleaned = PathValueResolver.Clean(value);
        if (Path.IsPathRooted(cleaned))
            throw new InvalidOperationException($"External asset output path '{value}' must be relative.");

        return cleaned.Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool TryCreateHttpUri(string value, out Uri? uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        uri = null;
        return false;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void ValidateSha256(string path, string actualSha256, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return;

        var expected = NormalizeSha256(expectedSha256!);
        if (!string.Equals(actualSha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"External asset SHA256 mismatch for '{path}'. Expected {expected}, actual {actualSha256}.");
    }

    private static string NormalizeSha256(string value)
        => value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value.Substring("sha256:".Length).Trim()
            : value.Trim();

    private static string RequireValue(string? value, string message)
    {
        var normalized = NormalizeOptionalValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException(message);

        return normalized!;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool SamePath(string left, string right, StringComparison pathComparison)
    {
        var leftFull = NormalizePathForComparison(left);
        var rightFull = NormalizePathForComparison(right);
        return string.Equals(leftFull, rightFull, pathComparison);
    }

    private static string NormalizePathForComparison(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static StringComparer GetPathStringComparer(StringComparison pathComparison)
        => pathComparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static void EnsureSameOrChildPath(string rootPath, string candidatePath, string label)
    {
        if (!IsSameOrChildPath(rootPath, candidatePath, GetPathStringComparison(rootPath)))
            throw new InvalidOperationException($"{label} must resolve inside project root '{rootPath}'.");
    }

    private static bool IsSameOrChildPath(string rootPath, string candidatePath, StringComparison pathComparison)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(root, candidate, pathComparison))
            return true;

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var candidateWithSeparator = candidate + Path.DirectorySeparatorChar;
        return candidateWithSeparator.StartsWith(rootWithSeparator, pathComparison);
    }

    private static StringComparison GetPathStringComparison(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                return IsCaseSensitiveDirectory(directory)
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;
        }
        catch
        {
            // fall back to the platform default below
        }

        return FrameworkCompatibility.PathStringComparison();
    }

    private static bool IsCaseSensitiveDirectory(string directory)
    {
        var probeName = "powerforge-case-" + Guid.NewGuid().ToString("N") + "a.tmp";
        var probePath = Path.Combine(directory, probeName);
        var alternatePath = Path.Combine(directory, probeName.ToUpperInvariant());
        try
        {
            File.WriteAllText(probePath, string.Empty);
            return !File.Exists(alternatePath);
        }
        finally
        {
            TryDeleteFile(probePath);
            TryDeleteFile(alternatePath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort cleanup
        }
    }

    private static string DownloadFile(Uri uri, string targetPath, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = timeout };
        using var response = http.GetAsync(uri).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var output = File.Create(targetPath);
        input.CopyTo(output);
        return targetPath;
    }

    private sealed class ExternalAssetManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("license")]
        public string? License { get; set; }

        [JsonPropertyName("files")]
        public ExternalAssetManifestFile[] Files { get; set; } = Array.Empty<ExternalAssetManifestFile>();
    }

    private sealed class ExternalAssetManifestFile
    {
        [JsonPropertyName("runtime")]
        public string Runtime { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("architecture")]
        public string? Architecture { get; set; }
    }

    private sealed class ResolvedExternalAssetFile
    {
        public ResolvedExternalAssetFile(
            string runtime,
            string? architecture,
            string source,
            string outputRelativePath,
            string targetPath,
            string? expectedSha256)
        {
            Runtime = runtime;
            Architecture = architecture;
            Source = source;
            OutputRelativePath = outputRelativePath;
            TargetPath = targetPath;
            ExpectedSha256 = expectedSha256;
        }

        public string Runtime { get; }

        public string? Architecture { get; }

        public string Source { get; }

        public string OutputRelativePath { get; }

        public string TargetPath { get; }

        public string? ExpectedSha256 { get; }
    }
}

internal sealed class ExternalAssetPreparationResult
{
    public ExternalAssetPreparationResult(
        string name,
        string outputPath,
        string manifestPath,
        ExternalAssetFilePreparationResult[] files)
    {
        Name = name ?? string.Empty;
        OutputPath = outputPath ?? string.Empty;
        ManifestPath = manifestPath ?? string.Empty;
        Files = files ?? Array.Empty<ExternalAssetFilePreparationResult>();
    }

    public string Name { get; }

    public string OutputPath { get; }

    public string ManifestPath { get; }

    public ExternalAssetFilePreparationResult[] Files { get; }
}

internal sealed class ExternalAssetFilePreparationResult
{
    public ExternalAssetFilePreparationResult(
        string runtime,
        string? architecture,
        string filePath,
        string manifestPath,
        string sha256)
    {
        Runtime = runtime ?? string.Empty;
        Architecture = architecture;
        FilePath = filePath ?? string.Empty;
        ManifestPath = manifestPath ?? string.Empty;
        Sha256 = sha256 ?? string.Empty;
    }

    public string Runtime { get; }

    public string? Architecture { get; }

    public string FilePath { get; }

    public string ManifestPath { get; }

    public string Sha256 { get; }
}
