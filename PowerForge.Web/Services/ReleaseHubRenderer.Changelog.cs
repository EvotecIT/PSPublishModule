using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal static partial class ReleaseHubRenderer
{
    // Strip only anchor wrappers; preserve readable notes and escaped code examples.
    private static readonly Regex ChangelogAnchorTagRegex = new(
        """</?a\b(?:[^>"']|"[^"]*"|'[^']*')*>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal static string RenderReleaseChangelog(
        IReadOnlyDictionary<string, object?> data,
        MarkdownSpec? markdown,
        string? product = null,
        int limit = 20,
        bool includePreview = true,
        string? cssClass = null,
        string? dataPath = null,
        bool includeLinks = true,
        bool includeAssets = true)
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
            if (includeLinks && !string.IsNullOrWhiteSpace(release.Url))
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
                    .Append(includeLinks ? release.BodyHtml : ChangelogAnchorTagRegex.Replace(release.BodyHtml, string.Empty))
                    .Append("</div>");
            }

            if (includeAssets && assets.Count > 0)
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
                        .Append(Html(ResolveChangelogAssetLabel(asset, release)))
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

}
