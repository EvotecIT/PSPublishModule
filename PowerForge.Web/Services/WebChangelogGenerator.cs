using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Generates a changelog data file.</summary>
public static class WebChangelogGenerator
{
    /// <summary>Generates a changelog JSON file from a local file or repository releases.</summary>
    /// <param name="options">Generation options.</param>
    /// <returns>Result payload describing the generated output.</returns>
    public static WebChangelogResult Generate(WebChangelogOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var warnings = new List<string>();
        var outputPath = Path.GetFullPath(options.OutputPath);
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
            Directory.CreateDirectory(outputDir);

        var items = new List<ChangelogItem>();
        var source = options.Source;

        if (options.Source is WebChangelogSource.Auto or WebChangelogSource.File)
        {
            var filePath = ResolveChangelogPath(options.ChangelogPath);
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                items = ParseChangelogFile(filePath);
                if (items.Count == 0)
                    warnings.Add($"Changelog file parsed but produced 0 releases: {filePath}");
                source = WebChangelogSource.File;
            }
            else if (options.Source == WebChangelogSource.File)
            {
                warnings.Add($"Changelog file not found: {options.ChangelogPath}");
            }
        }

        if (items.Count == 0 && options.Source is WebChangelogSource.Auto or WebChangelogSource.GitHub)
        {
            var repo = ResolveRepo(options, warnings);
            if (repo is null)
            {
                if (options.Source == WebChangelogSource.GitHub)
                    warnings.Add("Repository not specified for GitHub changelog source.");
            }
            else
            {
                items = ListGitHubReleases(repo.Value.Owner, repo.Value.Repo, options);
                if (items.Count == 0)
                    warnings.Add($"No GitHub releases found for {repo.Value.Owner}/{repo.Value.Repo}.");
                source = WebChangelogSource.GitHub;
            }
        }

        if (options.MaxReleases is { } max && max > 0 && items.Count > max)
            items = items.Take(max).ToList();

        var document = new ChangelogDocument
        {
            Title = string.IsNullOrWhiteSpace(options.Title) ? "Changelog" : options.Title!.Trim(),
            Source = source.ToString(),
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Items = items
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(document, WebJson.Options));

        return new WebChangelogResult
        {
            OutputPath = outputPath,
            ReleaseCount = items.Count,
            Source = source,
            Warnings = warnings.ToArray()
        };
    }

    private static string? ResolveChangelogPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        return full;
    }

    private static (string Owner, string Repo)? ResolveRepo(WebChangelogOptions options, List<string> warnings)
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

    private static List<ChangelogItem> ParseChangelogFile(string path)
    {
        var releases = new List<ChangelogItem>();
        var lines = File.ReadAllLines(path);
        var current = (ChangelogItem?)null;
        var sb = new StringBuilder();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var heading = ParseReleaseHeading(line);
            if (heading is not null)
            {
                if (current is not null)
                {
                    current.BodyMarkdown = sb.ToString().Trim();
                    releases.Add(current);
                }
                sb.Clear();
                current = new ChangelogItem
                {
                    Title = heading.Title,
                    Tag = heading.Tag,
                    PublishedAt = heading.PublishedAt,
                    BodyMarkdown = string.Empty
                };
                continue;
            }

            if (current is not null)
                sb.AppendLine(raw);
        }

        if (current is not null)
        {
            current.BodyMarkdown = sb.ToString().Trim();
            releases.Add(current);
        }

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
        DateTimeOffset? published = null;

        var tagMatch = Regex.Match(title, @"\[(?<tag>[^\]]+)\]");
        if (tagMatch.Success)
            tag = tagMatch.Groups["tag"].Value.Trim();
        else
        {
            var vMatch = Regex.Match(title, @"\b(v?\d+\.\d+[^ ]*)", RegexOptions.IgnoreCase);
            if (vMatch.Success)
                tag = vMatch.Groups[1].Value.Trim();
        }

        var dateMatch = Regex.Match(title, @"\b(?<date>\d{4}-\d{2}-\d{2})\b");
        if (dateMatch.Success && DateTimeOffset.TryParse(dateMatch.Groups["date"].Value, out var parsed))
            published = parsed;

        return new ReleaseHeading { Title = title, Tag = tag, PublishedAt = published };
    }

    private static List<ChangelogItem> ListGitHubReleases(string owner, string repo, WebChangelogOptions options)
    {
        var results = new List<ChangelogItem>();
        try
        {
            using var client = CreateGitHubClient(options.Token);
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases";
            var resp = client.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
                return results;

            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.TryGetProperty("draft", out var draftEl) && draftEl.ValueKind == JsonValueKind.True)
                    continue;

                var tag = rel.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
                var name = rel.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                var body = rel.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
                var published = rel.TryGetProperty("published_at", out var pubEl) && pubEl.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(pubEl.GetString(), out var dto)
                    ? dto
                    : (DateTimeOffset?)null;

                var item = new ChangelogItem
                {
                    Title = string.IsNullOrWhiteSpace(name) ? (tag ?? "Release") : name!,
                    Tag = tag,
                    PublishedAt = published,
                    BodyMarkdown = RewriteRelativeLinks(body ?? string.Empty, BuildRawBase(owner, repo, tag))
                };

                if (options.IncludeAssets && rel.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsEl.EnumerateArray())
                    {
                        var assetItem = new ChangelogAsset
                        {
                            Name = asset.TryGetProperty("name", out var an) ? an.GetString() ?? string.Empty : string.Empty,
                            DownloadUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() ?? string.Empty : string.Empty,
                            ContentType = asset.TryGetProperty("content_type", out var ct) ? ct.GetString() : null,
                            Size = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : (long?)null
                        };
                        item.Assets.Add(assetItem);
                    }
                }

                results.Add(item);
            }
        }
        catch
        {
            return results;
        }

        return results;
    }

    private static HttpClient CreateGitHubClient(string? token)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PowerForge.Web", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string? BuildRawBase(string owner, string repo, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        return $"https://raw.githubusercontent.com/{owner}/{repo}/{tag.Trim('/')}/";
    }

    private static string RewriteRelativeLinks(string markdown, string? baseUri)
    {
        if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(baseUri))
            return markdown;

        string Replace(Match m)
        {
            var url = m.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(url)) return m.Value;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("#", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return m.Value;
            try
            {
                var abs = new Uri(new Uri(baseUri, UriKind.Absolute), url).ToString();
                return m.Groups[1].Value + abs + m.Groups[3].Value;
            }
            catch
            {
                return m.Value;
            }
        }

        var pattern = @"(!?\[[^\]]*\]\()([^\)]+)(\))";
        return Regex.Replace(markdown, pattern, new MatchEvaluator(Replace));
    }

    private sealed class ChangelogDocument
    {
        public string Title { get; set; } = "Changelog";
        public string Source { get; set; } = "auto";
        public string GeneratedAtUtc { get; set; } = string.Empty;
        public List<ChangelogItem> Items { get; set; } = new();
    }

    private sealed class ChangelogItem
    {
        public string Title { get; set; } = string.Empty;
        public string? Tag { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        [JsonPropertyName("body_md")]
        public string? BodyMarkdown { get; set; }
        public List<ChangelogAsset> Assets { get; } = new();
    }

    private sealed class ChangelogAsset
    {
        public string Name { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long? Size { get; set; }
    }

    private sealed class ReleaseHeading
    {
        public string Title { get; set; } = string.Empty;
        public string? Tag { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
