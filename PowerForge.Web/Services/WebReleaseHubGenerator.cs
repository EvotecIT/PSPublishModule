using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Generates release/download hub data from changelog or repository releases.</summary>
public static class WebReleaseHubGenerator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex ReleaseTagRegex = new(@"\[(?<tag>[^\]]+)\]", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ReleaseVersionRegex = new(@"\b(v?\d+\.\d+[^ ]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex ReleaseDateRegex = new(@"\b(?<date>\d{4}-\d{2}-\d{2})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex MarkdownLinkRegex = new(@"(!?\[[^\]]*\]\()([^\)]+)(\))", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly HttpClient GitHubClient = CreateGitHubClient();

    /// <summary>Generates a release hub JSON file.</summary>
    /// <param name="options">Generator options.</param>
    /// <returns>Result payload describing the generated output.</returns>
    public static WebReleaseHubResult Generate(WebReleaseHubOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var warnings = new List<string>();
        var baseDir = ResolveBaseDirectory(options.BaseDirectory);
        var outputPath = ResolvePath(options.OutputPath, baseDir, warnings, "OutputPath");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("OutputPath is invalid.", nameof(options));

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
            Directory.CreateDirectory(outputDir);

        var products = BuildInitialProductCatalog(options.Products);
        var releases = new List<WebReleaseHubRelease>();
        var source = options.Source;
        string? resolvedRepo = null;

        if (options.Source is WebChangelogSource.Auto or WebChangelogSource.File)
        {
            var parsedFromFile = TryLoadFromFileSources(options, baseDir, warnings, ref resolvedRepo, out var fileReleases);
            if (parsedFromFile)
            {
                releases = fileReleases;
                source = WebChangelogSource.File;
            }
            else if (options.Source == WebChangelogSource.File)
            {
                warnings.Add("release-hub source=file but no valid local source was found (releasesPath/changelogPath).");
            }
        }

        if (releases.Count == 0 && options.Source is WebChangelogSource.Auto or WebChangelogSource.GitHub)
        {
            var repo = ResolveRepo(options, warnings);
            if (repo is null)
            {
                if (options.Source == WebChangelogSource.GitHub)
                    warnings.Add("Repository not specified for GitHub release source.");
            }
            else
            {
                resolvedRepo = $"{repo.Value.Owner}/{repo.Value.Repo}";
                releases = ListGitHubReleases(repo.Value.Owner, repo.Value.Repo, options, warnings);
                source = WebChangelogSource.GitHub;
                if (releases.Count == 0)
                    warnings.Add($"No GitHub releases found for {resolvedRepo}.");
            }
        }

        releases = releases
            .Where(release => ShouldIncludeRelease(release, options))
            .OrderByDescending(ResolveReleaseTimestamp)
            .ThenByDescending(static release => release.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.MaxReleases is { } max && max > 0 && releases.Count > max)
            releases = releases.Take(max).ToList();

        ClassifyAssets(releases, options, products);
        MarkLatest(releases, out var latestStableTag, out var latestPrereleaseTag);

        var document = new WebReleaseHubDocument
        {
            Title = string.IsNullOrWhiteSpace(options.Title) ? "Release Hub" : options.Title!.Trim(),
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Source = source.ToString().ToLowerInvariant(),
            Repo = resolvedRepo,
            Latest = new WebReleaseHubLatest
            {
                StableTag = latestStableTag,
                PrereleaseTag = latestPrereleaseTag
            },
            Products = products.Values
                .OrderBy(product => product.Order ?? int.MaxValue)
                .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Releases = releases,
            Warnings = warnings
                .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(document, WebJson.Options), Encoding.UTF8);

        var assetCount = releases.Sum(static release => release.Assets.Count);
        return new WebReleaseHubResult
        {
            OutputPath = outputPath,
            ReleaseCount = releases.Count,
            AssetCount = assetCount,
            Source = source,
            Warnings = document.Warnings.ToArray()
        };
    }

    private static Dictionary<string, WebReleaseHubProduct> BuildInitialProductCatalog(IEnumerable<WebReleaseHubProductInput>? products)
    {
        var map = new Dictionary<string, WebReleaseHubProduct>(StringComparer.OrdinalIgnoreCase);
        if (products is null)
            return map;

        foreach (var product in products)
        {
            var id = NormalizeProductId(product?.Id);
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (map.ContainsKey(id))
                continue;

            map[id] = new WebReleaseHubProduct
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(product?.Name) ? id : product!.Name!.Trim(),
                Order = product?.Order
            };
        }

        return map;
    }

    private static void AddProductIfMissing(
        Dictionary<string, WebReleaseHubProduct> products,
        string productId,
        string? label)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return;
        if (products.ContainsKey(productId))
            return;

        products[productId] = new WebReleaseHubProduct
        {
            Id = productId,
            Name = string.IsNullOrWhiteSpace(label) ? productId : label.Trim()
        };
    }

    private static bool TryLoadFromFileSources(
        WebReleaseHubOptions options,
        string? baseDir,
        List<string> warnings,
        ref string? resolvedRepo,
        out List<WebReleaseHubRelease> releases)
    {
        releases = new List<WebReleaseHubRelease>();

        var releasesPath = ResolvePath(options.ReleasesPath ?? string.Empty, baseDir, warnings, "ReleasesPath");
        if (!string.IsNullOrWhiteSpace(releasesPath) && File.Exists(releasesPath))
        {
            if (TryParseReleasesJson(releasesPath, warnings, ref resolvedRepo, out var fromJson) && fromJson.Count > 0)
            {
                releases = fromJson;
                return true;
            }
        }

        var changelogPath = ResolvePath(options.ChangelogPath ?? string.Empty, baseDir, warnings, "ChangelogPath");
        if (!string.IsNullOrWhiteSpace(changelogPath) && File.Exists(changelogPath))
        {
            var fromMarkdown = ParseChangelogFile(changelogPath);
            if (fromMarkdown.Count > 0)
            {
                releases = fromMarkdown;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseReleasesJson(
        string path,
        List<string> warnings,
        ref string? resolvedRepo,
        out List<WebReleaseHubRelease> releases)
    {
        releases = new List<WebReleaseHubRelease>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                releases = ParseReleaseArray(root, owner: null, repo: null, sourceIsGitHubApi: true);
                return releases.Count > 0;
            }

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("repo", out var repoEl) && repoEl.ValueKind == JsonValueKind.String)
                resolvedRepo = NormalizeRepoText(repoEl.GetString());

            if (root.TryGetProperty("releases", out var releasesEl) && releasesEl.ValueKind == JsonValueKind.Array)
            {
                releases = ParseReleaseArray(releasesEl, owner: null, repo: null, sourceIsGitHubApi: false);
                return releases.Count > 0;
            }

            if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                releases = ParseReleaseArray(itemsEl, owner: null, repo: null, sourceIsGitHubApi: false);
                return releases.Count > 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse release JSON '{path}': {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static List<WebReleaseHubRelease> ParseReleaseArray(
        JsonElement arrayElement,
        string? owner,
        string? repo,
        bool sourceIsGitHubApi)
    {
        var releases = new List<WebReleaseHubRelease>();
        foreach (var element in arrayElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            var tag = ReadString(element, "tag_name", "tag");
            var title = ReadString(element, "name", "title");
            var body = ReadString(element, "body", "body_md");
            var url = ReadString(element, "html_url", "url", "releaseUrl", "release_url");
            var isDraft = ReadBool(element, "draft", "isDraft", "is_draft");
            var isPrerelease = ReadBool(element, "prerelease", "isPrerelease", "is_prerelease");
            var publishedAt = ReadDateTimeOffset(element, "published_at", "publishedAt", "published_at_utc");
            var createdAt = ReadDateTimeOffset(element, "created_at", "createdAt");

            var release = new WebReleaseHubRelease
            {
                Tag = tag,
                Title = string.IsNullOrWhiteSpace(title) ? (tag ?? "Release") : title,
                Url = url,
                IsDraft = isDraft,
                IsPrerelease = isPrerelease,
                PublishedAt = publishedAt,
                CreatedAt = createdAt,
                BodyMarkdown = sourceIsGitHubApi
                    ? RewriteRelativeLinks(body ?? string.Empty, BuildRawBase(owner, repo, tag))
                    : body
            };

            if (ReadArray(element, "assets").HasValue)
            {
                var assetsEl = ReadArray(element, "assets")!.Value;
                foreach (var assetEl in assetsEl.EnumerateArray())
                {
                    if (assetEl.ValueKind != JsonValueKind.Object)
                        continue;

                    var assetName = ReadString(assetEl, "name") ?? string.Empty;
                    var assetUrl = ReadString(assetEl, "browser_download_url", "downloadUrl", "download_url", "url") ?? string.Empty;
                    var asset = new WebReleaseHubAsset
                    {
                        Name = assetName,
                        DownloadUrl = assetUrl,
                        ContentType = ReadString(assetEl, "content_type", "contentType"),
                        Size = ReadLong(assetEl, "size"),
                        Sha256 = ReadString(assetEl, "sha256")
                    };
                    release.Assets.Add(asset);
                }
            }

            release.Id = BuildReleaseId(release, releases.Count + 1);
            releases.Add(release);
        }

        return releases;
    }

    private static List<WebReleaseHubRelease> ParseChangelogFile(string path)
    {
        var releases = new List<WebReleaseHubRelease>();
        var lines = File.ReadAllLines(path);
        WebReleaseHubRelease? current = null;
        var body = new StringBuilder();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var heading = ParseReleaseHeading(line);
            if (heading is not null)
            {
                if (current is not null)
                {
                    current.BodyMarkdown = body.ToString().Trim();
                    releases.Add(current);
                }

                body.Clear();
                current = new WebReleaseHubRelease
                {
                    Tag = heading.Tag,
                    Title = heading.Title,
                    PublishedAt = heading.PublishedAt,
                    IsDraft = false,
                    IsPrerelease = InferPrerelease(heading.Tag),
                    Id = string.Empty
                };
                continue;
            }

            if (current is not null)
                body.AppendLine(raw);
        }

        if (current is not null)
        {
            current.BodyMarkdown = body.ToString().Trim();
            releases.Add(current);
        }

        for (var i = 0; i < releases.Count; i++)
            releases[i].Id = BuildReleaseId(releases[i], i + 1);

        return releases;
    }

    private static ReleaseHeading? ParseReleaseHeading(string line)
    {
        if (!line.StartsWith("##", StringComparison.Ordinal))
            return null;

        var title = line.TrimStart('#').Trim();
        if (string.IsNullOrWhiteSpace(title))
            return null;

        string? tag = null;
        DateTimeOffset? publishedAt = null;

        var tagMatch = ReleaseTagRegex.Match(title);
        if (tagMatch.Success)
            tag = tagMatch.Groups["tag"].Value.Trim();
        else
        {
            var versionMatch = ReleaseVersionRegex.Match(title);
            if (versionMatch.Success)
                tag = versionMatch.Groups[1].Value.Trim();
        }

        var dateMatch = ReleaseDateRegex.Match(title);
        if (dateMatch.Success && DateTimeOffset.TryParse(dateMatch.Groups["date"].Value, out var parsedDate))
            publishedAt = parsedDate;

        return new ReleaseHeading
        {
            Title = title,
            Tag = tag,
            PublishedAt = publishedAt
        };
    }

    private static bool ShouldIncludeRelease(WebReleaseHubRelease release, WebReleaseHubOptions options)
    {
        if (!options.IncludeDraft && release.IsDraft)
            return false;
        if (!options.IncludePrerelease && release.IsPrerelease)
            return false;
        return true;
    }

    private static DateTimeOffset ResolveReleaseTimestamp(WebReleaseHubRelease release)
    {
        return release.PublishedAt
               ?? release.CreatedAt
               ?? DateTimeOffset.MinValue;
    }

    private static void MarkLatest(
        List<WebReleaseHubRelease> releases,
        out string? latestStableTag,
        out string? latestPrereleaseTag)
    {
        latestStableTag = null;
        latestPrereleaseTag = null;

        foreach (var release in releases)
        {
            release.IsLatestStable = false;
            release.IsLatestPrerelease = false;
        }

        var latestStable = releases.FirstOrDefault(static release => !release.IsPrerelease);
        if (latestStable is not null)
        {
            latestStable.IsLatestStable = true;
            latestStableTag = latestStable.Tag;
        }

        var latestPreview = releases.FirstOrDefault(static release => release.IsPrerelease);
        if (latestPreview is not null)
        {
            latestPreview.IsLatestPrerelease = true;
            latestPrereleaseTag = latestPreview.Tag;
        }
    }

    private static void ClassifyAssets(
        IEnumerable<WebReleaseHubRelease> releases,
        WebReleaseHubOptions options,
        Dictionary<string, WebReleaseHubProduct> products)
    {
        var defaultChannel = NormalizeToken(options.DefaultChannel) ?? "stable";
        var rules = CompileRules(options.AssetRules);

        foreach (var release in releases)
        {
            var releaseChannel = release.IsPrerelease ? "preview" : defaultChannel;
            for (var i = 0; i < release.Assets.Count; i++)
            {
                var asset = release.Assets[i];
                var fileName = asset.Name ?? string.Empty;
                var rule = rules.FirstOrDefault(candidate => candidate.Matches(fileName));

                var product = NormalizeProductId(rule?.Product);
                if (string.IsNullOrWhiteSpace(product))
                    product = "unknown";
                var channel = NormalizeToken(rule?.Channel) ?? releaseChannel;
                var platform = NormalizeToken(rule?.Platform) ?? DetectPlatform(fileName);
                var arch = NormalizeToken(rule?.Arch) ?? DetectArch(fileName);
                var kind = NormalizeToken(rule?.Kind) ?? DetectKind(fileName);

                asset.Product = product;
                asset.Channel = string.IsNullOrWhiteSpace(channel) ? releaseChannel : channel;
                asset.Platform = string.IsNullOrWhiteSpace(platform) ? "any" : platform;
                asset.Arch = string.IsNullOrWhiteSpace(arch) ? "any" : arch;
                asset.Kind = string.IsNullOrWhiteSpace(kind) ? "file" : kind;
                asset.Id = BuildAssetId(asset, i + 1);

                if (!string.Equals(product, "unknown", StringComparison.OrdinalIgnoreCase))
                    AddProductIfMissing(products, product, rule?.Label);
            }
        }
    }

    private static List<CompiledAssetRule> CompileRules(IEnumerable<WebReleaseHubAssetRuleInput>? rules)
    {
        var compiled = new List<CompiledAssetRule>();
        if (rules is null)
            return compiled;

        var index = 0;
        foreach (var rule in rules)
        {
            index++;
            if (rule is null)
                continue;

            var productId = NormalizeProductId(rule.Product);
            if (string.IsNullOrWhiteSpace(productId))
                continue;

            var globPatterns = (rule.Match ?? new List<string>())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(ConvertGlobToRegex)
                .ToArray();
            var contains = (rule.Contains ?? new List<string>())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .ToArray();

            Regex? explicitRegex = null;
            if (!string.IsNullOrWhiteSpace(rule.Regex))
            {
                try
                {
                    explicitRegex = new Regex(rule.Regex, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);
                }
                catch
                {
                    // Invalid regex should not break generation; the rule will behave as if regex is absent.
                }
            }

            compiled.Add(new CompiledAssetRule
            {
                Product = productId,
                Label = string.IsNullOrWhiteSpace(rule.Label) ? null : rule.Label.Trim(),
                Channel = NormalizeToken(rule.Channel),
                Platform = NormalizeToken(rule.Platform),
                Arch = NormalizeToken(rule.Arch),
                Kind = NormalizeToken(rule.Kind),
                MatchGlobs = globPatterns,
                MatchContains = contains,
                MatchRegex = explicitRegex,
                Priority = rule.Priority,
                Index = index
            });
        }

        return compiled
            .OrderBy(rule => rule.Priority ?? int.MaxValue)
            .ThenBy(rule => rule.Index)
            .ToList();
    }

    private static Regex ConvertGlobToRegex(string pattern)
    {
        var trimmed = pattern.Trim();
        var escaped = Regex.Escape(trimmed)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", @"[^\\/]*")
            .Replace(@"\?", ".");
        return new Regex("^" + escaped + "$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);
    }

    private static List<WebReleaseHubRelease> ListGitHubReleases(
        string owner,
        string repo,
        WebReleaseHubOptions options,
        List<string> warnings)
    {
        var results = new List<WebReleaseHubRelease>();
        var pageSize = Math.Clamp(options.PageSize <= 0 ? 100 : options.PageSize, 1, 100);
        var maxPages = options.MaxPages <= 0 ? 5 : options.MaxPages;
        var maxReleases = options.MaxReleases is > 0 ? options.MaxReleases.Value : int.MaxValue;

        for (var page = 1; page <= maxPages && results.Count < maxReleases; page++)
        {
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page={pageSize}&page={page}";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(options.Token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);

                using var response = GitHubClient.Send(request);
                if (!response.IsSuccessStatusCode)
                {
                    if (ShouldRetryGitHubWithoutAuthorization(request, response))
                    {
                        using var retryRequest = CloneRequestWithoutAuthorization(request);
                        using var retryResponse = GitHubClient.Send(retryRequest);
                        if (retryResponse.IsSuccessStatusCode)
                        {
                            warnings.Add($"GitHub release fetch retried without Authorization after token auth failed for {owner}/{repo}.");
                            using var retryStream = retryResponse.Content.ReadAsStream();
                            using var retryDoc = JsonDocument.Parse(retryStream);
                            if (retryDoc.RootElement.ValueKind != JsonValueKind.Array)
                                break;

                            var retryPageItems = ParseReleaseArray(retryDoc.RootElement, owner, repo, sourceIsGitHubApi: true);
                            if (retryPageItems.Count == 0)
                                break;

                            results.AddRange(retryPageItems);

                            if (retryPageItems.Count < pageSize)
                                break;

                            continue;
                        }
                    }

                    warnings.Add($"GitHub release fetch failed ({(int)response.StatusCode}) for {owner}/{repo}.");
                    break;
                }

                using var stream = response.Content.ReadAsStream();
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    break;

                var pageItems = ParseReleaseArray(doc.RootElement, owner, repo, sourceIsGitHubApi: true);
                if (pageItems.Count == 0)
                    break;

                results.AddRange(pageItems);

                if (pageItems.Count < pageSize)
                    break;
            }
            catch (Exception ex)
            {
                warnings.Add($"GitHub release fetch failed for {owner}/{repo}: {ex.GetType().Name}: {ex.Message}");
                Trace.TraceWarning($"GitHub release fetch failed for {owner}/{repo}: {ex.GetType().Name}: {ex.Message}");
                break;
            }
        }

        return results;
    }

    private static bool ShouldRetryGitHubWithoutAuthorization(HttpRequestMessage request, HttpResponseMessage response)
    {
        if (request.Headers.Authorization is null)
            return false;

        if (response.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden)
            return false;

        return request.RequestUri?.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static HttpRequestMessage CloneRequestWithoutAuthorization(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                continue;

            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private static HttpClient CreateGitHubClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PowerForge.Web", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static (string Owner, string Repo)? ResolveRepo(WebReleaseHubOptions options, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(options.Repo))
        {
            var parts = options.Repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return (parts[0], parts[1]);
            warnings.Add($"Invalid repo value (expected owner/repo): {options.Repo}");
        }

        if (!string.IsNullOrWhiteSpace(options.RepoUrl) && Uri.TryCreate(options.RepoUrl, UriKind.Absolute, out var uri))
        {
            if (uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2)
                    return (segments[0], segments[1]);
            }
            warnings.Add($"Unsupported repo URL: {options.RepoUrl}");
        }

        return null;
    }

    private static string? ResolveBaseDirectory(string? baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            return null;
        try
        {
            return Path.GetFullPath(baseDir.Trim().Trim('"'));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePath(string path, string? baseDir, List<string> warnings, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var trimmed = path.Trim().Trim('"');
        try
        {
            var resolved = Path.IsPathRooted(trimmed) || string.IsNullOrWhiteSpace(baseDir)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(baseDir, trimmed));
            return resolved;
        }
        catch (Exception ex)
        {
            warnings.Add($"{label} could not be resolved: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string? BuildRawBase(string? owner, string? repo, string? tag)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(tag))
            return null;
        return $"https://raw.githubusercontent.com/{owner}/{repo}/{tag.Trim('/')}/";
    }

    private static string RewriteRelativeLinks(string markdown, string? baseUri)
    {
        if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(baseUri))
            return markdown;

        string Replace(Match match)
        {
            var url = match.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(url))
                return match.Value;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("#", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return match.Value;

            try
            {
                var absolute = new Uri(new Uri(baseUri, UriKind.Absolute), url).ToString();
                return match.Groups[1].Value + absolute + match.Groups[3].Value;
            }
            catch
            {
                return match.Value;
            }
        }

        return MarkdownLinkRegex.Replace(markdown, new MatchEvaluator(Replace));
    }

    private static string BuildReleaseId(WebReleaseHubRelease release, int fallbackIndex)
    {
        var basis = release.Tag;
        if (string.IsNullOrWhiteSpace(basis))
            basis = release.Title;
        if (string.IsNullOrWhiteSpace(basis))
            basis = "release-" + fallbackIndex;

        return SlugifyToken(basis);
    }

    private static string BuildAssetId(WebReleaseHubAsset asset, int fallbackIndex)
    {
        var basis = string.IsNullOrWhiteSpace(asset.Name) ? "asset-" + fallbackIndex : asset.Name;
        return SlugifyToken(basis);
    }

    private static string SlugifyToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "item";

        var sb = new StringBuilder(value.Length);
        var previousDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            var allowed = (ch is >= 'a' and <= 'z') || (ch is >= '0' and <= '9');
            if (allowed)
            {
                sb.Append(ch);
                previousDash = false;
                continue;
            }

            if (!previousDash)
            {
                sb.Append('-');
                previousDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeProductId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim().ToLowerInvariant();
        var sb = new StringBuilder(trimmed.Length);
        var previousDot = false;
        foreach (var ch in trimmed)
        {
            var allowed = (ch is >= 'a' and <= 'z') || (ch is >= '0' and <= '9') || ch is '-' or '_' or '.';
            if (!allowed)
                continue;

            if (ch == '.')
            {
                if (previousDot)
                    continue;
                previousDot = true;
                sb.Append(ch);
                continue;
            }

            previousDot = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim('.');
    }

    private static string DetectPlatform(string fileName)
    {
        var normalized = fileName.ToLowerInvariant();
        if (normalized.Contains("win", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("windows", StringComparison.OrdinalIgnoreCase))
            return "windows";
        if (normalized.Contains("linux", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ubuntu", StringComparison.OrdinalIgnoreCase))
            return "linux";
        if (normalized.Contains("osx", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("macos", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("darwin", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("mac", StringComparison.OrdinalIgnoreCase))
            return "macos";
        return "any";
    }

    private static string DetectArch(string fileName)
    {
        var normalized = fileName.ToLowerInvariant();
        if (normalized.Contains("arm64", StringComparison.OrdinalIgnoreCase) || normalized.Contains("aarch64", StringComparison.OrdinalIgnoreCase))
            return "arm64";
        if (normalized.Contains("x64", StringComparison.OrdinalIgnoreCase) || normalized.Contains("amd64", StringComparison.OrdinalIgnoreCase))
            return "x64";
        if (normalized.Contains("x86", StringComparison.OrdinalIgnoreCase) || normalized.Contains("i386", StringComparison.OrdinalIgnoreCase))
            return "x86";
        return "any";
    }

    private static string DetectKind(string fileName)
    {
        var normalized = fileName.ToLowerInvariant();
        if (normalized.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            return "tar.gz";
        if (normalized.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            return "tar.gz";
        if (normalized.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return "zip";
        if (normalized.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            return "msi";
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return "exe";
        if (normalized.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            return "nupkg";
        if (normalized.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
            return "pkg";
        if (normalized.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            return "tar";
        return "file";
    }

    private static bool InferPrerelease(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return false;
        var normalized = tag.ToLowerInvariant();
        return normalized.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("alpha", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("rc", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeRepoText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Trim('/');
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind != JsonValueKind.String)
                continue;
            var parsed = value.GetString();
            if (!string.IsNullOrWhiteSpace(parsed))
                return parsed;
        }

        return null;
    }

    private static bool ReadBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.True)
                return true;
            if (value.ValueKind == JsonValueKind.False)
                return false;
            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        return false;
    }

    private static long? ReadLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsedLong))
                return parsedLong;
            if (value.ValueKind == JsonValueKind.String &&
                long.TryParse(value.GetString(), out var parsedText))
                return parsedText;
        }

        return null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind != JsonValueKind.String)
                continue;
            if (DateTimeOffset.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static JsonElement? ReadArray(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Array)
                return value;
        }

        return null;
    }

    private sealed class ReleaseHeading
    {
        public string Title { get; set; } = string.Empty;
        public string? Tag { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class CompiledAssetRule
    {
        public string Product { get; init; } = string.Empty;
        public string? Label { get; init; }
        public string? Channel { get; init; }
        public string? Platform { get; init; }
        public string? Arch { get; init; }
        public string? Kind { get; init; }
        public Regex[] MatchGlobs { get; init; } = Array.Empty<Regex>();
        public string[] MatchContains { get; init; } = Array.Empty<string>();
        public Regex? MatchRegex { get; init; }
        public int? Priority { get; init; }
        public int Index { get; init; }

        public bool Matches(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            if (MatchGlobs.Length == 0 &&
                MatchContains.Length == 0 &&
                MatchRegex is null)
                return false;

            if (MatchGlobs.Any(regex => regex.IsMatch(fileName)))
                return true;

            if (MatchContains.Any(token => fileName.Contains(token, StringComparison.OrdinalIgnoreCase)))
                return true;

            return MatchRegex?.IsMatch(fileName) == true;
        }
    }
}
