using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge;

namespace PowerForge.Web;

/// <summary>
/// Generates normalized company portal documentation data for static websites.
/// </summary>
public static partial class WebPortalDocsGenerator
{
    /// <summary>
    /// Generates portal documentation JSON outputs.
    /// </summary>
    /// <param name="options">Generation options.</param>
    /// <param name="messageHandler">Optional HTTP handler override used by tests.</param>
    /// <returns>Generation result.</returns>
    public static WebPortalDocsResult Generate(WebPortalDocsOptions options, HttpMessageHandler? messageHandler = null)
        => GenerateAsync(options, messageHandler).GetAwaiter().GetResult();

    private static async Task<WebPortalDocsResult> GenerateAsync(WebPortalDocsOptions options, HttpMessageHandler? messageHandler)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("OutputDirectory is required.", nameof(options));

        var baseDir = ResolveBaseDirectory(options.BaseDirectory);
        var outputDirectory = ResolvePath(options.OutputDirectory, baseDir);
        Directory.CreateDirectory(outputDirectory);

        var sourcesPath = ResolveOptionalPath(options.SourcesPath, baseDir);
        var galleryPath = ResolveOptionalPath(options.PrivateGalleryPath, baseDir);
        var token = ResolveToken(options);
        var document = new WebPortalDocsDocument
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };

        var sourceSpec = LoadSources(sourcesPath, document.Warnings);
        var gallery = LoadPrivateGallery(galleryPath, document.Warnings);
        using var http = CreateHttpClient(options, messageHandler);

        foreach (var source in sourceSpec.Sources)
        {
            var normalizedSource = CreateSource(source);
            document.Sources.Add(normalizedSource);

            try
            {
                var docs = normalizedSource.Kind.Trim().ToLowerInvariant() switch
                {
                    "local" => IndexLocalSource(sourceSpec.Defaults, source, normalizedSource, baseDir, options, document.Warnings),
                    "package" => IndexPackageSource(sourceSpec.Defaults, source, normalizedSource, gallery, document.Warnings),
                    "github" => await IndexGitHubSource(sourceSpec.Defaults, source, normalizedSource, http, options, token, document.Warnings).ConfigureAwait(false),
                    "azure-devops" or "azuredevops" => await IndexAzureDevOpsSource(sourceSpec.Defaults, source, normalizedSource, http, options, token, document.Warnings).ConfigureAwait(false),
                    _ => UnsupportedSource(source, normalizedSource, document.Warnings)
                };

                document.Documents.AddRange(docs);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException or TaskCanceledException)
            {
                AddSourceWarning(normalizedSource, document.Warnings, $"Source '{normalizedSource.Id}' failed: {ex.Message}");
            }
        }

        document.Documents = document.Documents
            .OrderBy(doc => doc.Order)
            .ThenBy(doc => doc.NavigationGroup, StringComparer.OrdinalIgnoreCase)
            .ThenBy(doc => doc.Module, StringComparer.OrdinalIgnoreCase)
            .ThenBy(doc => doc.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        document.Summary = new WebPortalDocsSummary
        {
            SourceCount = document.Sources.Count,
            DocumentCount = document.Documents.Count,
            LocalDocumentCount = document.Documents.Count(doc => doc.SourceKind.Equals("local", StringComparison.OrdinalIgnoreCase)),
            PackageDocumentCount = document.Documents.Count(doc => doc.SourceKind.Equals("package", StringComparison.OrdinalIgnoreCase)),
            RepositoryDocumentCount = document.Documents.Count(doc =>
                doc.SourceKind.Equals("github", StringComparison.OrdinalIgnoreCase) ||
                doc.SourceKind.Equals("azure-devops", StringComparison.OrdinalIgnoreCase) ||
                doc.SourceKind.Equals("azuredevops", StringComparison.OrdinalIgnoreCase))
        };

        var docsPath = Path.Combine(outputDirectory, "docs.json");
        File.WriteAllText(docsPath, JsonSerializer.Serialize(document, WebJson.Options));

        var search = BuildSearch(document);
        var searchPath = Path.Combine(outputDirectory, "search.json");
        File.WriteAllText(searchPath, JsonSerializer.Serialize(search, WebJson.Options));

        return new WebPortalDocsResult
        {
            DocsPath = docsPath,
            SearchPath = searchPath,
            SourceCount = document.Summary.SourceCount,
            DocumentCount = document.Summary.DocumentCount,
            Warnings = document.Warnings.ToArray()
        };
    }

    private static WebPortalDocsSourcesSpec LoadSources(string? sourcesPath, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(sourcesPath))
        {
            warnings.Add("portal-docs-index did not receive a sources file; generated an empty portal docs index.");
            return new WebPortalDocsSourcesSpec();
        }

        if (!File.Exists(sourcesPath))
        {
            warnings.Add($"Portal docs sources file '{sourcesPath}' was not found; generated an empty portal docs index.");
            return new WebPortalDocsSourcesSpec();
        }

        var spec = JsonSerializer.Deserialize<WebPortalDocsSourcesSpec>(File.ReadAllText(sourcesPath), WebJson.Options);
        return spec ?? new WebPortalDocsSourcesSpec();
    }

    private static PrivateGalleryDocument? LoadPrivateGallery(string? galleryPath, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(galleryPath))
            return null;

        if (!File.Exists(galleryPath))
        {
            warnings.Add($"Private gallery feed '{galleryPath}' was not found; package-backed portal docs were skipped.");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PrivateGalleryDocument>(File.ReadAllText(galleryPath), WebJson.Options);
        }
        catch (JsonException ex)
        {
            warnings.Add($"Private gallery feed '{galleryPath}' could not be parsed: {ex.Message}. Package-backed portal docs were skipped.");
            return null;
        }
    }

    private static IEnumerable<WebPortalDocEntry> IndexLocalSource(
        WebPortalDocsDefaults defaults,
        WebPortalDocsSourceSpec source,
        WebPortalDocsSource normalizedSource,
        string baseDir,
        WebPortalDocsOptions options,
        List<string> warnings)
    {
        var root = ResolvePath(source.Path ?? ".", baseDir);
        if (!Directory.Exists(root))
        {
            AddSourceWarning(normalizedSource, warnings, $"Local source '{normalizedSource.Id}' path '{root}' was not found.");
            return Array.Empty<WebPortalDocEntry>();
        }

        var include = EffectiveList(source.Include, defaults.Include, DefaultIncludePatterns());
        var exclude = EffectiveList(source.Exclude, defaults.Exclude, DefaultExcludePatterns());
        var classify = MergeClassify(defaults.Classify, source.Classify);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => (FullPath: path, RelativePath: NormalizePath(Path.GetRelativePath(root, path))))
            .Where(file => IsIncluded(file.RelativePath, include, exclude))
            .Where(file => IsTextDocument(file.RelativePath))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var docs = new List<WebPortalDocEntry>();
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var content = ReadTextWithinLimit(file.FullPath, options);
            docs.Add(CreateDocument(source, normalizedSource, file.RelativePath, content, null, null, Classify(file.RelativePath, classify), index));
        }

        return docs;
    }

    private static IEnumerable<WebPortalDocEntry> IndexPackageSource(
        WebPortalDocsDefaults defaults,
        WebPortalDocsSourceSpec source,
        WebPortalDocsSource normalizedSource,
        PrivateGalleryDocument? gallery,
        List<string> warnings)
    {
        if (gallery is null)
        {
            AddSourceWarning(normalizedSource, warnings, $"Package source '{normalizedSource.Id}' requires privateGallery/private-gallery feed JSON.");
            return Array.Empty<WebPortalDocEntry>();
        }

        var moduleName = source.Module ?? source.RelationshipDefaults?.Module ?? source.Placement?.Module;
        var packages = gallery.Packages
            .Where(package => string.IsNullOrWhiteSpace(moduleName) || package.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (packages.Count == 0)
        {
            AddSourceWarning(normalizedSource, warnings, $"Package source '{normalizedSource.Id}' did not match any packages.");
            return Array.Empty<WebPortalDocEntry>();
        }

        var classify = MergeClassify(defaults.Classify, source.Classify);
        var docs = new List<WebPortalDocEntry>();
        foreach (var package in packages)
        {
            var assets = package.Module?.Documents ?? package.Versions.SelectMany(version => version.Module?.Documents ?? Enumerable.Empty<PrivateGalleryDocumentAsset>()).ToList();
            foreach (var asset in assets.OrderBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase))
            {
                var kind = string.IsNullOrWhiteSpace(asset.Kind) ? Classify(asset.Path, classify) : asset.Kind;
                var doc = CreateDocument(source, normalizedSource, asset.Path, null, package.WebUrl, null, kind, docs.Count);
                doc.Module = source.RelationshipDefaults?.Module ?? source.Placement?.Module ?? package.Module?.Name ?? package.Name;
                doc.Package = package.Name;
                doc.Version = package.Module?.Version ?? package.LatestVersion;
                doc.Title = string.IsNullOrWhiteSpace(asset.Title) ? ExtractTitle(null, asset.Path) : asset.Title!;
                docs.Add(doc);
            }
        }

        return docs;
    }

    private static async Task<IEnumerable<WebPortalDocEntry>> IndexGitHubSource(
        WebPortalDocsDefaults defaults,
        WebPortalDocsSourceSpec source,
        WebPortalDocsSource normalizedSource,
        HttpClient http,
        WebPortalDocsOptions options,
        string? token,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(source.Owner) || string.IsNullOrWhiteSpace(source.Repo))
        {
            AddSourceWarning(normalizedSource, warnings, $"GitHub source '{normalizedSource.Id}' requires owner and repo.");
            return Array.Empty<WebPortalDocEntry>();
        }

        var branch = source.Branch ?? defaults.Branch ?? "main";
        normalizedSource.RepositoryUrl = $"https://github.com/{source.Owner}/{source.Repo}";
        var include = EffectiveList(source.Include, defaults.Include, DefaultIncludePatterns());
        var exclude = EffectiveList(source.Exclude, defaults.Exclude, DefaultExcludePatterns());
        var classify = MergeClassify(defaults.Classify, source.Classify);
        var paths = await GetGitHubPaths(source.Owner, source.Repo, branch, http, include, exclude, token, normalizedSource, warnings).ConfigureAwait(false);
        var docs = new List<WebPortalDocEntry>();

        foreach (var path in paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var rawUrl = BuildGitHubRawUrl(source.Owner, source.Repo, branch, path);
            string? content = null;
            if (options.IncludeContent)
                content = await FetchText(rawUrl, http, options.MaxContentBytes, token, normalizedSource, warnings).ConfigureAwait(false);

            var blobUrl = $"https://github.com/{source.Owner}/{source.Repo}/blob/{Uri.EscapeDataString(branch)}/{EscapePath(path)}";
            docs.Add(CreateDocument(source, normalizedSource, path, content, blobUrl, rawUrl, Classify(path, classify), docs.Count));
        }

        return docs;
    }

    private static async Task<IEnumerable<WebPortalDocEntry>> IndexAzureDevOpsSource(
        WebPortalDocsDefaults defaults,
        WebPortalDocsSourceSpec source,
        WebPortalDocsSource normalizedSource,
        HttpClient http,
        WebPortalDocsOptions options,
        string? token,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(source.Organization) ||
            string.IsNullOrWhiteSpace(source.Project) ||
            string.IsNullOrWhiteSpace(source.Repository ?? source.Repo))
        {
            AddSourceWarning(normalizedSource, warnings, $"Azure DevOps source '{normalizedSource.Id}' requires organization, project, and repository.");
            return Array.Empty<WebPortalDocEntry>();
        }

        var repository = source.Repository ?? source.Repo!;
        var branch = source.Branch ?? defaults.Branch ?? "main";
        normalizedSource.RepositoryUrl = $"https://dev.azure.com/{source.Organization}/{source.Project}/_git/{repository}";
        var include = EffectiveList(source.Include, defaults.Include, DefaultIncludePatterns());
        var exclude = EffectiveList(source.Exclude, defaults.Exclude, DefaultExcludePatterns());
        var classify = MergeClassify(defaults.Classify, source.Classify);
        var paths = await GetAzureDevOpsPaths(source, repository, branch, http, include, exclude, token, normalizedSource, warnings).ConfigureAwait(false);
        var docs = new List<WebPortalDocEntry>();

        foreach (var path in paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var rawUrl = BuildAzureDevOpsItemUrl(source.Organization!, source.Project!, repository, branch, path, includeContent: true);
            string? content = null;
            if (options.IncludeContent)
                content = await FetchAzureDevOpsText(rawUrl, http, options.MaxContentBytes, source, token, normalizedSource, warnings).ConfigureAwait(false);

            var browseUrl = $"https://dev.azure.com/{source.Organization}/{source.Project}/_git/{Uri.EscapeDataString(repository)}?path=/{EscapePath(path)}&version=GB{Uri.EscapeDataString(branch)}&_a=contents";
            docs.Add(CreateDocument(source, normalizedSource, path, content, browseUrl, rawUrl, Classify(path, classify), docs.Count));
        }

        return docs;
    }

    private static IEnumerable<WebPortalDocEntry> UnsupportedSource(WebPortalDocsSourceSpec source, WebPortalDocsSource normalizedSource, List<string> warnings)
    {
        AddSourceWarning(normalizedSource, warnings, $"Portal docs source '{normalizedSource.Id}' has unsupported kind '{source.Kind}'.");
        return Array.Empty<WebPortalDocEntry>();
    }

    private static WebPortalDocEntry CreateDocument(
        WebPortalDocsSourceSpec source,
        WebPortalDocsSource normalizedSource,
        string relativePath,
        string? content,
        string? url,
        string? rawUrl,
        string kind,
        int index)
    {
        var module = source.RelationshipDefaults?.Module ?? source.Placement?.Module ?? source.Module;
        var tags = new List<string>();
        if (source.RelationshipDefaults?.Tags is { Count: > 0 })
            tags.AddRange(source.RelationshipDefaults.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)));

        if (!string.IsNullOrWhiteSpace(kind))
            tags.Add(kind);
        if (!string.IsNullOrWhiteSpace(module))
            tags.Add(module);

        return new WebPortalDocEntry
        {
            Id = MakeStableId(normalizedSource.Id, relativePath),
            Title = ExtractTitle(content, relativePath),
            Kind = string.IsNullOrWhiteSpace(kind) ? "docs" : kind,
            SourceId = normalizedSource.Id,
            SourceKind = normalizedSource.Kind,
            Path = NormalizePath(relativePath),
            Module = module,
            Package = source.RelationshipDefaults?.Package,
            Version = source.RelationshipDefaults?.Version,
            Command = source.RelationshipDefaults?.Command,
            Surface = source.Placement?.Surface,
            NavigationGroup = source.Placement?.NavigationGroup,
            Summary = ExtractSummary(content),
            Content = content,
            Url = url,
            RawUrl = rawUrl,
            Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Order = ResolveOrder(source.Placement, relativePath, index)
        };
    }

    private static WebPortalDocsSource CreateSource(WebPortalDocsSourceSpec source)
    {
        var id = string.IsNullOrWhiteSpace(source.Id)
            ? MakeSafeFragment($"{source.Kind}-{source.Title}-{source.Module}-{source.Path}-{source.Repo}")
            : source.Id!;
        return new WebPortalDocsSource
        {
            Id = id,
            Kind = source.Kind ?? "local",
            Title = source.Title,
            Description = source.Description,
            Module = source.Module ?? source.RelationshipDefaults?.Module ?? source.Placement?.Module
        };
    }

    private static WebPortalDocsSearchDocument BuildSearch(WebPortalDocsDocument document)
    {
        var search = new WebPortalDocsSearchDocument
        {
            GeneratedAtUtc = document.GeneratedAtUtc
        };

        foreach (var doc in document.Documents)
        {
            search.Entries.Add(new WebPortalDocsSearchEntry
            {
                Id = doc.Id,
                Kind = doc.Kind,
                Title = doc.Title,
                Summary = doc.Summary,
                Module = doc.Module,
                Version = doc.Version,
                Url = doc.Url,
                Tags = doc.Tags.ToList()
            });
        }

        return search;
    }

    private static List<string> EffectiveList(List<string>? source, List<string>? defaults, List<string> fallback)
    {
        if (source is { Count: > 0 })
            return source.Where(value => !string.IsNullOrWhiteSpace(value)).Select(NormalizePath).ToList();
        if (defaults is { Count: > 0 })
            return defaults.Where(value => !string.IsNullOrWhiteSpace(value)).Select(NormalizePath).ToList();
        return fallback;
    }

    private static Dictionary<string, string> MergeClassify(Dictionary<string, string>? defaults, Dictionary<string, string>? source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (defaults is not null)
        {
            foreach (var pair in defaults)
                result[NormalizePath(pair.Key)] = pair.Value;
        }

        if (source is not null)
        {
            foreach (var pair in source)
                result[NormalizePath(pair.Key)] = pair.Value;
        }

        return result;
    }

    private static string Classify(string path, Dictionary<string, string> classify)
    {
        var normalized = NormalizePath(path);
        foreach (var pair in classify)
        {
            if (GlobMatches(normalized, pair.Key))
                return pair.Value;
        }

        var fileName = Path.GetFileName(normalized);
        if (fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase) || fileName.Equals("README", StringComparison.OrdinalIgnoreCase))
            return "readme";
        if (fileName.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase))
            return "changelog";
        if (fileName.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("LICENSE.", StringComparison.OrdinalIgnoreCase))
            return "license";
        if (normalized.Contains("/examples/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("examples/", StringComparison.OrdinalIgnoreCase))
            return "example";
        if (normalized.Contains("/sop/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("sop/", StringComparison.OrdinalIgnoreCase))
            return "sop";
        return "docs";
    }

    private static string ExtractTitle(string? content, string path)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            var normalizedContent = TrimByteOrderMark(content);
            var frontMatterTitle = ExtractFrontMatterTitle(normalizedContent);
            if (!string.IsNullOrWhiteSpace(frontMatterTitle))
                return frontMatterTitle!;

            var inCodeBlock = false;
            foreach (var line in normalizedContent.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    inCodeBlock = !inCodeBlock;
                    continue;
                }

                if (inCodeBlock)
                    continue;

                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                    return trimmed[2..].Trim();
            }
        }

        var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(name)
            ? path
            : name.Replace('-', ' ').Replace('_', ' ');
    }

    private static string? ExtractFrontMatterTitle(string content)
    {
        using var reader = new StringReader(content);
        var firstLine = reader.ReadLine();
        if (!string.Equals(firstLine?.Trim(), "---", StringComparison.Ordinal))
            return null;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Equals("---", StringComparison.Ordinal))
                break;
            if (trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                return trimmed["title:".Length..].Trim().Trim('"', '\'');
        }

        return null;
    }

    private static string? ExtractSummary(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var normalizedContent = TrimByteOrderMark(content);
        var inCodeBlock = false;
        foreach (var line in normalizedContent.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
                continue;

            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.Equals("---", StringComparison.Ordinal))
                continue;
            if (trimmed.Contains(':', StringComparison.Ordinal) && trimmed.Length < 80)
                continue;
            return trimmed.Length <= 240 ? trimmed : trimmed[..240];
        }

        var collapsed = Regex.Replace(normalizedContent, @"\s+", " ").Trim();
        return collapsed.Length <= 240 ? collapsed : collapsed[..240];
    }

    private static string TrimByteOrderMark(string content)
        => content.Length > 0 && content[0] == '\uFEFF' ? content[1..] : content;

    private static int ResolveOrder(WebPortalDocsPlacement? placement, string path, int index)
    {
        if (placement?.Order is { Count: > 0 })
        {
            var normalized = NormalizePath(path);
            for (var orderIndex = 0; orderIndex < placement.Order.Count; orderIndex++)
            {
                if (NormalizePath(placement.Order[orderIndex]).Equals(normalized, StringComparison.OrdinalIgnoreCase))
                    return orderIndex;
            }
        }

        return 1000 + index;
    }

    private static string? ReadTextWithinLimit(string path, WebPortalDocsOptions options)
    {
        if (!options.IncludeContent)
            return null;

        using var stream = File.OpenRead(path);
        var max = Math.Max(1, options.MaxContentBytes);
        var buffer = new byte[(int)Math.Min(stream.Length, max)];
        var read = stream.Read(buffer, 0, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static HttpClient CreateHttpClient(WebPortalDocsOptions options, HttpMessageHandler? messageHandler)
    {
        var client = messageHandler is null ? new HttpClient() : new HttpClient(messageHandler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge.Web.PortalDocsIndex/1.0");
        return client;
    }

    private static string? ResolveToken(WebPortalDocsOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Token))
            return options.Token;
        return string.IsNullOrWhiteSpace(options.TokenEnvironmentVariable)
            ? null
            : Environment.GetEnvironmentVariable(options.TokenEnvironmentVariable);
    }

    private static bool IsIncluded(string path, List<string> include, List<string> exclude)
    {
        var normalized = NormalizePath(path);
        return include.Any(pattern => GlobMatches(normalized, pattern)) &&
               !exclude.Any(pattern => GlobMatches(normalized, pattern));
    }

    private static bool GlobMatches(string path, string pattern)
        => Regex.IsMatch(NormalizePath(path), "^" + GlobToRegex(NormalizePath(pattern)) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string GlobToRegex(string pattern)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (ch == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                    {
                        builder.Append("(?:.*/)?");
                        i += 2;
                    }
                    else
                    {
                        builder.Append(".*");
                        i++;
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }
            }
            else if (ch == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(ch.ToString()));
            }
        }

        return builder.ToString();
    }

    private static bool ContainsWildcard(string value)
        => value.Contains('*', StringComparison.Ordinal) || value.Contains('?', StringComparison.Ordinal);

    private static bool IsTextDocument(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mdx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
               string.IsNullOrWhiteSpace(extension) && Path.GetFileName(path).Equals("LICENSE", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> DefaultIncludePatterns()
        => new()
        {
            "README.md",
            "CHANGELOG.md",
            "LICENSE",
            "Docs/**/*.md",
            "docs/**/*.md",
            "Examples/**/*.md",
            "examples/**/*.md"
        };

    private static List<string> DefaultExcludePatterns()
        => new()
        {
            "**/bin/**",
            "**/obj/**",
            "**/_site/**",
            "**/.git/**"
        };

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

    private static string NormalizePath(string value)
        => value.Replace('\\', '/').TrimStart('/');

    private static string MakeStableId(string sourceId, string path)
    {
        var normalized = NormalizePath(path);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceId + "|" + normalized));
        return MakeSafeFragment(sourceId + "-" + normalized) + "-" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    private static string MakeSafeFragment(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
            else if (builder.Length == 0 || builder[^1] != '-')
                builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }

    private static void AddSourceWarning(WebPortalDocsSource source, List<string> warnings, string warning)
    {
        source.Status = "warning";
        source.Warnings.Add(warning);
        warnings.Add(warning);
    }

    private sealed class WebPortalDocsSourcesSpec
    {
        public WebPortalDocsDefaults Defaults { get; set; } = new();

        public List<WebPortalDocsSourceSpec> Sources { get; set; } = new();
    }

    private sealed class WebPortalDocsDefaults
    {
        public string? Branch { get; set; }

        public List<string> Include { get; set; } = new();

        public List<string> Exclude { get; set; } = new();

        public Dictionary<string, string> Classify { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class WebPortalDocsSourceSpec
    {
        public string? Id { get; set; }

        public string? Kind { get; set; }

        public string? Title { get; set; }

        public string? Description { get; set; }

        public string? Path { get; set; }

        public string? Section { get; set; }

        public string? Module { get; set; }

        public string? Owner { get; set; }

        public string? Repo { get; set; }

        public string? Organization { get; set; }

        public string? Project { get; set; }

        public string? Repository { get; set; }

        public string? Branch { get; set; }

        public string? Authentication { get; set; }

        public string? Auth { get; set; }

        public List<string> Include { get; set; } = new();

        public List<string> Exclude { get; set; } = new();

        public Dictionary<string, string> Classify { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public WebPortalDocsPlacement? Placement { get; set; }

        public WebPortalDocsRelationshipDefaults? RelationshipDefaults { get; set; }
    }

    private sealed class WebPortalDocsPlacement
    {
        public string? Surface { get; set; }

        public string? Module { get; set; }

        public string? NavigationGroup { get; set; }

        public List<string> Order { get; set; } = new();
    }

    private sealed class WebPortalDocsRelationshipDefaults
    {
        public string? Module { get; set; }

        public string? Package { get; set; }

        public string? Version { get; set; }

        public string? Command { get; set; }

        public List<string> Tags { get; set; } = new();
    }
}
