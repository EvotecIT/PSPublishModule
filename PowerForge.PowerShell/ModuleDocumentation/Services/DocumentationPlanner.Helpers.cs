using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class DocumentationPlanner
{
    private void AddKind(List<(string Kind,string Path)> items, Request req, DocumentKind kind, bool includeEvenIfSelected = true)
    {
        var f = _Resolve(req, kind);
        if (f != null) items.Add(("FILE", f.FullName));
    }

    private FileInfo? _Resolve(Request req, DocumentKind kind)
        => _finder.ResolveDocument((req.RootBase, req.InternalsBase, new DeliveryOptions()), kind, req.PreferInternals);

    private static string ResolveToken(string? explicitToken)
        => explicitToken
           ?? Environment.GetEnvironmentVariable("PG_GITHUB_TOKEN")
           ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
           ?? Environment.GetEnvironmentVariable("PG_AZDO_PAT")
           ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_EXT_PAT")
           ?? string.Empty;

    private static IRepoClient? ResolveRepoClient(Request req, IRepoClient? clientOverride)
    {
        if (clientOverride is not null)
            return clientOverride;

        if (string.IsNullOrWhiteSpace(req.ProjectUri))
            return null;

        var info = RepoUrlParser.Parse(req.ProjectUri!);
        var token = ResolveToken(req.RepositoryToken);
        if (string.IsNullOrEmpty(token))
            token = TokenStore.GetToken(info.Host) ?? string.Empty;

        return RepoClientFactory.Create(info, token);
    }

    private static string? TryFetchFirst(IRepoClient client, string branch, string[] candidates)
    {
        foreach (var p in candidates)
        {
            var s = client.GetFileContent(p, branch);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return null;
    }

    private static DocumentItem MakeContentItem(Request req, string name, string content)
        => new DocumentItem { Title = BuildTitle(req, name), Kind = "FILE", Content = content };

    private static DocumentItem? TryCreateRemoteSingleFileItem(Request req, IRepoClient client, string branch)
    {
        if (string.IsNullOrWhiteSpace(req.SingleFile))
            return null;

        var remotePath = req.SingleFile!.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(remotePath))
            return null;

        var content = client.GetFileContent(remotePath, branch);
        if (string.IsNullOrEmpty(content))
            return null;

        var fileName = Path.GetFileName(remotePath);
        var normalizedContent = RepositoryContentNormalizer.RewriteRelativeUris(
            content!,
            RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch),
            RepositoryContentNormalizer.BuildBlobBase(req.ProjectUri, branch));

        return new DocumentItem
        {
            Title = BuildTitle(req, fileName),
            Kind = "FILE",
            Content = normalizedContent,
            FileName = fileName,
            Path = remotePath,
            Source = "Remote",
            BaseUri = RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch)
        };
    }

    private static string BuildTitle(Request req, string leaf)
        => !string.IsNullOrEmpty(req.TitleName) ? $"{req.TitleName} {req.TitleVersion} - {leaf}" : leaf;
    private static string StripAboutExtensions(string name)
    {
        var n = name;
        if (n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
        if (n.EndsWith(".help", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 5);
        return n;
    }

    private static bool IsCommunityFile(string lowerName)
    {
        if (string.IsNullOrEmpty(lowerName)) return false;
        var trimmed = lowerName.Replace('-', '_');
        return trimmed.Contains("contributing")
               || trimmed.Contains("security")
               || trimmed.Contains("support")
               || trimmed.Contains("code_of_conduct")
               || trimmed.Contains("code_of_codunduct");
    }

    private static string AboutToMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var sb = new System.Text.StringBuilder(content.Length + 256);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) { sb.AppendLine(); continue; }
            // Uppercase headings become H3 for readability
            bool looksHeading = trimmed.Length <= 40 && trimmed.ToUpperInvariant() == trimmed && trimmed.All(ch => !char.IsLetter(ch) || char.IsUpper(ch));
            if (looksHeading)
            {
                var title = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant());
                sb.Append("### ").Append(title).AppendLine();
            }
            else
            {
                sb.AppendLine(trimmed);
            }
        }
        return sb.ToString();
    }

    private static List<RepoRelease> NormalizeRepoReleases(IEnumerable<RepoRelease> releases, string? projectUri)
    {
        var normalized = new List<RepoRelease>();
        foreach (var release in releases.Where(r => r is not null && !r.IsDraft))
        {
            var normalizedTag = InferReleaseTag(release.Tag, release.Name);
            var normalizedName = NormalizeReleaseHeadingTitle(release.Name);
            var clone = new RepoRelease
            {
                Tag = normalizedTag,
                Name = normalizedName,
                Url = string.IsNullOrWhiteSpace(release.Url) ? BuildReleaseUrl(projectUri, normalizedTag) : release.Url,
                IsDraft = release.IsDraft,
                IsPrerelease = release.IsPrerelease || IsLikelyPrerelease(normalizedTag, normalizedName),
                PublishedAt = release.PublishedAt,
                Body = NormalizeReleaseBody(release.Body, projectUri, normalizedTag)
            };
            foreach (var asset in release.Assets)
            {
                clone.Assets.Add(new RepoReleaseAsset
                {
                    Name = asset.Name,
                    DownloadUrl = asset.DownloadUrl,
                    Size = asset.Size,
                    ContentType = asset.ContentType
                });
            }

            normalized.Add(clone);
        }

        return normalized;
    }

    private static List<RepoRelease> ParseChangelogReleases(string changelogContent)
    {
        var releases = new List<RepoRelease>();
        if (string.IsNullOrWhiteSpace(changelogContent))
            return releases;

        var lines = changelogContent.Replace("\r\n", "\n").Split('\n');
        RepoRelease? current = null;
        var body = new StringBuilder();
        var headingRegex = new Regex("^##\\s*(?<title>.+)$", RegexOptions.IgnoreCase);
        var tagRegex = new Regex("\\[(?<tag>[^\\]]+)\\]|\\b(v?\\d+\\.\\d+[^ ]*)", RegexOptions.IgnoreCase);
        var dateRegex = new Regex("\\b(?<date>\\d{4}-\\d{2}-\\d{2})\\b", RegexOptions.CultureInvariant);

        foreach (var rawLine in lines)
        {
            var trimmedLine = rawLine.TrimEnd();
            var headingMatch = headingRegex.Match(trimmedLine.Trim());
            if (headingMatch.Success)
            {
                if (current is not null)
                {
                    current.Body = body.ToString().Trim();
                    releases.Add(current);
                }

                body.Clear();
                var title = NormalizeReleaseHeadingTitle(headingMatch.Groups["title"].Value.Trim());
                var tag = string.Empty;
                var tagMatch = tagRegex.Match(title);
                if (tagMatch.Success)
                {
                    tag = NormalizeReleaseToken(tagMatch.Groups["tag"].Success
                        ? tagMatch.Groups["tag"].Value.Trim()
                        : tagMatch.Groups[2].Value.Trim());
                }

                DateTimeOffset? publishedAt = null;
                var dateMatch = dateRegex.Match(title);
                if (dateMatch.Success && DateTimeOffset.TryParse(dateMatch.Groups["date"].Value, out var parsedDate))
                    publishedAt = parsedDate;

                current = new RepoRelease
                {
                    Tag = tag,
                    Name = title,
                    Url = null,
                    IsPrerelease = IsLikelyPrerelease(tag, title),
                    PublishedAt = publishedAt
                };

                continue;
            }

            if (current is not null)
                body.AppendLine(rawLine);
        }

        if (current is not null)
        {
            current.Body = body.ToString().Trim();
            releases.Add(current);
        }

        return releases;
    }

    private static string BuildReleaseSummaryMarkdown(IEnumerable<RepoRelease> releases)
    {
        var releaseList = releases.ToList();
        if (releaseList.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("# Releases");
        sb.AppendLine();
        sb.AppendLine($"- Total releases: {releaseList.Count}");
        var latest = releaseList.OrderByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue).FirstOrDefault();
        if (latest is not null)
        {
            var latestLabel = string.IsNullOrWhiteSpace(latest.Name) ? latest.Tag : latest.Name;
            if (!string.IsNullOrWhiteSpace(latestLabel))
                sb.AppendLine($"- Latest: {latestLabel}");
        }
        sb.AppendLine();

        foreach (var r in releaseList)
        {
            var label = string.IsNullOrWhiteSpace(r.Name) ? r.Tag : r.Name;
            if (string.IsNullOrWhiteSpace(label))
                label = "Release";

            sb.Append("## ").Append(label);
            if (r.PublishedAt.HasValue)
                sb.Append(" (").Append(r.PublishedAt.Value.ToString("yyyy-MM-dd")).Append(')');
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(r.Body))
                sb.AppendLine(r.Body.Trim()).AppendLine();

            if (r.Assets.Count > 0)
            {
                sb.AppendLine("### Assets");
                foreach (var asset in r.Assets)
                {
                    sb.Append("- [").Append(asset.Name).Append("](").Append(asset.DownloadUrl).Append(')');
                    if (asset.Size.HasValue)
                        sb.Append(" (").Append(asset.Size.Value / 1024).Append(" KB)");
                    sb.AppendLine();
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static List<RepoRelease> GetNormalizedRepoReleases(Request req, IRepoClient? clientOverride = null)
    {
        if (string.IsNullOrWhiteSpace(req.ProjectUri) || (!req.Online && clientOverride is null))
            return new List<RepoRelease>();

        var client = ResolveRepoClient(req, clientOverride);
        var rels = client?.ListReleases() ?? new List<RepoRelease>();
        return rels.Count > 0 ? NormalizeRepoReleases(rels, req.ProjectUri) : new List<RepoRelease>();
    }

    private static List<RepoRelease> MergeReleaseMetadata(IEnumerable<RepoRelease> changelogReleases, IEnumerable<RepoRelease> repoReleases)
    {
        var merged = new List<RepoRelease>();
        var remainingRepoReleases = repoReleases.ToList();

        foreach (var changelogRelease in changelogReleases)
        {
            var match = FindMatchingRelease(changelogRelease, remainingRepoReleases);
            if (match is null)
            {
                merged.Add(changelogRelease);
                continue;
            }

            remainingRepoReleases.Remove(match);
            var mergedRelease = new RepoRelease
            {
                Tag = string.IsNullOrWhiteSpace(changelogRelease.Tag) ? match.Tag : changelogRelease.Tag,
                Name = string.IsNullOrWhiteSpace(changelogRelease.Name) ? match.Name : changelogRelease.Name,
                Url = string.IsNullOrWhiteSpace(changelogRelease.Url) ? match.Url : changelogRelease.Url,
                IsDraft = changelogRelease.IsDraft || match.IsDraft,
                IsPrerelease = changelogRelease.IsPrerelease || match.IsPrerelease,
                PublishedAt = changelogRelease.PublishedAt ?? match.PublishedAt,
                Body = string.IsNullOrWhiteSpace(changelogRelease.Body) ? match.Body : changelogRelease.Body
            };
            foreach (var asset in changelogRelease.Assets.Count > 0 ? changelogRelease.Assets : match.Assets)
            {
                mergedRelease.Assets.Add(new RepoReleaseAsset
                {
                    Name = asset.Name,
                    DownloadUrl = asset.DownloadUrl,
                    Size = asset.Size,
                    ContentType = asset.ContentType
                });
            }
            merged.Add(mergedRelease);
        }

        merged.AddRange(remainingRepoReleases);
        return merged;
    }

    private static RepoRelease? FindMatchingRelease(RepoRelease release, IEnumerable<RepoRelease> candidates)
    {
        var releaseKeys = EnumerateReleaseKeys(release).Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (releaseKeys.Count == 0)
            return null;

        return candidates.FirstOrDefault(candidate => EnumerateReleaseKeys(candidate).Any(k => releaseKeys.Contains(k)));
    }

    private static IEnumerable<string> EnumerateReleaseKeys(RepoRelease release)
    {
        var tag = NormalizeReleaseToken(release.Tag);
        var name = NormalizeReleaseHeadingTitle(release.Name);
        var version = ExtractReleaseVersionToken(tag) ?? ExtractReleaseVersionToken(name);

        if (!string.IsNullOrWhiteSpace(tag))
            yield return tag;
        if (!string.IsNullOrWhiteSpace(name))
            yield return name;
        if (!string.IsNullOrWhiteSpace(version))
        {
            yield return version!;
            yield return "v" + version;
        }
    }

    private static string NormalizeReleaseBody(string? body, string? projectUri, string? reference)
    {
        var normalized = ConvertSimpleHtmlLinksToMarkdown(body ?? string.Empty);
        normalized = RepositoryContentNormalizer.RewriteRelativeUris(
            normalized,
            RepositoryContentNormalizer.BuildRawBase(projectUri, reference),
            RepositoryContentNormalizer.BuildBlobBase(projectUri, reference));

        return LinkifyIssueReferences(normalized, projectUri);
    }

    private static string ConvertSimpleHtmlLinksToMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content ?? string.Empty;

        return Regex.Replace(
            content,
            @"<a\b[^>]*\bhref\s*=\s*(['""])(?<url>.*?)\1[^>]*>(?<text>.*?)</a>",
            match =>
            {
                var url = match.Groups["url"].Value.Trim();
                var text = Regex.Replace(match.Groups["text"].Value, "<.*?>", string.Empty, RegexOptions.Singleline).Trim();
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(text))
                    return match.Value;

                return $"[{System.Net.WebUtility.HtmlDecode(text)}]({url})";
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }

    private static string LinkifyIssueReferences(string content, string? projectUri)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(projectUri))
            return content ?? string.Empty;

        try
        {
            var info = RepoUrlParser.Parse(projectUri!);
            if (info.Host != RepoHost.GitHub || string.IsNullOrWhiteSpace(info.Owner) || string.IsNullOrWhiteSpace(info.Repo))
                return content;

            return Regex.Replace(
                content,
                @"(^|[\s(])#(?<id>\d+)\b",
                match => $"{match.Groups[1].Value}[#{match.Groups["id"].Value}](https://github.com/{info.Owner}/{info.Repo}/issues/{match.Groups["id"].Value})",
                RegexOptions.CultureInvariant | RegexOptions.Multiline);
        }
        catch
        {
            return content;
        }
    }

    private static string? BuildReleaseUrl(string? projectUri, string? tag)
    {
        if (string.IsNullOrWhiteSpace(projectUri) || string.IsNullOrWhiteSpace(tag))
            return null;

        var normalizedProjectUri = projectUri!.Trim();
        var normalizedTag = tag!.Trim();

        try
        {
            var info = RepoUrlParser.Parse(normalizedProjectUri);
            if (info.Host == RepoHost.GitHub && !string.IsNullOrWhiteSpace(info.Owner) && !string.IsNullOrWhiteSpace(info.Repo))
            {
                return $"https://github.com/{info.Owner}/{info.Repo}/releases/tag/{Uri.EscapeDataString(normalizedTag)}";
            }
        }
        catch
        {
            // Ignore invalid repository URLs and fall back to null.
        }

        return null;
    }

    private static bool IsLikelyPrerelease(string? tag, string? name)
    {
        var text = string.Join(" ", new[] { tag ?? string.Empty, name ?? string.Empty }).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("preview", StringComparison.OrdinalIgnoreCase)
               || text.Contains("prerelease", StringComparison.OrdinalIgnoreCase)
               || text.Contains("pre-release", StringComparison.OrdinalIgnoreCase)
               || text.Contains("alpha", StringComparison.OrdinalIgnoreCase)
               || text.Contains("beta", StringComparison.OrdinalIgnoreCase)
               || Regex.IsMatch(text, @"(?<![a-z])rc[\.-]?\d*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizeReleaseHeadingTitle(string? title)
    {
        var normalized = title ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        normalized = normalized.Trim();
        return normalized.TrimEnd(':', ';', ',', ')', ']');
    }

    private static string NormalizeReleaseToken(string? token)
    {
        var normalized = token ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        normalized = normalized.Trim();
        return normalized.TrimEnd(':', ';', ',', ')', ']');
    }

    private static string InferReleaseTag(string? tag, string? name)
    {
        var normalizedTag = NormalizeReleaseToken(tag);
        if (!string.IsNullOrWhiteSpace(normalizedTag))
            return normalizedTag;

        var version = ExtractReleaseVersionToken(name);
        if (string.IsNullOrWhiteSpace(version))
            return string.Empty;

        return "v" + version;
    }

    private static string? ExtractReleaseVersionToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = Regex.Match(text, @"\bv?(?<version>\d+(?:\.\d+)+(?:[-a-z0-9\.]+)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static string? BuildRawBase(string? projectUri, string? refName)
        => RepositoryContentNormalizer.BuildRawBase(projectUri, refName);

    private static string RewriteRelativeLinks(string markdown, string? baseUri)
        => RepositoryContentNormalizer.RewriteRelativeUris(markdown, baseUri);

    private static object? GetDeliveryValue(object? delivery, string name)
    {
        if (delivery == null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (delivery is System.Collections.IDictionary dictionary)
        {
            return dictionary.Contains(name) ? dictionary[name] : null;
        }

        var type = delivery.GetType();
        var directProperty = type.GetProperty(name);
        if (directProperty != null)
        {
            return directProperty.GetValue(delivery);
        }

        var propertiesProperty = type.GetProperty("Properties");
        var properties = propertiesProperty?.GetValue(delivery);
        if (properties != null)
        {
            var indexer = properties.GetType().GetProperty("Item", new[] { typeof(string) });
            var property = indexer?.GetValue(properties, new object[] { name });
            if (property != null)
            {
                return property.GetType().GetProperty("Value")?.GetValue(property);
            }
        }

        return null;
    }
}
