using System.Text;

namespace PowerForge.Web;

internal static partial class ShortcodeDefaults
{
    private static readonly Dictionary<string, int> ScreenshotWidthBySize = new(StringComparer.OrdinalIgnoreCase)
    {
        ["xs"] = 320,
        ["sm"] = 420,
        ["md"] = 640,
        ["lg"] = 960,
        ["xl"] = 1200,
        ["full"] = 0
    };

    internal static string RenderMedia(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var kind = ReadAttr(attrs, "kind", "type", "provider");
        if (string.IsNullOrWhiteSpace(kind))
            kind = InferMediaKind(ReadAttr(attrs, "src", "url", "href"));

        return kind.ToLowerInvariant() switch
        {
            "youtube" => RenderYouTube(context, attrs),
            "x" or "tweet" => RenderXPost(context, attrs),
            "video" => RenderVideo(context, attrs),
            "iframe" or "embed" => RenderIFrame(context, attrs),
            "image" or "screenshot" => RenderScreenshot(context, attrs),
            "screenshots" or "gallery" => RenderScreenshots(context, attrs),
            _ => string.Empty
        };
    }

    internal static string RenderYouTube(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var idOrUrl = ReadAttr(attrs, "id", "video", "src", "url", "href");
        var videoId = ExtractYouTubeVideoId(idOrUrl);
        if (string.IsNullOrWhiteSpace(videoId))
            return string.Empty;

        var start = ReadIntAttr(attrs, "start", "t");
        var autoplay = ReadBoolAttr(attrs, defaultValue: false, "autoplay");
        var muted = ReadBoolAttr(attrs, defaultValue: false, "muted", "mute");
        var loop = ReadBoolAttr(attrs, defaultValue: false, "loop");
        var controls = ReadBoolAttr(attrs, defaultValue: true, "controls");
        var modestBranding = ReadBoolAttr(attrs, defaultValue: true, "modestbranding");
        var rel = ReadBoolAttr(attrs, defaultValue: false, "rel");
        var noCookie = ReadBoolAttr(attrs, defaultValue: true, "nocookie", "noCookie");
        var ratio = NormalizeRatio(ReadAttr(attrs, "ratio", "aspect"), "16/9");
        var title = ReadAttr(attrs, "title");
        if (string.IsNullOrWhiteSpace(title))
            title = "YouTube video";

        var query = new List<string>();
        if (start is > 0) query.Add("start=" + start.Value);
        if (autoplay) query.Add("autoplay=1");
        if (muted) query.Add("mute=1");
        if (!controls) query.Add("controls=0");
        if (modestBranding) query.Add("modestbranding=1");
        if (!rel) query.Add("rel=0");
        if (loop)
        {
            query.Add("loop=1");
            query.Add("playlist=" + Uri.EscapeDataString(videoId));
        }

        var host = noCookie ? "www.youtube-nocookie.com" : "www.youtube.com";
        var url = $"https://{host}/embed/{Uri.EscapeDataString(videoId)}";
        if (query.Count > 0)
            url += "?" + string.Join("&", query);

        var className = JoinClassTokens("pf-media pf-media-youtube", ReadAttr(attrs, "class"), ResolveSizeClass(attrs, "lg"));
        var style = BuildContainerStyle(attrs, "lg", "center");

        return $@"<div class=""{System.Web.HttpUtility.HtmlEncode(className)}"" style=""{System.Web.HttpUtility.HtmlEncode(style)}"">
  <div class=""pf-media-frame"" style=""position:relative;width:100%;aspect-ratio:{System.Web.HttpUtility.HtmlEncode(ratio)};overflow:hidden;border-radius:14px;background:#0b0b0f;"">
    <iframe src=""{System.Web.HttpUtility.HtmlEncode(url)}"" title=""{System.Web.HttpUtility.HtmlEncode(title)}"" loading=""lazy"" referrerpolicy=""strict-origin-when-cross-origin"" allow=""accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"" allowfullscreen style=""position:absolute;inset:0;width:100%;height:100%;border:0;""></iframe>
  </div>
</div>";
    }

    internal static string RenderXPost(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var url = ReadAttr(attrs, "url", "src", "href");
        var status = ReadAttr(attrs, "status", "id");
        var user = ReadAttr(attrs, "user", "author");
        if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(status))
        {
            var account = string.IsNullOrWhiteSpace(user) ? "i" : user.Trim().TrimStart('@');
            url = $"https://x.com/{account}/status/{status.Trim()}";
        }
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var theme = ReadAttr(attrs, "theme");
        var conversation = ReadAttr(attrs, "conversation");
        var dnt = ReadBoolAttr(attrs, defaultValue: false, "dnt", "doNotTrack");
        var className = JoinClassTokens("pf-media pf-media-x", ReadAttr(attrs, "class"), ResolveSizeClass(attrs, "md"));
        var style = BuildContainerStyle(attrs, "md", "center");

        var extraData = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(theme))
            extraData.Append($" data-theme=\"{System.Web.HttpUtility.HtmlEncode(theme)}\"");
        if (!string.IsNullOrWhiteSpace(conversation))
            extraData.Append($" data-conversation=\"{System.Web.HttpUtility.HtmlEncode(conversation)}\"");
        if (dnt)
            extraData.Append(" data-dnt=\"true\"");

        return $@"<div class=""{System.Web.HttpUtility.HtmlEncode(className)}"" style=""{System.Web.HttpUtility.HtmlEncode(style)}"">
  <blockquote class=""twitter-tweet""{extraData}>
    <a href=""{System.Web.HttpUtility.HtmlEncode(url)}"">View post on X</a>
  </blockquote>
  <script async src=""https://platform.twitter.com/widgets.js"" charset=""utf-8""></script>
</div>";
    }

    internal static string RenderVideo(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var src = ReadAttr(attrs, "src", "url", "href");
        if (string.IsNullOrWhiteSpace(src))
            return string.Empty;

        var poster = ReadAttr(attrs, "poster");
        var preload = ReadAttr(attrs, "preload");
        if (string.IsNullOrWhiteSpace(preload))
            preload = "metadata";

        var controls = ReadBoolAttr(attrs, defaultValue: true, "controls");
        var autoplay = ReadBoolAttr(attrs, defaultValue: false, "autoplay");
        var muted = ReadBoolAttr(attrs, defaultValue: autoplay, "muted", "mute");
        var loop = ReadBoolAttr(attrs, defaultValue: false, "loop");
        var playsInline = ReadBoolAttr(attrs, defaultValue: true, "playsinline", "playsInline");
        var caption = ReadAttr(attrs, "caption", "text");
        var title = ReadAttr(attrs, "title");
        var className = JoinClassTokens("pf-media pf-media-video", ReadAttr(attrs, "class"), ResolveSizeClass(attrs, "lg"));
        var style = BuildContainerStyle(attrs, "lg", "center");

        var attrBuilder = new StringBuilder();
        attrBuilder.Append($" preload=\"{System.Web.HttpUtility.HtmlEncode(preload)}\"");
        if (!string.IsNullOrWhiteSpace(title))
            attrBuilder.Append($" title=\"{System.Web.HttpUtility.HtmlEncode(title)}\"");
        if (!string.IsNullOrWhiteSpace(poster))
            attrBuilder.Append($" poster=\"{System.Web.HttpUtility.HtmlEncode(poster)}\"");
        if (controls) attrBuilder.Append(" controls");
        if (autoplay) attrBuilder.Append(" autoplay");
        if (muted) attrBuilder.Append(" muted");
        if (loop) attrBuilder.Append(" loop");
        if (playsInline) attrBuilder.Append(" playsinline");

        var figure = new StringBuilder();
        figure.AppendLine($@"<figure class=""{System.Web.HttpUtility.HtmlEncode(className)}"" style=""{System.Web.HttpUtility.HtmlEncode(style)}"">");
        figure.AppendLine($@"  <video src=""{System.Web.HttpUtility.HtmlEncode(src)}"" style=""display:block;width:100%;height:auto;border-radius:14px;background:#08090c;""{attrBuilder}></video>");
        if (!string.IsNullOrWhiteSpace(caption))
            figure.AppendLine($@"  <figcaption style=""margin-top:.5rem;font-size:.9rem;opacity:.82;"">{System.Web.HttpUtility.HtmlEncode(caption)}</figcaption>");
        figure.Append("</figure>");
        return figure.ToString();
    }

    internal static string RenderIFrame(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var src = ReadAttr(attrs, "src", "url", "href");
        if (string.IsNullOrWhiteSpace(src))
            return string.Empty;

        var title = ReadAttr(attrs, "title");
        if (string.IsNullOrWhiteSpace(title))
            title = "Embedded content";
        var ratio = NormalizeRatio(ReadAttr(attrs, "ratio", "aspect"), "16/9");
        var className = JoinClassTokens("pf-media pf-media-embed", ReadAttr(attrs, "class"), ResolveSizeClass(attrs, "lg"));
        var style = BuildContainerStyle(attrs, "lg", "center");

        return $@"<div class=""{System.Web.HttpUtility.HtmlEncode(className)}"" style=""{System.Web.HttpUtility.HtmlEncode(style)}"">
  <div class=""pf-media-frame"" style=""position:relative;width:100%;aspect-ratio:{System.Web.HttpUtility.HtmlEncode(ratio)};overflow:hidden;border-radius:14px;background:#0b0b0f;"">
    <iframe src=""{System.Web.HttpUtility.HtmlEncode(src)}"" title=""{System.Web.HttpUtility.HtmlEncode(title)}"" loading=""lazy"" referrerpolicy=""strict-origin-when-cross-origin"" allowfullscreen style=""position:absolute;inset:0;width:100%;height:100%;border:0;""></iframe>
  </div>
</div>";
    }

    internal static string RenderScreenshot(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var src = ReadAttr(attrs, "src", "image", "url", "href");
        if (string.IsNullOrWhiteSpace(src))
            return string.Empty;

        var alt = ReadAttr(attrs, "alt", "title");
        var caption = ReadAttr(attrs, "caption", "text");
        var link = ReadAttr(attrs, "link", "full", "target");
        var objectFit = ReadAttr(attrs, "fit");
        if (string.IsNullOrWhiteSpace(objectFit))
            objectFit = "cover";
        var ratio = NormalizeRatio(ReadAttr(attrs, "ratio", "aspect"), string.Empty);
        var width = ReadIntAttr(attrs, "width", "w");
        var height = ReadIntAttr(attrs, "height", "h");

        var className = JoinClassTokens("pf-screenshot", ReadAttr(attrs, "class"), ResolveSizeClass(attrs, "lg"));
        var style = BuildContainerStyle(attrs, "lg", "center");
        return RenderScreenshotFigure(
            src,
            alt,
            caption,
            link,
            className,
            style,
            width,
            height,
            ratio,
            objectFit,
            inCollection: false);
    }

    internal static string RenderScreenshots(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var shots = ShortcodeProcessor.ResolveList(context.Data, attrs)?.ToList();
        if (shots is null || shots.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(ReadAttr(attrs, "src", "image", "url")))
                return RenderScreenshot(context, attrs);
            return string.Empty;
        }

        var layout = ReadAttr(attrs, "layout", "view");
        if (string.IsNullOrWhiteSpace(layout))
            layout = "grid";
        layout = layout.ToLowerInvariant();

        var columns = Math.Clamp(ReadIntAttr(attrs, "columns", "cols") ?? 3, 1, 6);
        var gap = ReadAttr(attrs, "gap");
        if (string.IsNullOrWhiteSpace(gap))
            gap = "1rem";

        var className = JoinClassTokens("pf-screenshots", ReadAttr(attrs, "class"), "pf-screenshots-" + layout);
        var containerStyle = BuildContainerStyle(attrs, "xl", "center");
        containerStyle = AppendStyle(containerStyle, BuildScreenshotsLayoutStyle(layout, columns, gap));

        var sb = new StringBuilder();
        sb.AppendLine($@"<div class=""{System.Web.HttpUtility.HtmlEncode(className)}"" style=""{System.Web.HttpUtility.HtmlEncode(containerStyle)}"">");
        foreach (var item in shots)
        {
            if (item is not IReadOnlyDictionary<string, object?> map)
                continue;

            var src = ReadMapAttr(map, "src", "image", "url", "href");
            if (string.IsNullOrWhiteSpace(src))
                continue;

            var alt = ReadMapAttr(map, "alt", "title");
            var caption = ReadMapAttr(map, "caption", "text", "description");
            var link = ReadMapAttr(map, "link", "full", "target");
            var objectFit = ReadMapAttr(map, "fit");
            if (string.IsNullOrWhiteSpace(objectFit))
                objectFit = "cover";
            var ratio = NormalizeRatio(ReadMapAttr(map, "ratio", "aspect"), string.Empty);
            var width = ReadMapIntAttr(map, "width", "w");
            var height = ReadMapIntAttr(map, "height", "h");
            var size = ReadMapAttr(map, "size");

            var itemClass = JoinClassTokens("pf-screenshot-item", ResolveSizeClass(size));
            var itemStyle = BuildScreenshotsItemStyle(layout, columns, gap, size);
            sb.AppendLine($@"  <div class=""{System.Web.HttpUtility.HtmlEncode(itemClass)}"" style=""{System.Web.HttpUtility.HtmlEncode(itemStyle)}"">");
            sb.AppendLine(RenderScreenshotFigure(
                src,
                alt,
                caption,
                link,
                "pf-screenshot pf-screenshot-collection",
                "margin:0;",
                width,
                height,
                ratio,
                objectFit,
                inCollection: true));
            sb.AppendLine("  </div>");
        }
        sb.AppendLine("</div>");
        return sb.ToString().TrimEnd();
    }

    private static string RenderScreenshotFigure(
        string src,
        string alt,
        string caption,
        string link,
        string className,
        string style,
        int? width,
        int? height,
        string ratio,
        string objectFit,
        bool inCollection)
    {
        var imgStyle = new StringBuilder();
        imgStyle.Append("display:block;width:100%;height:auto;border-radius:12px;background:#0b0b0f;");
        if (!string.IsNullOrWhiteSpace(ratio))
            imgStyle.Append($"aspect-ratio:{ratio};");
        if (!string.IsNullOrWhiteSpace(objectFit))
            imgStyle.Append($"object-fit:{objectFit};");

        var widthAttr = width is > 0 ? $" width=\"{width.Value}\"" : string.Empty;
        var heightAttr = height is > 0 ? $" height=\"{height.Value}\"" : string.Empty;
        var img = $@"<img src=""{System.Web.HttpUtility.HtmlEncode(src)}"" alt=""{System.Web.HttpUtility.HtmlEncode(alt)}"" loading=""lazy"" decoding=""async""{widthAttr}{heightAttr} style=""{System.Web.HttpUtility.HtmlEncode(imgStyle.ToString())}"" />";
        var body = string.IsNullOrWhiteSpace(link)
            ? img
            : $@"<a href=""{System.Web.HttpUtility.HtmlEncode(link)}"" target=""_blank"" rel=""noopener"">{img}</a>";

        var sb = new StringBuilder();
        sb.AppendLine($@"<figure class=""{System.Web.HttpUtility.HtmlEncode(className)}"" style=""{System.Web.HttpUtility.HtmlEncode(style)}"">");
        sb.AppendLine("  " + body);
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var captionStyle = inCollection
                ? "margin-top:.45rem;font-size:.86rem;opacity:.82;"
                : "margin-top:.5rem;font-size:.9rem;opacity:.82;";
            sb.AppendLine($@"  <figcaption style=""{captionStyle}"">{System.Web.HttpUtility.HtmlEncode(caption)}</figcaption>");
        }
        sb.Append("</figure>");
        return sb.ToString();
    }

    private static string BuildScreenshotsLayoutStyle(string layout, int columns, string gap)
    {
        return layout switch
        {
            "masonry" => $"column-count:{columns};column-gap:{gap};",
            "strip" => $"display:grid;grid-auto-flow:column;grid-auto-columns:minmax(min(18rem,100%),1fr);gap:{gap};overflow-x:auto;padding:0 0 .35rem;",
            "stack" => $"display:grid;grid-template-columns:1fr;gap:{gap};",
            _ => $"display:grid;grid-template-columns:repeat({columns},minmax(0,1fr));gap:{gap};"
        };
    }

    private static string BuildScreenshotsItemStyle(string layout, int columns, string gap, string size)
    {
        if (layout.Equals("masonry", StringComparison.OrdinalIgnoreCase))
            return $"break-inside:avoid;page-break-inside:avoid;margin-bottom:{gap};";
        if (layout.Equals("grid", StringComparison.OrdinalIgnoreCase))
        {
            var span = ResolveGridSpan(columns, size);
            return span <= 1 ? "min-width:0;" : $"min-width:0;grid-column:span {span};";
        }

        return "min-width:0;";
    }

    private static int ResolveGridSpan(int columns, string size)
    {
        if (columns <= 1)
            return 1;

        return size.ToLowerInvariant() switch
        {
            "full" => columns,
            "xl" => Math.Min(columns, 3),
            "lg" => Math.Min(columns, 2),
            _ => 1
        };
    }

    private static string BuildContainerStyle(Dictionary<string, string> attrs, string defaultSize, string defaultAlign)
    {
        var size = ReadAttr(attrs, "size");
        if (string.IsNullOrWhiteSpace(size))
            size = defaultSize;

        var align = ReadAttr(attrs, "align");
        if (string.IsNullOrWhiteSpace(align))
            align = defaultAlign;

        var maxWidth = ReadAttr(attrs, "maxWidth", "max-width", "width");
        if (string.IsNullOrWhiteSpace(maxWidth))
        {
            if (ScreenshotWidthBySize.TryGetValue(size, out var width) && width > 0)
                maxWidth = width + "px";
            else
                maxWidth = "100%";
        }
        else if (int.TryParse(maxWidth, out var numeric) && numeric > 0)
        {
            maxWidth = numeric + "px";
        }

        var margin = align.ToLowerInvariant() switch
        {
            "left" => "margin:0 auto 1rem 0;",
            "right" => "margin:0 0 1rem auto;",
            _ => "margin:0 auto 1rem auto;"
        };

        var style = $"max-width:{maxWidth};{margin}";
        return AppendStyle(style, ReadAttr(attrs, "style"));
    }

    private static string ResolveSizeClass(Dictionary<string, string> attrs, string defaultSize)
    {
        var size = ReadAttr(attrs, "size");
        if (string.IsNullOrWhiteSpace(size))
            size = defaultSize;
        return ResolveSizeClass(size);
    }

    private static string ResolveSizeClass(string size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return string.Empty;
        return "pf-size-" + size.Trim().ToLowerInvariant();
    }

    private static string ReadAttr(Dictionary<string, string> attrs, params string[] keys)
    {
        if (attrs is null || keys is null)
            return string.Empty;

        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (!attrs.TryGetValue(key, out var value))
                continue;
            if (string.IsNullOrWhiteSpace(value))
                continue;
            return value.Trim();
        }

        return string.Empty;
    }

    private static bool ReadBoolAttr(Dictionary<string, string> attrs, bool defaultValue, params string[] keys)
    {
        var value = ReadAttr(attrs, keys);
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        if (bool.TryParse(value, out var parsed))
            return parsed;
        if (int.TryParse(value, out var numeric))
            return numeric != 0;
        return defaultValue;
    }

    private static int? ReadIntAttr(Dictionary<string, string> attrs, params string[] keys)
    {
        var value = ReadAttr(attrs, keys);
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string ReadMapAttr(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        if (map is null || keys is null)
            return string.Empty;

        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (!map.TryGetValue(key, out var value))
                continue;
            var text = value?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                continue;
            return text.Trim();
        }

        return string.Empty;
    }

    private static int? ReadMapIntAttr(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        var value = ReadMapAttr(map, keys);
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string ExtractYouTubeVideoId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            return trimmed;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return trimmed;

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.Trim('/');
        if (host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase))
            return path.Split('/')[0];

        if (host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            var query = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(static token => token.Split('=', 2))
                .Where(static parts => parts.Length == 2)
                .ToDictionary(static parts => parts[0], static parts => Uri.UnescapeDataString(parts[1]), StringComparer.OrdinalIgnoreCase);
            if (query.TryGetValue("v", out var fromQuery) && !string.IsNullOrWhiteSpace(fromQuery))
                return fromQuery;

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 &&
                (segments[0].Equals("embed", StringComparison.OrdinalIgnoreCase) ||
                 segments[0].Equals("shorts", StringComparison.OrdinalIgnoreCase)))
                return segments[1];
        }

        return trimmed;
    }

    private static string NormalizeRatio(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim().Replace(':', '/');
        if (trimmed.Contains('/'))
            return trimmed;

        if (double.TryParse(trimmed, out var numeric) && numeric > 0)
            return numeric.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        return fallback;
    }

    private static string JoinClassTokens(params string[] values)
    {
        var tokens = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            var parts = value
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => part.Trim())
                .Where(static part => !string.IsNullOrWhiteSpace(part));
            tokens.AddRange(parts);
        }

        return string.Join(" ", tokens.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string AppendStyle(string current, string extra)
    {
        if (string.IsNullOrWhiteSpace(extra))
            return current;
        if (string.IsNullOrWhiteSpace(current))
            return extra;

        var merged = current.TrimEnd();
        if (!merged.EndsWith(";", StringComparison.Ordinal))
            merged += ";";
        return merged + extra;
    }

    private static string InferMediaKind(string src)
    {
        if (string.IsNullOrWhiteSpace(src))
            return "iframe";

        var value = src.Trim();
        if (value.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            return "youtube";
        if (value.Contains("x.com/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("twitter.com/", StringComparison.OrdinalIgnoreCase))
            return "x";

        var lower = value.ToLowerInvariant();
        if (lower.EndsWith(".mp4", StringComparison.Ordinal) ||
            lower.EndsWith(".webm", StringComparison.Ordinal) ||
            lower.EndsWith(".ogg", StringComparison.Ordinal))
            return "video";
        if (lower.EndsWith(".png", StringComparison.Ordinal) ||
            lower.EndsWith(".jpg", StringComparison.Ordinal) ||
            lower.EndsWith(".jpeg", StringComparison.Ordinal) ||
            lower.EndsWith(".webp", StringComparison.Ordinal) ||
            lower.EndsWith(".gif", StringComparison.Ordinal) ||
            lower.EndsWith(".svg", StringComparison.Ordinal))
            return "image";

        return "iframe";
    }
}
