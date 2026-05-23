using System.Text.Json;
using PowerForge;

namespace PowerForge.Web;

/// <summary>
/// Generates private gallery JSON data for static websites.
/// </summary>
public static class WebPrivateGalleryGenerator
{
    /// <summary>
    /// Generates private gallery JSON outputs.
    /// </summary>
    /// <param name="options">Generation options.</param>
    /// <param name="indexer">Optional indexer override used by tests.</param>
    /// <returns>Generation result.</returns>
    public static WebPrivateGalleryResult Generate(WebPrivateGalleryOptions options, PrivateGalleryIndexer? indexer = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("OutputDirectory is required.", nameof(options));

        var baseDir = ResolveBaseDirectory(options.BaseDirectory);
        var outputDirectory = ResolvePath(options.OutputDirectory, baseDir);
        Directory.CreateDirectory(outputDirectory);

        var token = options.Token;
        if (string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(options.TokenEnvironmentVariable))
            token = Environment.GetEnvironmentVariable(options.TokenEnvironmentVariable);

        var galleryIndexer = indexer ?? new PrivateGalleryIndexer();
        var result = galleryIndexer.IndexAsync(new PrivateGalleryIndexOptions
        {
            Provider = PrivateGalleryIndexProvider.AzureArtifacts,
            Organization = options.Organization,
            Project = options.Project,
            Feed = options.Feed,
            RepositoryName = options.RepositoryName,
            Title = options.Title,
            IncludeAllVersions = options.IncludeAllVersions,
            IncludePackageContent = options.IncludePackageContent,
            IncludeMetrics = options.IncludeMetrics,
            MaxPackages = options.MaxPackages,
            MaxVersionsPerPackage = options.MaxVersionsPerPackage,
            RequestTimeoutSeconds = options.RequestTimeoutSeconds,
            Token = token,
            AuthenticationKind = options.AuthenticationKind,
            TempDirectory = ResolveOptionalPath(options.TempDirectory, baseDir)
        }).GetAwaiter().GetResult();

        var feedPath = Path.Combine(outputDirectory, "feed.json");
        File.WriteAllText(feedPath, JsonSerializer.Serialize(result.Document, WebJson.Options));

        var moduleDirectory = Path.Combine(outputDirectory, "modules");
        Directory.CreateDirectory(moduleDirectory);
        foreach (var package in result.Document.Packages)
        {
            var packagePath = Path.Combine(moduleDirectory, MakeSafeFileName(package.Name) + ".json");
            File.WriteAllText(packagePath, JsonSerializer.Serialize(package, WebJson.Options));

            var versionDirectory = Path.Combine(moduleDirectory, MakeSafeFileName(package.Name));
            Directory.CreateDirectory(versionDirectory);
            foreach (var version in package.Versions)
            {
                var versionPath = Path.Combine(versionDirectory, MakeSafeFileName(version.Version) + ".json");
                File.WriteAllText(versionPath, JsonSerializer.Serialize(version, WebJson.Options));
            }
        }

        var searchDocument = WebPrivateGallerySearchBuilder.Build(result.Document);
        var searchPath = Path.Combine(outputDirectory, "search.json");
        File.WriteAllText(searchPath, JsonSerializer.Serialize(searchDocument, WebJson.Options));

        return new WebPrivateGalleryResult
        {
            FeedPath = feedPath,
            SearchPath = searchPath,
            PackageCount = result.Document.Summary.PackageCount,
            VersionCount = result.Document.Summary.VersionCount,
            CommandCount = result.Document.Summary.CommandCount,
            Warnings = result.Warnings.ToArray()
        };
    }

    private static string ResolveBaseDirectory(string? baseDirectory)
        => string.IsNullOrWhiteSpace(baseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(baseDirectory);

    private static string ResolvePath(string path, string baseDirectory)
        => Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path));

    private static string? ResolveOptionalPath(string? path, string baseDirectory)
        => string.IsNullOrWhiteSpace(path) ? null : ResolvePath(path!, baseDirectory);

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}
