using OfficeIMO.Markdown;

namespace PowerForge.Web;

internal static class MarkdownRenderer
{
    private sealed class AuthoredImageAttributes
    {
        public int? Width { get; init; }
        public int? Height { get; init; }
    }

    private static readonly System.Text.RegularExpressions.Regex AnchorTagRegex = new(
        "<a\\b(?<attrs>[^>]*)>(?<text>.*?)</a>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

    private static readonly System.Text.RegularExpressions.Regex ImageTagRegex = new(
        "<img\\b(?<attrs>[^>]*?)(?<close>\\s*/?)>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex MarkdownImageRegex = new(
        "!\\[[^\\]]*\\]\\((?<url>\\S+?)(?:\\s+(?:\"(?<title_dq>[^\"]+)\"|'(?<title_sq>[^']+)'))?\\)",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex DimensionHintRegex = new(
        "^(?<width>\\d{1,5})\\s*x\\s*(?<height>\\d{1,5})$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex AriaLabelAttributeRegex = new(
        "\\baria-label\\s*=",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex LoadingAttributeRegex = new(
        "\\bloading\\s*=",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex DecodingAttributeRegex = new(
        "\\bdecoding\\s*=",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex WidthAttributeRegex = new(
        "\\bwidth\\s*=",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex HeightAttributeRegex = new(
        "\\bheight\\s*=",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex TitleAttributeRegex = new(
        "\\btitle\\s*=\\s*(?<quote>['\"])(?<value>.*?)\\k<quote>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex StyleAttributeRegex = new(
        "\\bstyle\\s*=\\s*(?<quote>['\"])(?<value>.*?)\\k<quote>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex SrcAttributeRegex = new(
        "\\bsrc\\s*=\\s*(?<quote>['\"])(?<value>.*?)\\k<quote>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex HrefAttributeRegex = new(
        "\\bhref\\s*=\\s*(?<quote>['\"])(?<value>.*?)\\k<quote>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex HtmlTagRegex = new(
        "<.*?>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

    private static readonly System.Text.RegularExpressions.Regex MultiWhitespaceRegex = new(
        "\\s+",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly MarkdownReaderOptions GitHubLikeReaderOptions = new()
    {
        // GitHub-flavored markdown does not support definition lists by default.
        // Disabling this avoids accidental dt/dd rendering from "Q:"/"A:" prose.
        DefinitionLists = false
    };

    public static string RenderToHtml(string content, MarkdownSpec? markdown = null, string? sourcePath = null, string? siteRoot = null)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;

        try
        {
            content = MarkdownMediaTagNormalizer.NormalizeMultilineMediaTagsOutsideFences(content);
            var authoredImageAttributes = CaptureAuthoredImageAttributes(content);
            var doc = MarkdownReader.Parse(content, GitHubLikeReaderOptions);
            var options = new HtmlOptions
            {
                Kind = HtmlKind.Fragment,
                Prism = new PrismOptions
                {
                    Enabled = true,
                    Theme = PrismTheme.GithubAuto
                }
            };
            var html = doc.ToHtmlFragment(options);
            html = ApplyAuthoredImageAttributes(html, authoredImageAttributes);
            html = ApplyImageHints(html, markdown, sourcePath, siteRoot);
            html = ApplyLinkAriaLabels(html);
            return InjectHeadingIds(html);
        }
        catch (Exception ex)
        {
            return $"<pre class=\"markdown-error\">Error rendering markdown: {System.Web.HttpUtility.HtmlEncode(ex.Message)}</pre>";
        }
    }

    public static string ApplyImageHints(string html, MarkdownSpec? markdown = null, string? sourcePath = null, string? siteRoot = null)
    {
        return InjectDefaultImageHints(html, markdown, sourcePath, siteRoot);
    }

    public static string ApplyLinkAriaLabels(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        return AnchorTagRegex.Replace(html, match =>
        {
            var attrs = match.Groups["attrs"].Value;
            if (AriaLabelAttributeRegex.IsMatch(attrs))
                return match.Value;

            var href = GetAttributeValue(attrs, HrefAttributeRegex);
            if (string.IsNullOrWhiteSpace(href))
                return match.Value;

            var innerHtml = match.Groups["text"].Value;
            var linkText = NormalizeLinkText(innerHtml);
            var ariaLabel = BuildLinkAriaLabel(linkText, href);
            if (string.IsNullOrWhiteSpace(ariaLabel))
                return match.Value;

            return $"<a{attrs} aria-label=\"{System.Web.HttpUtility.HtmlEncode(ariaLabel)}\">{innerHtml}</a>";
        });
    }

    private static string InjectHeadingIds(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        return System.Text.RegularExpressions.Regex.Replace(html,
            "<h(?<level>[1-6])(?<attrs>[^>]*)>(?<text>.*?)</h\\1>",
            match =>
            {
                var level = match.Groups["level"].Value;
                var attrs = match.Groups["attrs"].Value;
                var headingHtml = NormalizeInlineCodeInHeading(match.Groups["text"].Value);
                var text = System.Text.RegularExpressions.Regex.Replace(headingHtml, "<.*?>", string.Empty);
                var id = Slugify(text);
                var hasId = System.Text.RegularExpressions.Regex.IsMatch(
                    attrs,
                    "\\sid\\s*=",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                var attrsWithId = hasId
                    ? attrs
                    : (string.IsNullOrWhiteSpace(attrs) ? $" id=\"{id}\"" : $"{attrs} id=\"{id}\"");
                return $"<h{level}{attrsWithId}>{headingHtml}</h{level}>";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    }

    private static string InjectDefaultImageHints(string html, MarkdownSpec? markdown, string? sourcePath, string? siteRoot)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var injectHints = markdown?.AutoImageHints ?? true;
        var injectDimensions = markdown?.AutoImageDimensions ?? true;
        if (!injectHints && !injectDimensions)
            return html;

        var loadingValue = string.IsNullOrWhiteSpace(markdown?.DefaultImageLoading)
            ? "lazy"
            : markdown!.DefaultImageLoading.Trim();
        var decodingValue = string.IsNullOrWhiteSpace(markdown?.DefaultImageDecoding)
            ? "async"
            : markdown!.DefaultImageDecoding.Trim();

        return ImageTagRegex.Replace(html, match =>
        {
            var attrs = match.Groups["attrs"].Value;
            var close = match.Groups["close"].Value;
            if (string.IsNullOrEmpty(close))
                close = string.Empty;

            var hasLoading = LoadingAttributeRegex.IsMatch(attrs);
            var hasDecoding = DecodingAttributeRegex.IsMatch(attrs);
            var hasWidth = WidthAttributeRegex.IsMatch(attrs);
            var hasHeight = HeightAttributeRegex.IsMatch(attrs);
            var hasAspectRatio = HasAspectRatioStyle(attrs);
            if (hasLoading && hasDecoding && (!injectDimensions || hasAspectRatio || (hasWidth && hasHeight)))
                return match.Value;

            var attrsUpdated = attrs;
            if (injectHints && !hasLoading)
                attrsUpdated += $" loading=\"{System.Web.HttpUtility.HtmlEncode(loadingValue)}\"";
            if (injectHints && !hasDecoding)
                attrsUpdated += $" decoding=\"{System.Web.HttpUtility.HtmlEncode(decodingValue)}\"";
            if (injectDimensions && !hasAspectRatio)
            {
                var src = GetAttributeValue(attrsUpdated, SrcAttributeRegex);
                if (WebImageDimensions.TryResolve(src, sourcePath, siteRoot, out var dimensions))
                {
                    if (!hasWidth && dimensions.Width > 0)
                        attrsUpdated += $" width=\"{dimensions.Width}\"";
                    if (!hasHeight && dimensions.Height > 0)
                        attrsUpdated += $" height=\"{dimensions.Height}\"";
                }
            }

            return $"<img{attrsUpdated}{close}>";
        });
    }

    private static Dictionary<string, AuthoredImageAttributes> CaptureAuthoredImageAttributes(string content)
    {
        var authored = new Dictionary<string, AuthoredImageAttributes>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(content))
            return authored;

        foreach (System.Text.RegularExpressions.Match match in ImageTagRegex.Matches(content))
        {
            var attrs = match.Groups["attrs"].Value;
            var src = GetAttributeValue(attrs, SrcAttributeRegex);
            if (string.IsNullOrWhiteSpace(src))
                continue;

            var width = ParseIntegerAttributeValue(GetAttributeValue(attrs, WidthAttributeRegex));
            var height = ParseIntegerAttributeValue(GetAttributeValue(attrs, HeightAttributeRegex));
            if (!width.HasValue && !height.HasValue)
                continue;

            authored[src] = new AuthoredImageAttributes
            {
                Width = width,
                Height = height
            };
        }

        foreach (System.Text.RegularExpressions.Match match in MarkdownImageRegex.Matches(content))
        {
            var src = match.Groups["url"].Value.Trim();
            if (string.IsNullOrWhiteSpace(src))
                continue;

            var title = match.Groups["title_dq"].Success
                ? match.Groups["title_dq"].Value
                : match.Groups["title_sq"].Value;
            if (!TryParseDimensionHint(title, out var width, out var height))
                continue;

            authored[src] = new AuthoredImageAttributes
            {
                Width = width,
                Height = height
            };
        }

        return authored;
    }

    private static string ApplyAuthoredImageAttributes(string html, Dictionary<string, AuthoredImageAttributes> authoredImageAttributes)
    {
        if (string.IsNullOrWhiteSpace(html) || authoredImageAttributes.Count == 0)
            return string.IsNullOrWhiteSpace(html) ? string.Empty : html;

        return ImageTagRegex.Replace(html, match =>
        {
            var attrs = match.Groups["attrs"].Value;
            var close = match.Groups["close"].Value;
            if (string.IsNullOrEmpty(close))
                close = string.Empty;

            var src = GetAttributeValue(attrs, SrcAttributeRegex);
            if (string.IsNullOrWhiteSpace(src) || !authoredImageAttributes.TryGetValue(src, out var authored))
                return match.Value;

            var hasWidth = WidthAttributeRegex.IsMatch(attrs);
            var hasHeight = HeightAttributeRegex.IsMatch(attrs);
            if ((hasWidth || !authored.Width.HasValue) && (hasHeight || !authored.Height.HasValue))
            {
                var title = GetAttributeValue(attrs, TitleAttributeRegex);
                if (TryParseDimensionHint(title, out _, out _))
                    return $"<img{TitleAttributeRegex.Replace(attrs, string.Empty)}{close}>";

                return match.Value;
            }

            var attrsUpdated = attrs;
            if (!hasWidth && authored.Width is > 0)
                attrsUpdated += $" width=\"{authored.Width.Value}\"";
            if (!hasHeight && authored.Height is > 0)
                attrsUpdated += $" height=\"{authored.Height.Value}\"";
            var titleValue = GetAttributeValue(attrsUpdated, TitleAttributeRegex);
            if (TryParseDimensionHint(titleValue, out _, out _))
                attrsUpdated = TitleAttributeRegex.Replace(attrsUpdated, string.Empty);

            return $"<img{attrsUpdated}{close}>";
        });
    }

    private static bool HasAspectRatioStyle(string attrs)
    {
        if (string.IsNullOrWhiteSpace(attrs))
            return false;

        var styleMatch = StyleAttributeRegex.Match(attrs);
        if (!styleMatch.Success)
            return false;

        var styleValue = styleMatch.Groups["value"].Value;
        return styleValue.IndexOf("aspect-ratio", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? GetAttributeValue(string attrs, System.Text.RegularExpressions.Regex regex)
    {
        if (string.IsNullOrWhiteSpace(attrs))
            return null;

        var match = regex.Match(attrs);
        if (!match.Success)
            return null;

        var value = match.Groups["value"].Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? ParseIntegerAttributeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
    }

    private static bool TryParseDimensionHint(string? value, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = DimensionHintRegex.Match(value.Trim());
        if (!match.Success)
            return false;

        return int.TryParse(match.Groups["width"].Value, out width)
               && width > 0
               && int.TryParse(match.Groups["height"].Value, out height)
               && height > 0;
    }

    private static string NormalizeInlineCodeInHeading(string headingHtml)
    {
        if (string.IsNullOrWhiteSpace(headingHtml))
            return string.Empty;
        if (headingHtml.Contains("<code", StringComparison.OrdinalIgnoreCase))
            return headingHtml;

        return System.Text.RegularExpressions.Regex.Replace(
            headingHtml,
            "(?<!\\\\)`(?<code>[^`]+)`",
            "<code>${code}</code>");
    }

    private static string NormalizeLinkText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var withoutTags = HtmlTagRegex.Replace(html, string.Empty);
        var decoded = System.Web.HttpUtility.HtmlDecode(withoutTags) ?? string.Empty;
        return MultiWhitespaceRegex.Replace(decoded, " ").Trim();
    }

    private static string? BuildLinkAriaLabel(string linkText, string href)
    {
        if (string.IsNullOrWhiteSpace(linkText) || string.IsNullOrWhiteSpace(href))
            return null;

        var normalizedText = linkText.Trim();
        var textKey = normalizedText.ToLowerInvariant();
        if (!ShouldAutoDescribeLink(textKey))
            return null;

        if (!Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri) && !Uri.TryCreate(new Uri("https://example.test"), href, out absoluteUri))
            return null;

        return textKey switch
        {
            "github" or "github." => BuildGitHubAriaLabel(absoluteUri),
            "powershellgallery" or "powershell gallery" => BuildPowerShellGalleryAriaLabel(absoluteUri),
            "youtube video" => BuildYouTubeAriaLabel(absoluteUri),
            "microsoft technet" => BuildHostAndSlugAriaLabel("Microsoft Technet article", absoluteUri),
            "mit license" => BuildMitLicenseAriaLabel(absoluteUri),
            "microsoft account" => BuildHostAndSlugAriaLabel("Microsoft account page", absoluteUri),
            "register here" => BuildHostAndSlugAriaLabel("Registration page", absoluteUri),
            "instanceid" => BuildHostAndSlugAriaLabel("InstanceId reference", absoluteUri),
            "blog post" => BuildHostAndSlugAriaLabel("Blog post", absoluteUri),
            "read more" => BuildHostAndSlugAriaLabel("Read more", absoluteUri),
            "here" => BuildHostAndSlugAriaLabel("Open reference", absoluteUri),
            _ => null
        };
    }

    private static bool ShouldAutoDescribeLink(string textKey)
    {
        return textKey is
            "github" or
            "github." or
            "powershellgallery" or
            "powershell gallery" or
            "youtube video" or
            "microsoft technet" or
            "mit license" or
            "microsoft account" or
            "register here" or
            "instanceid" or
            "blog post" or
            "read more" or
            "here";
    }

    private static string BuildGitHubAriaLabel(Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return "GitHub";

        if (segments.Length == 1)
            return $"GitHub organization {segments[0]}";

        var repo = segments[1];
        if (segments.Length >= 3)
        {
            var kind = segments[2].ToLowerInvariant();
            if (kind == "issues")
                return $"GitHub issues for {repo}";
            if (kind == "releases")
                return $"GitHub releases for {repo}";
            if (kind == "blob")
                return $"GitHub file in {repo}";
            if (kind == "tree")
                return $"GitHub folder in {repo}";
        }

        return $"GitHub repository {repo}";
    }

    private static string BuildPowerShellGalleryAriaLabel(Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].Equals("packages", StringComparison.OrdinalIgnoreCase))
            return $"PowerShell Gallery package {segments[1]}";
        if (segments.Length >= 2 && segments[0].Equals("profiles", StringComparison.OrdinalIgnoreCase))
            return $"PowerShell Gallery profile {segments[1]}";

        return "PowerShell Gallery";
    }

    private static string BuildYouTubeAriaLabel(Uri uri)
    {
        var videoId = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("v");
        if (string.IsNullOrWhiteSpace(videoId))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
                videoId = segments[^1];
        }

        return string.IsNullOrWhiteSpace(videoId)
            ? "YouTube video"
            : $"YouTube video {videoId}";
    }

    private static string BuildMitLicenseAriaLabel(Uri uri)
    {
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
                return $"MIT license for {segments[1]}";
        }

        return BuildHostAndSlugAriaLabel("MIT license document", uri);
    }

    private static string BuildHostAndSlugAriaLabel(string prefix, Uri uri)
    {
        var slug = ExtractReadableSlug(uri);
        return string.IsNullOrWhiteSpace(slug)
            ? $"{prefix} on {uri.Host}"
            : $"{prefix}: {slug}";
    }

    private static string? ExtractReadableSlug(Uri uri)
    {
        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var lastSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(lastSegment))
            return null;

        lastSegment = lastSegment.Replace(".aspx", string.Empty, StringComparison.OrdinalIgnoreCase)
                                 .Replace(".html", string.Empty, StringComparison.OrdinalIgnoreCase);
        lastSegment = lastSegment.Replace('-', ' ').Replace('_', ' ');
        return MultiWhitespaceRegex.Replace(lastSegment, " ").Trim();
    }

    private static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lower = text.Trim().ToLowerInvariant();
        lower = System.Text.RegularExpressions.Regex.Replace(lower, "[^a-z0-9\\s-]", string.Empty);
        lower = System.Text.RegularExpressions.Regex.Replace(lower, "\\s+", "-");
        return lower.Trim('-');
    }

}
