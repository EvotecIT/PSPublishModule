using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal static class ReleaseHubRenderer
{
    private const string DefaultDataPath = "release_hub";
    private static readonly Regex HeadingWithIdRegex = new(
        "<h(?<level>[1-6])(?<attrs>[^>]*)>(?<text>.*?)</h\\1>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex IdAttributeRegex = new(
        "\\sid\\s*=\\s*([\"'])(?<id>[^\"']+)\\1",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FragmentLinkRegex = new(
        "(?<prefix><a\\b[^>]*\\bhref\\s*=\\s*)(?<quote>[\"'])#(?<target>[^\"'#]+)(\\k<quote>)(?<suffix>[^>]*>)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(
        "<[^>]+>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex AnchorWithHrefRegex = new(
        "<a(?<attrs>\\b[^>]*?\\bhref\\s*=\\s*(?<quote>[\"'])(?<href>https?://[^\"']+)\\k<quote>[^>]*)>(?<text>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ClassAttributeRegex = new(
        "\\bclass\\s*=\\s*(?<quote>[\"'])(?<value>[^\"']*)(\\k<quote>)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GithubUserMentionRegex = new(
        "(?<![\\w/])@(?<user>[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static string RenderReleaseButton(
        IReadOnlyDictionary<string, object?> data,
        MarkdownSpec? markdown,
        string? product,
        string? channel = null,
        string? platform = null,
        string? arch = null,
        string? kind = null,
        string? label = null,
        string? cssClass = null,
        string? dataPath = null)
    {
        if (string.IsNullOrWhiteSpace(product))
            return string.Empty;

        var document = TryReadDocument(data, markdown, dataPath);
        if (document is null)
            return string.Empty;

        var productFilter = NormalizeFilter(product);
        if (string.IsNullOrWhiteSpace(productFilter))
            return string.Empty;

        var channelFilter = NormalizeFilter(channel);
        var platformFilter = NormalizeFilter(platform);
        var archFilter = NormalizeFilter(arch);
        var kindFilter = NormalizeFilter(kind);

        var matches = FindAssets(document, productFilter, channelFilter, platformFilter, archFilter, kindFilter);
        if (matches.Count == 0)
            return string.Empty;

        ReleaseHubAssetMatch chosen;
        if (string.IsNullOrWhiteSpace(channelFilter))
        {
            chosen = matches.FirstOrDefault(static item => string.Equals(item.EffectiveChannel, "stable", StringComparison.OrdinalIgnoreCase))
                     ?? matches[0];
        }
        else
        {
            chosen = matches[0];
        }

        var resolvedLabel = string.IsNullOrWhiteSpace(label)
            ? ResolveDefaultButtonLabel(document, productFilter)
            : label.Trim();
        var classes = JoinClasses("pf-release-button", cssClass);

        var sb = new StringBuilder();
        sb.Append("<a class=\"")
            .Append(Html(classes))
            .Append("\" href=\"")
            .Append(Html(chosen.Asset.DownloadUrl))
            .Append("\" data-release-product=\"")
            .Append(Html(chosen.Asset.Product))
            .Append("\" data-release-channel=\"")
            .Append(Html(chosen.EffectiveChannel))
            .Append("\" data-release-platform=\"")
            .Append(Html(chosen.Asset.Platform))
            .Append("\" data-release-arch=\"")
            .Append(Html(chosen.Asset.Arch))
            .Append("\" data-release-kind=\"")
            .Append(Html(chosen.Asset.Kind))
            .Append("\" data-release-tag=\"")
            .Append(Html(chosen.Release.Tag))
            .Append("\">")
            .Append(Html(resolvedLabel))
            .Append("</a>");

        return sb.ToString();
    }

    internal static string RenderReleaseButtons(
        IReadOnlyDictionary<string, object?> data,
        MarkdownSpec? markdown,
        string? product,
        string? channel = null,
        int limit = 0,
        string? groupBy = null,
        string? platform = null,
        string? arch = null,
        string? kind = null,
        string? cssClass = null,
        string? dataPath = null)
    {
        if (string.IsNullOrWhiteSpace(product))
            return string.Empty;

        var document = TryReadDocument(data, markdown, dataPath);
        if (document is null)
            return string.Empty;

        var productFilter = NormalizeFilter(product);
        var channelFilter = NormalizeFilter(channel);
        var platformFilter = NormalizeFilter(platform);
        var archFilter = NormalizeFilter(arch);
        var kindFilter = NormalizeFilter(kind);

        var matches = FindAssets(document, productFilter, channelFilter, platformFilter, archFilter, kindFilter);
        if (matches.Count == 0)
            return string.Empty;

        var take = limit > 0 ? Math.Clamp(limit, 1, 500) : matches.Count;
        if (matches.Count > take)
            matches = matches.Take(take).ToList();

        var mode = NormalizeGrouping(groupBy);
        var groups = GroupAssets(matches, mode);
        var classes = JoinClasses("pf-release-buttons", cssClass);

        var sb = new StringBuilder();
        sb.Append("<div class=\"")
            .Append(Html(classes))
            .Append("\" data-release-product=\"")
            .Append(Html(productFilter ?? "all"))
            .Append("\">");

        foreach (var group in groups)
        {
            if (mode != ReleaseButtonsGrouping.None)
            {
                sb.Append("<section class=\"pf-release-group\" data-release-group=\"")
                    .Append(Html(group.Key))
                    .Append("\">");
                sb.Append("<h4 class=\"pf-release-group-title\">")
                    .Append(Html(FormatGroupingLabel(mode, group.Key)))
                    .Append("</h4>");
            }

            sb.Append("<div class=\"pf-release-buttons-list\">");
            foreach (var item in group.Items)
            {
                sb.Append("<a class=\"pf-release-button pf-release-button--item\" href=\"")
                    .Append(Html(item.Asset.DownloadUrl))
                    .Append("\" data-release-tag=\"")
                    .Append(Html(item.Release.Tag))
                    .Append("\" data-release-channel=\"")
                    .Append(Html(item.EffectiveChannel))
                    .Append("\" data-release-platform=\"")
                    .Append(Html(item.Asset.Platform))
                    .Append("\" data-release-arch=\"")
                    .Append(Html(item.Asset.Arch))
                    .Append("\" data-release-kind=\"")
                    .Append(Html(item.Asset.Kind))
                    .Append("\">");

                var thumbnailUrl = ResolveAssetThumbnailUrl(item.Asset);
                if (!string.IsNullOrWhiteSpace(thumbnailUrl))
                {
                    sb.Append("<span class=\"pf-release-button-media\">")
                        .Append("<img class=\"pf-release-button-thumb\" src=\"")
                        .Append(Html(thumbnailUrl))
                        .Append("\" alt=\"\" loading=\"lazy\" decoding=\"async\" />")
                        .Append("</span>");
                }

                sb.Append("<span class=\"pf-release-button-content\">");
                sb.Append("<span class=\"pf-release-button-label\">")
                    .Append(Html(string.IsNullOrWhiteSpace(item.Asset.Name) ? "Download" : item.Asset.Name))
                    .Append("</span>");

                if (!string.IsNullOrWhiteSpace(item.Release.Tag))
                {
                    sb.Append("<span class=\"pf-release-button-meta\">")
                        .Append(Html(item.Release.Tag))
                        .Append("</span>");
                }

                AppendAssetBadges(sb, item.Asset, "pf-release-button-badges", "pf-release-badge");
                sb.Append("</span>");
                sb.Append("</a>");
            }
            sb.Append("</div>");

            if (mode != ReleaseButtonsGrouping.None)
                sb.Append("</section>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    internal static string RenderReleaseChangelog(
        IReadOnlyDictionary<string, object?> data,
        MarkdownSpec? markdown,
        string? product = null,
        int limit = 20,
        bool includePreview = true,
        string? cssClass = null,
        string? dataPath = null)
    {
        var document = TryReadDocument(data, markdown, dataPath);
        if (document is null)
            return string.Empty;

        var productFilter = NormalizeFilter(product);
        var take = limit > 0 ? Math.Clamp(limit, 1, 2000) : int.MaxValue;

        var releases = new List<ReleaseHubReleaseView>();
        foreach (var release in document.Releases)
        {
            if (!includePreview && release.IsPrerelease)
                continue;

            if (!string.IsNullOrWhiteSpace(productFilter))
            {
                var hasProductAsset = release.Assets.Any(a => string.Equals(a.Product, productFilter, StringComparison.OrdinalIgnoreCase));
                if (!hasProductAsset)
                    continue;
            }

            releases.Add(release);
            if (releases.Count >= take)
                break;
        }

        if (releases.Count == 0)
            return "<div class=\"pf-release-empty\">No releases found.</div>";

        var classes = JoinClasses("pf-release-changelog", cssClass);
        var sb = new StringBuilder();
        sb.Append("<section class=\"")
            .Append(Html(classes))
            .Append("\">");

        foreach (var release in releases)
        {
            var assets = string.IsNullOrWhiteSpace(productFilter)
                ? release.Assets
                : release.Assets.Where(a => string.Equals(a.Product, productFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            sb.Append("<article class=\"pf-release-entry\" data-release-tag=\"")
                .Append(Html(release.Tag))
                .Append("\">");

            sb.Append("<header class=\"pf-release-entry-header\">");
            if (!string.IsNullOrWhiteSpace(release.Url))
            {
                sb.Append("<h3><a href=\"")
                    .Append(Html(release.Url))
                    .Append("\">")
                    .Append(Html(ResolveReleaseTitle(release)))
                    .Append("</a></h3>");
            }
            else
            {
                sb.Append("<h3>")
                    .Append(Html(ResolveReleaseTitle(release)))
                    .Append("</h3>");
            }

            sb.Append("<p class=\"pf-release-meta\">");
            if (release.PublishedAt.HasValue)
            {
                var iso = release.PublishedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                sb.Append("<time datetime=\"")
                    .Append(iso)
                    .Append("\">")
                    .Append(iso)
                    .Append("</time>");
            }
            if (release.IsPrerelease)
                sb.Append("<span class=\"pf-release-chip pf-release-chip--preview\">Preview</span>");
            if (release.IsLatestStable)
                sb.Append("<span class=\"pf-release-chip pf-release-chip--latest\">Latest Stable</span>");
            if (release.IsLatestPrerelease)
                sb.Append("<span class=\"pf-release-chip pf-release-chip--latest-preview\">Latest Preview</span>");
            sb.Append("</p>");
            sb.Append("</header>");

            if (!string.IsNullOrWhiteSpace(release.BodyHtml))
            {
                sb.Append("<div class=\"pf-release-body\">")
                    .Append(release.BodyHtml)
                    .Append("</div>");
            }

            if (assets.Count > 0)
            {
                sb.Append("<ul class=\"pf-release-assets\">");
                foreach (var asset in assets)
                {
                    sb.Append("<li class=\"pf-release-asset-item\">");
                    var thumbnailUrl = ResolveAssetThumbnailUrl(asset);
                    if (!string.IsNullOrWhiteSpace(thumbnailUrl))
                    {
                        sb.Append("<img class=\"pf-release-asset-thumb\" src=\"")
                            .Append(Html(thumbnailUrl))
                            .Append("\" alt=\"\" loading=\"lazy\" decoding=\"async\" />");
                    }

                    sb.Append("<a class=\"pf-release-asset-link\" href=\"")
                        .Append(Html(asset.DownloadUrl))
                        .Append("\">")
                        .Append(Html(asset.Name))
                        .Append("</a>");

                    var meta = BuildAssetMeta(asset);
                    if (!string.IsNullOrWhiteSpace(meta))
                    {
                        sb.Append("<span class=\"pf-release-asset-meta\">")
                            .Append(Html(meta))
                            .Append("</span>");
                    }

                    AppendAssetBadges(sb, asset, "pf-release-asset-badges", "pf-release-asset-badge");
                    sb.Append("</li>");
                }
                sb.Append("</ul>");
            }

            sb.Append("</article>");
        }

        sb.Append("</section>");
        return sb.ToString();
    }

    private static ReleaseHubDocumentView? TryReadDocument(
        IReadOnlyDictionary<string, object?> data,
        MarkdownSpec? markdown,
        string? dataPath)
    {
        if (data is null || data.Count == 0)
            return null;

        var root = ResolveHubRoot(data, dataPath);
        if (root is null)
            return null;

        var view = new ReleaseHubDocumentView();

        if (TryReadList(root, "products", out var productValues))
        {
            foreach (var productValue in productValues)
            {
                if (!TryReadMap(productValue, out var map))
                    continue;

                var id = NormalizeFilter(ReadString(map, "id", "product"));
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var name = ReadString(map, "name", "label");
                if (string.IsNullOrWhiteSpace(name))
                    name = id;
                view.ProductNames[id] = name!;
            }
        }

        if (!TryReadList(root, "releases", "items", out var releaseValues))
            return view;

        foreach (var releaseValue in releaseValues)
        {
            if (!TryReadMap(releaseValue, out var releaseMap))
                continue;

            var release = new ReleaseHubReleaseView
            {
                Tag = ReadString(releaseMap, "tag", "tag_name") ?? string.Empty,
                Title = ReadString(releaseMap, "title", "name") ?? string.Empty,
                Url = ReadString(releaseMap, "url", "html_url") ?? string.Empty,
                PublishedAt = ReadDate(releaseMap, "publishedAt", "published_at", "publishedAtUtc", "published_at_utc"),
                CreatedAt = ReadDate(releaseMap, "createdAt", "created_at"),
                IsPrerelease = ReadBool(releaseMap, defaultValue: false, "isPrerelease", "is_prerelease", "prerelease"),
                IsDraft = ReadBool(releaseMap, defaultValue: false, "isDraft", "is_draft", "draft"),
                IsLatestStable = ReadBool(releaseMap, defaultValue: false, "isLatestStable", "is_latest_stable"),
                IsLatestPrerelease = ReadBool(releaseMap, defaultValue: false, "isLatestPrerelease", "is_latest_prerelease")
            };

            var bodyHtml = ReadString(releaseMap, "body", "bodyHtml", "body_html");
            if (string.IsNullOrWhiteSpace(bodyHtml))
            {
                var bodyMarkdown = ReadString(releaseMap, "body_md", "bodyMarkdown", "body_markdown");
                if (!string.IsNullOrWhiteSpace(bodyMarkdown))
                    bodyHtml = MarkdownRenderer.RenderToHtml(bodyMarkdown, markdown);
            }
            release.BodyHtml = PolishReleaseBodyHtml(NamespaceReleaseBodyHtml(bodyHtml ?? string.Empty, release.Tag));

            if (TryReadList(releaseMap, "assets", out var assetValues))
            {
                foreach (var assetValue in assetValues)
                {
                    if (!TryReadMap(assetValue, out var assetMap))
                        continue;

                    var downloadUrl = ReadString(assetMap, "downloadUrl", "download_url", "browser_download_url", "url");
                    if (string.IsNullOrWhiteSpace(downloadUrl))
                        continue;

                    var product = NormalizeFilter(ReadString(assetMap, "product", "id")) ?? "unknown";
                    var name = ReadString(assetMap, "name") ?? "asset";
                    var channel = NormalizeFilter(ReadString(assetMap, "channel")) ?? (release.IsPrerelease ? "preview" : "stable");
                    var platform = NormalizeFilter(ReadString(assetMap, "platform")) ?? "any";
                    var arch = NormalizeFilter(ReadString(assetMap, "arch")) ?? "any";
                    var kind = NormalizeFilter(ReadString(assetMap, "kind")) ?? "file";

                    release.Assets.Add(new ReleaseHubAssetView
                    {
                        Name = name,
                        DownloadUrl = downloadUrl!,
                        Product = product,
                        Channel = channel,
                        Platform = platform,
                        Arch = arch,
                        Kind = kind,
                        ThumbnailUrl = ReadString(
                            assetMap,
                            "thumbnailUrl",
                            "thumbnail_url",
                            "thumbUrl",
                            "thumb_url",
                            "imageUrl",
                            "image_url",
                            "screenshotUrl",
                            "screenshot_url",
                            "iconUrl",
                            "icon_url"),
                        Size = ReadLong(assetMap, "size")
                    });
                }
            }

            view.Releases.Add(release);
        }

        view.Releases = view.Releases
            .OrderByDescending(static release => release.PublishedAt ?? release.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(static release => release.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return view;
    }

    private static IReadOnlyDictionary<string, object?>? ResolveHubRoot(IReadOnlyDictionary<string, object?> data, string? dataPath)
    {
        if (!string.IsNullOrWhiteSpace(dataPath))
        {
            var resolved = ResolveDataPath(data, dataPath!);
            if (TryReadMap(resolved, out var mapped))
                return mapped;
        }

        if (TryReadMap(ResolveDataPath(data, DefaultDataPath), out var defaultMap))
            return defaultMap;
        if (TryReadMap(ResolveDataPath(data, "release-hub"), out var dashMap))
            return dashMap;
        if (TryReadMap(ResolveDataPath(data, "releaseHub"), out var camelMap))
            return camelMap;

        return null;
    }

    private static object? ResolveDataPath(IReadOnlyDictionary<string, object?> data, string path)
    {
        if (data is null || string.IsNullOrWhiteSpace(path))
            return null;

        object? current = data;
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in parts)
        {
            if (!TryReadMap(current, out var map))
                return null;

            var part = raw.Trim();
            if (!map.TryGetValue(part, out current))
                return null;
        }

        return current;
    }

    private static List<ReleaseHubAssetMatch> FindAssets(
        ReleaseHubDocumentView document,
        string? product,
        string? channel,
        string? platform,
        string? arch,
        string? kind)
    {
        var matches = new List<ReleaseHubAssetMatch>();
        foreach (var release in document.Releases)
        {
            foreach (var asset in release.Assets)
            {
                if (!MatchesFilter(asset.Product, product))
                    continue;
                if (!MatchesFilter(asset.Channel, channel))
                    continue;
                if (!MatchesFilter(asset.Platform, platform))
                    continue;
                if (!MatchesFilter(asset.Arch, arch))
                    continue;
                if (!MatchesFilter(asset.Kind, kind))
                    continue;

                matches.Add(new ReleaseHubAssetMatch
                {
                    Release = release,
                    Asset = asset,
                    EffectiveChannel = string.IsNullOrWhiteSpace(asset.Channel)
                        ? (release.IsPrerelease ? "preview" : "stable")
                        : asset.Channel
                });
            }
        }

        return matches;
    }

    private static string NamespaceReleaseBodyHtml(string html, string? releaseTag)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var prefix = Slugify(releaseTag);
        if (string.IsNullOrWhiteSpace(prefix))
            return html;

        var remappedHeadingIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var namespaced = HeadingWithIdRegex.Replace(html, match =>
        {
            var level = match.Groups["level"].Value;
            var attrs = match.Groups["attrs"].Value;
            var text = match.Groups["text"].Value;

            var idMatch = IdAttributeRegex.Match(attrs);
            var baseId = idMatch.Success
                ? idMatch.Groups["id"].Value
                : Slugify(Regex.Replace(text, "<.*?>", string.Empty));
            if (string.IsNullOrWhiteSpace(baseId))
                return match.Value;

            var namespacedId = NamespaceFragment(prefix, baseId);
            remappedHeadingIds[baseId] = namespacedId;
            var attrsWithId = idMatch.Success
                ? IdAttributeRegex.Replace(attrs, $" id=\"{namespacedId}\"", 1)
                : (string.IsNullOrWhiteSpace(attrs) ? $" id=\"{namespacedId}\"" : $"{attrs} id=\"{namespacedId}\"");
            return $"<h{level}{attrsWithId}>{text}</h{level}>";
        });

        if (remappedHeadingIds.Count == 0)
            return namespaced;

        return FragmentLinkRegex.Replace(namespaced, match =>
        {
            var target = match.Groups["target"].Value;
            if (string.IsNullOrWhiteSpace(target))
                return match.Value;

            if (!remappedHeadingIds.TryGetValue(target, out var namespacedTarget))
                return match.Value;

            return $"{match.Groups["prefix"].Value}{match.Groups["quote"].Value}#{namespacedTarget}{match.Groups["quote"].Value}{match.Groups["suffix"].Value}";
        });
    }

    private static string PolishReleaseBodyHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var polished = AnchorWithHrefRegex.Replace(html, RewriteRawUrlAnchor);
        return ReplaceTextOutsideAnchors(polished, LinkifyGithubMentions);
    }

    private static string RewriteRawUrlAnchor(Match match)
    {
        var href = System.Web.HttpUtility.HtmlDecode(match.Groups["href"].Value);
        if (string.IsNullOrWhiteSpace(href))
            return match.Value;

        var text = System.Web.HttpUtility.HtmlDecode(StripTags(match.Groups["text"].Value)).Trim();
        if (!string.Equals(text.TrimEnd('/'), href.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            return match.Value;

        var label = ResolveFriendlyUrlLabel(href, out var modifier);
        if (string.IsNullOrWhiteSpace(label))
            return match.Value;

        var classes = string.IsNullOrWhiteSpace(modifier)
            ? "pf-release-link"
            : $"pf-release-link pf-release-link--{modifier}";
        var attrs = EnsureAnchorClass(match.Groups["attrs"].Value, classes);
        return $"<a{attrs}>{Html(label)}</a>";
    }

    private static string? ResolveFriendlyUrlLabel(string href, out string modifier)
    {
        modifier = "external";
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
            return null;

        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 &&
                (parts[2].Equals("pull", StringComparison.OrdinalIgnoreCase) ||
                 parts[2].Equals("issues", StringComparison.OrdinalIgnoreCase)))
            {
                modifier = parts[2].Equals("pull", StringComparison.OrdinalIgnoreCase) ? "pull" : "issue";
                return "#" + parts[3];
            }

            if (parts.Length >= 4 && parts[2].Equals("compare", StringComparison.OrdinalIgnoreCase))
            {
                modifier = "compare";
                return "Compare " + parts[3].Replace("...", " to ", StringComparison.Ordinal);
            }

            if (parts.Length >= 4 && parts[2].Equals("releases", StringComparison.OrdinalIgnoreCase) && parts[3].Equals("tag", StringComparison.OrdinalIgnoreCase))
            {
                modifier = "release";
                return parts.Length >= 5 ? parts[4] : "Release";
            }
        }

        return uri.Host;
    }

    private static string LinkifyGithubMentions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        return GithubUserMentionRegex.Replace(text, match =>
        {
            var user = match.Groups["user"].Value;
            if (string.IsNullOrWhiteSpace(user))
                return match.Value;

            return $"<a class=\"pf-release-link pf-release-link--user\" href=\"https://github.com/{Html(user)}\">@{Html(user)}</a>";
        });
    }

    private static string ReplaceTextOutsideAnchors(string html, Func<string, string> replacer)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var sb = new StringBuilder(html.Length);
        var cursor = 0;
        var inAnchor = false;
        foreach (Match match in HtmlTagRegex.Matches(html))
        {
            if (match.Index > cursor)
            {
                var segment = html.Substring(cursor, match.Index - cursor);
                sb.Append(inAnchor ? segment : replacer(segment));
            }

            var tag = match.Value;
            sb.Append(tag);
            if (Regex.IsMatch(tag, "^<\\s*a\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                inAnchor = true;
            else if (Regex.IsMatch(tag, "^<\\s*/\\s*a\\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                inAnchor = false;

            cursor = match.Index + match.Length;
        }

        if (cursor < html.Length)
        {
            var segment = html.Substring(cursor);
            sb.Append(inAnchor ? segment : replacer(segment));
        }

        return sb.ToString();
    }

    private static string EnsureAnchorClass(string attrs, string cssClass)
    {
        if (string.IsNullOrWhiteSpace(cssClass))
            return attrs;

        var match = ClassAttributeRegex.Match(attrs);
        if (!match.Success)
            return attrs + " class=\"" + Html(cssClass) + "\"";

        var existing = match.Groups["value"].Value;
        var merged = JoinClasses(existing, cssClass);
        return ClassAttributeRegex.Replace(attrs, $"class={match.Groups["quote"].Value}{merged}{match.Groups["quote"].Value}", 1);
    }

    private static string StripTags(string html)
        => string.IsNullOrWhiteSpace(html) ? string.Empty : HtmlTagRegex.Replace(html, string.Empty);

    private static string NamespaceFragment(string prefix, string fragment)
    {
        var normalizedPrefix = Slugify(prefix);
        var normalizedFragment = Slugify(fragment);
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
            return normalizedFragment;
        if (string.IsNullOrWhiteSpace(normalizedFragment))
            return normalizedPrefix;
        if (normalizedFragment.StartsWith(normalizedPrefix + "-", StringComparison.Ordinal))
            return normalizedFragment;
        return $"{normalizedPrefix}-{normalizedFragment}";
    }

    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var slug = value.Trim().ToLowerInvariant();
        slug = Regex.Replace(slug, "[^a-z0-9\\s-]", "-");
        slug = Regex.Replace(slug, "\\s+", "-");
        slug = Regex.Replace(slug, "-{2,}", "-");
        return slug.Trim('-');
    }

    private static ReleaseButtonsGrouping NormalizeGrouping(string? value)
    {
        var normalized = NormalizeFilter(value);
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized is "none" or "off")
            return ReleaseButtonsGrouping.None;

        return normalized switch
        {
            "platform" => ReleaseButtonsGrouping.Platform,
            "arch" => ReleaseButtonsGrouping.Arch,
            "kind" => ReleaseButtonsGrouping.Kind,
            "release" or "tag" => ReleaseButtonsGrouping.Release,
            "channel" => ReleaseButtonsGrouping.Channel,
            "product" => ReleaseButtonsGrouping.Product,
            _ => ReleaseButtonsGrouping.None
        };
    }

    private static List<ReleaseAssetGroup> GroupAssets(List<ReleaseHubAssetMatch> matches, ReleaseButtonsGrouping mode)
    {
        if (mode == ReleaseButtonsGrouping.None)
            return new List<ReleaseAssetGroup> { new() { Key = "all", Items = matches } };

        var groups = new Dictionary<string, ReleaseAssetGroup>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ReleaseAssetGroup>();
        foreach (var match in matches)
        {
            var key = ResolveGroupKey(match, mode);
            if (string.IsNullOrWhiteSpace(key))
                key = "other";

            if (!groups.TryGetValue(key, out var group))
            {
                group = new ReleaseAssetGroup { Key = key };
                groups[key] = group;
                ordered.Add(group);
            }

            group.Items.Add(match);
        }

        return ordered;
    }

    private static string ResolveGroupKey(ReleaseHubAssetMatch match, ReleaseButtonsGrouping mode)
    {
        return mode switch
        {
            ReleaseButtonsGrouping.Platform => match.Asset.Platform,
            ReleaseButtonsGrouping.Arch => match.Asset.Arch,
            ReleaseButtonsGrouping.Kind => match.Asset.Kind,
            ReleaseButtonsGrouping.Release => string.IsNullOrWhiteSpace(match.Release.Tag) ? "release" : match.Release.Tag,
            ReleaseButtonsGrouping.Channel => match.EffectiveChannel,
            ReleaseButtonsGrouping.Product => match.Asset.Product,
            _ => "all"
        };
    }

    private static string FormatGroupingLabel(ReleaseButtonsGrouping mode, string value)
    {
        var token = string.IsNullOrWhiteSpace(value) ? "other" : value.Trim();
        return mode switch
        {
            ReleaseButtonsGrouping.Platform => "Platform: " + ToDisplayToken(token),
            ReleaseButtonsGrouping.Arch => "Architecture: " + ToDisplayToken(token),
            ReleaseButtonsGrouping.Kind => "Type: " + ToDisplayToken(token),
            ReleaseButtonsGrouping.Release => "Release: " + token,
            ReleaseButtonsGrouping.Channel => "Channel: " + ToDisplayToken(token),
            ReleaseButtonsGrouping.Product => "Product: " + token,
            _ => ToDisplayToken(token)
        };
    }

    private static string ResolveDefaultButtonLabel(ReleaseHubDocumentView document, string product)
    {
        if (document.ProductNames.TryGetValue(product, out var name) &&
            !string.IsNullOrWhiteSpace(name))
            return "Download " + name;

        return "Download";
    }

    private static string ResolveReleaseTitle(ReleaseHubReleaseView release)
    {
        if (!string.IsNullOrWhiteSpace(release.Title))
            return release.Title;
        if (!string.IsNullOrWhiteSpace(release.Tag))
            return release.Tag;
        return "Release";
    }

    private static string BuildAssetMeta(ReleaseHubAssetView asset)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(asset.Platform) && !string.Equals(asset.Platform, "any", StringComparison.OrdinalIgnoreCase))
            parts.Add(ToDisplayToken(asset.Platform));
        if (!string.IsNullOrWhiteSpace(asset.Arch) && !string.Equals(asset.Arch, "any", StringComparison.OrdinalIgnoreCase))
            parts.Add(asset.Arch);
        if (asset.Size is > 0)
            parts.Add(FormatBytes(asset.Size.Value));

        return string.Join(" · ", parts);
    }

    private static string? ResolveAssetThumbnailUrl(ReleaseHubAssetView asset)
    {
        if (asset is null)
            return null;

        if (string.IsNullOrWhiteSpace(asset.ThumbnailUrl))
            return null;

        return asset.ThumbnailUrl.Trim();
    }

    private static void AppendAssetBadges(
        StringBuilder sb,
        ReleaseHubAssetView asset,
        string containerClass,
        string badgeClass)
    {
        var badges = BuildAssetBadges(asset);
        if (badges.Count == 0)
            return;

        sb.Append("<span class=\"")
            .Append(Html(containerClass))
            .Append("\">");

        foreach (var badge in badges)
        {
            sb.Append("<span class=\"")
                .Append(Html(badgeClass))
                .Append(" ")
                .Append(Html(badgeClass))
                .Append("--")
                .Append(Html(badge.Kind))
                .Append("\">")
                .Append(Html(badge.Label))
                .Append("</span>");
        }

        sb.Append("</span>");
    }

    private static List<ReleaseAssetBadgeView> BuildAssetBadges(ReleaseHubAssetView asset)
    {
        var badges = new List<ReleaseAssetBadgeView>();

        if (!string.IsNullOrWhiteSpace(asset.Platform) &&
            !string.Equals(asset.Platform, "any", StringComparison.OrdinalIgnoreCase))
        {
            badges.Add(new ReleaseAssetBadgeView
            {
                Kind = "platform",
                Label = ToDisplayToken(asset.Platform)
            });
        }

        if (!string.IsNullOrWhiteSpace(asset.Arch) &&
            !string.Equals(asset.Arch, "any", StringComparison.OrdinalIgnoreCase))
        {
            badges.Add(new ReleaseAssetBadgeView
            {
                Kind = "arch",
                Label = ToDisplayToken(asset.Arch)
            });
        }

        return badges;
    }

    private static string FormatBytes(long size)
    {
        if (size < 1024)
            return size.ToString(CultureInfo.InvariantCulture) + " B";
        if (size < 1024L * 1024L)
            return (size / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        if (size < 1024L * 1024L * 1024L)
            return (size / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";

        return (size / (1024d * 1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture) + " GB";
    }

    private static string ToDisplayToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        if (string.Equals(normalized, "any", StringComparison.OrdinalIgnoreCase))
            return "Any";
        if (string.Equals(normalized, "x64", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "x86", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "arm64", StringComparison.OrdinalIgnoreCase))
            return normalized.ToLowerInvariant();

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.Replace('-', ' ').Replace('_', ' ').ToLowerInvariant());
    }

    private static bool MatchesFilter(string value, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        return string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "*" or "any" or "all" or "auto")
            return null;
        return normalized;
    }

    private static string JoinClasses(params string?[] values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            foreach (var token in value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;
                set.Add(token.Trim());
            }
        }

        return string.Join(" ", set);
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;
            var text = value.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }
        return null;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, object?> map, bool defaultValue, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;
            if (value is bool booleanValue)
                return booleanValue;
            var text = value.ToString();
            if (bool.TryParse(text, out var parsedBool))
                return parsedBool;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                return parsedInt != 0;
        }

        return defaultValue;
    }

    private static long? ReadLong(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;
            if (value is long longValue)
                return longValue;
            if (value is int intValue)
                return intValue;
            if (value is double doubleValue)
                return (long)Math.Round(doubleValue);
            if (long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        return null;
    }

    private static DateTimeOffset? ReadDate(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;

            if (value is DateTimeOffset dto)
                return dto;
            if (value is DateTime dt)
                return new DateTimeOffset(dt);

            if (DateTimeOffset.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed;
        }

        return null;
    }

    private static bool TryReadList(IReadOnlyDictionary<string, object?> map, string key, out List<object?> values)
        => TryReadList(map, new[] { key }, out values);

    private static bool TryReadList(IReadOnlyDictionary<string, object?> map, string key1, string key2, out List<object?> values)
        => TryReadList(map, new[] { key1, key2 }, out values);

    private static bool TryReadList(IReadOnlyDictionary<string, object?> map, string[] keys, out List<object?> values)
    {
        values = new List<object?>();
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;
            if (TryReadList(value, out var list))
            {
                values = list;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadList(object? value, out List<object?> values)
    {
        values = new List<object?>();
        if (value is null || value is string)
            return false;

        if (value is List<object?> typedList)
        {
            values = typedList;
            return true;
        }

        if (value is IEnumerable<object?> enumerable)
        {
            values = enumerable.ToList();
            return true;
        }

        return false;
    }

    private static bool TryReadMap(object? value, out IReadOnlyDictionary<string, object?> map)
    {
        map = null!;
        if (value is IReadOnlyDictionary<string, object?> ro)
        {
            map = ro;
            return true;
        }

        if (value is Dictionary<string, object?> dictionary)
        {
            map = dictionary;
            return true;
        }

        return false;
    }

    private static string Html(string value)
        => System.Web.HttpUtility.HtmlEncode(value ?? string.Empty);

    private enum ReleaseButtonsGrouping
    {
        None,
        Platform,
        Arch,
        Kind,
        Release,
        Channel,
        Product
    }

    private sealed class ReleaseHubDocumentView
    {
        public Dictionary<string, string> ProductNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ReleaseHubReleaseView> Releases { get; set; } = new();
    }

    private sealed class ReleaseHubReleaseView
    {
        public string Tag { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public DateTimeOffset? PublishedAt { get; init; }
        public DateTimeOffset? CreatedAt { get; init; }
        public bool IsPrerelease { get; init; }
        public bool IsDraft { get; init; }
        public bool IsLatestStable { get; init; }
        public bool IsLatestPrerelease { get; init; }
        public string BodyHtml { get; set; } = string.Empty;
        public List<ReleaseHubAssetView> Assets { get; } = new();
    }

    private sealed class ReleaseHubAssetView
    {
        public string Name { get; init; } = string.Empty;
        public string DownloadUrl { get; init; } = string.Empty;
        public string Product { get; init; } = "unknown";
        public string Channel { get; init; } = "stable";
        public string Platform { get; init; } = "any";
        public string Arch { get; init; } = "any";
        public string Kind { get; init; } = "file";
        public string? ThumbnailUrl { get; init; }
        public long? Size { get; init; }
    }

    private sealed class ReleaseHubAssetMatch
    {
        public ReleaseHubReleaseView Release { get; init; } = new();
        public ReleaseHubAssetView Asset { get; init; } = new();
        public string EffectiveChannel { get; init; } = "stable";
    }

    private sealed class ReleaseAssetGroup
    {
        public string Key { get; init; } = "all";
        public List<ReleaseHubAssetMatch> Items { get; init; } = new();
    }

    private sealed class ReleaseAssetBadgeView
    {
        public string Kind { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
    }
}
