using System.Text;

namespace PowerForge.Web;

internal static partial class ShortcodeDefaults
{
    private const string MediaMetaCssKey = "extra_css";
    private const string MediaMetaScriptsKey = "extra_scripts";
    private const string MediaBaseCssMarker = "pf-media-base-v1";
    private const string YouTubeLiteScriptMarker = "pf-media-youtube-lite-v1";
    private const string XEmbedScriptMarker = "pf-media-x-embed-v1";

    private const string MediaBaseCss = """
<style>
/* pf-media-base-v1 */
.pf-media,.pf-screenshot,.pf-screenshots{width:100%}
.pf-media-frame iframe,.pf-media-frame img{display:block}
.pf-media-youtube-lite:focus-visible{outline:2px solid #f59e0b;outline-offset:2px}
.pf-media-youtube-lite button{pointer-events:none;backdrop-filter:blur(2px)}
.pf-screenshots-strip{scroll-snap-type:x proximity}
.pf-screenshots-strip .pf-screenshot-item{scroll-snap-align:start}
.pf-screenshot a{display:block}
</style>
""";

    private const string YouTubeLiteBootstrapScript = """
<script>
/* pf-media-youtube-lite-v1 */
(function(){
  if (window.__pfYoutubeLiteInit) return;
  window.__pfYoutubeLiteInit = true;
  function activate(frame){
    if (!frame || frame.dataset.pfYoutubeMounted === "1") return;
    var src = frame.getAttribute("data-pf-youtube-url");
    if (!src) return;
    var title = frame.getAttribute("data-pf-youtube-title") || "YouTube video";
    var iframe = document.createElement("iframe");
    iframe.src = src;
    iframe.title = title;
    iframe.loading = "lazy";
    iframe.referrerPolicy = "strict-origin-when-cross-origin";
    iframe.allow = "accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share";
    iframe.allowFullscreen = true;
    iframe.style.position = "absolute";
    iframe.style.inset = "0";
    iframe.style.width = "100%";
    iframe.style.height = "100%";
    iframe.style.border = "0";
    frame.innerHTML = "";
    frame.appendChild(iframe);
    frame.dataset.pfYoutubeMounted = "1";
  }
  function bind(frame){
    if (!frame || frame.dataset.pfYoutubeBound === "1") return;
    frame.dataset.pfYoutubeBound = "1";
    frame.setAttribute("role", "button");
    frame.setAttribute("tabindex", "0");
    frame.addEventListener("click", function(){ activate(frame); });
    frame.addEventListener("keydown", function(e){
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        activate(frame);
      }
    });
  }
  function init(root){
    var scope = root || document;
    var nodes = scope.querySelectorAll("[data-pf-youtube-lite]");
    for (var i = 0; i < nodes.length; i++) bind(nodes[i]);
  }
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function(){ init(document); });
  } else {
    init(document);
  }
})();
</script>
""";

    private const string XEmbedBootstrapScript = """
<script>
/* pf-media-x-embed-v1 */
(function(){
  if (window.__pfXEmbedsInit) return;
  window.__pfXEmbedsInit = true;
  var twitterScriptRequested = false;
  function requestTwitterScript(onReady){
    if (window.twttr && window.twttr.widgets && typeof window.twttr.widgets.load === "function") {
      onReady();
      return;
    }
    if (!twitterScriptRequested) {
      twitterScriptRequested = true;
      var s = document.createElement("script");
      s.async = true;
      s.src = "https://platform.twitter.com/widgets.js";
      s.charset = "utf-8";
      s.onload = onReady;
      document.head.appendChild(s);
      return;
    }
    var tries = 0;
    var timer = setInterval(function(){
      tries++;
      if (window.twttr && window.twttr.widgets && typeof window.twttr.widgets.load === "function") {
        clearInterval(timer);
        onReady();
      } else if (tries > 80) {
        clearInterval(timer);
      }
    }, 100);
  }
  function hydrate(target){
    requestTwitterScript(function(){
      if (window.twttr && window.twttr.widgets && typeof window.twttr.widgets.load === "function") {
        window.twttr.widgets.load(target || document);
      }
    });
  }
  function attach(node){
    if (!node || node.dataset.pfXBound === "1") return;
    node.dataset.pfXBound = "1";
    if ("IntersectionObserver" in window) {
      var observer = new IntersectionObserver(function(entries){
        for (var i = 0; i < entries.length; i++) {
          if (!entries[i].isIntersecting) continue;
          observer.disconnect();
          hydrate(node);
          break;
        }
      }, { rootMargin: "350px 0px" });
      observer.observe(node);
      return;
    }
    hydrate(node);
  }
  function init(root){
    var scope = root || document;
    var nodes = scope.querySelectorAll("[data-pf-x-embed]");
    for (var i = 0; i < nodes.length; i++) attach(nodes[i]);
  }
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function(){ init(document); });
  } else {
    init(document);
  }
})();
</script>
""";

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
        EnsureMediaBaseCss(context);
        var kind = ReadAttr(attrs, "kind", "type", "provider");
        if (string.IsNullOrWhiteSpace(kind))
            kind = InferMediaKind(ReadAttr(attrs, "src", "url", "href"));

        return kind.ToLowerInvariant() switch
        {
            "youtube" => RenderYouTube(context, attrs),
            "x" or "tweet" => RenderXPost(context, attrs),
            "video" => RenderVideo(context, attrs),
            "map" or "google-map" or "googlemaps" => RenderMap(context, attrs),
            "iframe" or "embed" => RenderIFrame(context, attrs),
            "image" or "screenshot" => RenderScreenshot(context, attrs),
            "screenshots" or "gallery" => RenderScreenshots(context, attrs),
            _ => string.Empty
        };
    }

    internal static string RenderYouTube(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        EnsureMediaBaseCss(context);
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
        var lite = ReadBoolAttr(attrs, defaultValue: true, "lite");
        var ratio = NormalizeRatio(ReadAttr(attrs, "ratio", "aspect"), "16/9");
        var title = ReadAttr(attrs, "title");
        if (string.IsNullOrWhiteSpace(title))
            title = "YouTube video";
        var poster = ReadAttr(attrs, "poster", "thumbnail", "thumb");
        if (string.IsNullOrWhiteSpace(poster))
            poster = $"https://i.ytimg.com/vi/{Uri.EscapeDataString(videoId)}/hqdefault.jpg";
        var loading = NormalizeLoading(ReadAttr(attrs, "loading"), "lazy");
        var fetchPriority = NormalizeFetchPriority(ReadAttr(attrs, "fetchpriority", "priority"), "low");
        var buttonLabel = ReadAttr(attrs, "buttonLabel", "button");
        if (string.IsNullOrWhiteSpace(buttonLabel))
            buttonLabel = "Play video";

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

        if (lite)
        {
            EnsurePageScript(context, YouTubeLiteScriptMarker, YouTubeLiteBootstrapScript);
            return $@"<div class=""{System.Web.HttpUtility.HtmlEncode(className)}"" style=""{System.Web.HttpUtility.HtmlEncode(style)}"">
  <div class=""pf-media-frame pf-media-youtube-lite"" data-pf-youtube-lite data-pf-youtube-url=""{System.Web.HttpUtility.HtmlEncode(url)}"" data-pf-youtube-title=""{System.Web.HttpUtility.HtmlEncode(title)}"" style=""position:relative;width:100%;aspect-ratio:{System.Web.HttpUtility.HtmlEncode(ratio)};overflow:hidden;border-radius:14px;background:#0b0b0f;cursor:pointer;"">
    <img src=""{System.Web.HttpUtility.HtmlEncode(poster)}"" alt=""{System.Web.HttpUtility.HtmlEncode(title)}"" loading=""{System.Web.HttpUtility.HtmlEncode(loading)}"" decoding=""async"" fetchpriority=""{System.Web.HttpUtility.HtmlEncode(fetchPriority)}"" style=""position:absolute;inset:0;width:100%;height:100%;object-fit:cover;display:block;"" />
    <span aria-hidden=""true"" style=""position:absolute;inset:0;background:linear-gradient(180deg,rgba(0,0,0,.1) 0%,rgba(0,0,0,.55) 100%);""></span>
    <button type=""button"" aria-label=""{System.Web.HttpUtility.HtmlEncode(buttonLabel)}"" style=""position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);padding:.58rem .95rem;border-radius:999px;border:1px solid rgba(255,255,255,.34);background:rgba(0,0,0,.62);color:#fff;font-weight:600;cursor:pointer;"">{System.Web.HttpUtility.HtmlEncode(buttonLabel)}</button>
  </div>
  <noscript>
    <div class=""pf-media-frame"" style=""position:relative;width:100%;aspect-ratio:{System.Web.HttpUtility.HtmlEncode(ratio)};overflow:hidden;border-radius:14px;background:#0b0b0f;"">
      <iframe src=""{System.Web.HttpUtility.HtmlEncode(url)}"" title=""{System.Web.HttpUtility.HtmlEncode(title)}"" loading=""lazy"" referrerpolicy=""strict-origin-when-cross-origin"" allow=""accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"" allowfullscreen style=""position:absolute;inset:0;width:100%;height:100%;border:0;""></iframe>
    </div>
  </noscript>
</div>";
        }

        return $@"<div class=""{System.Web.HttpUtility.HtmlEncode(className)}"" style=""{System.Web.HttpUtility.HtmlEncode(style)}"">
  <div class=""pf-media-frame"" style=""position:relative;width:100%;aspect-ratio:{System.Web.HttpUtility.HtmlEncode(ratio)};overflow:hidden;border-radius:14px;background:#0b0b0f;"">
    <iframe src=""{System.Web.HttpUtility.HtmlEncode(url)}"" title=""{System.Web.HttpUtility.HtmlEncode(title)}"" loading=""{System.Web.HttpUtility.HtmlEncode(loading)}"" referrerpolicy=""strict-origin-when-cross-origin"" allow=""accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"" allowfullscreen style=""position:absolute;inset:0;width:100%;height:100%;border:0;""></iframe>
  </div>
</div>";
    }

    internal static string RenderXPost(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        EnsureMediaBaseCss(context);
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

        EnsurePageScript(context, XEmbedScriptMarker, XEmbedBootstrapScript);

        return $@"<div class=""{System.Web.HttpUtility.HtmlEncode(className)}"" style=""{System.Web.HttpUtility.HtmlEncode(style)}"">
  <blockquote class=""twitter-tweet"" data-pf-x-embed{extraData}>
    <a href=""{System.Web.HttpUtility.HtmlEncode(url)}"">View post on X</a>
  </blockquote>
</div>";
    }

    internal static string RenderVideo(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        EnsureMediaBaseCss(context);
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
        EnsureMediaBaseCss(context);
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

    internal static string RenderMap(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        EnsureMediaBaseCss(context);
        var src = ResolveMapEmbedSource(attrs);
        if (string.IsNullOrWhiteSpace(src))
            return string.Empty;

        var title = ReadAttr(attrs, "title", "name", "label");
        if (string.IsNullOrWhiteSpace(title))
            title = "Map";

        var ratio = NormalizeRatio(ReadAttr(attrs, "ratio", "aspect"), "16/9");
        var loading = NormalizeLoading(ReadAttr(attrs, "loading"), "lazy");
        var referrerPolicy = ReadAttr(attrs, "referrerpolicy", "referrerPolicy");
        if (string.IsNullOrWhiteSpace(referrerPolicy))
            referrerPolicy = "no-referrer-when-downgrade";

        var caption = ReadAttr(attrs, "caption", "text", "description");
        var allowFullScreen = ReadBoolAttr(attrs, defaultValue: true, "allowfullscreen", "allowFullScreen");
        var className = JoinClassTokens("pf-media pf-media-map", ReadAttr(attrs, "class"), ResolveSizeClass(attrs, "xl"));
        var style = BuildContainerStyle(attrs, "xl", "center");

        var sb = new StringBuilder();
        sb.AppendLine($@"<figure class=""{System.Web.HttpUtility.HtmlEncode(className)}"" style=""{System.Web.HttpUtility.HtmlEncode(style)}"">");
        sb.AppendLine(
            $@"  <div class=""pf-media-frame"" style=""position:relative;width:100%;aspect-ratio:{System.Web.HttpUtility.HtmlEncode(ratio)};overflow:hidden;border-radius:14px;background:#0b0b0f;"">");
        sb.Append($@"    <iframe src=""{System.Web.HttpUtility.HtmlEncode(src)}"" title=""{System.Web.HttpUtility.HtmlEncode(title)}"" loading=""{System.Web.HttpUtility.HtmlEncode(loading)}"" referrerpolicy=""{System.Web.HttpUtility.HtmlEncode(referrerPolicy)}""");
        if (allowFullScreen)
            sb.Append(" allowfullscreen");
        sb.AppendLine(@" style=""position:absolute;inset:0;width:100%;height:100%;border:0;""></iframe>");
        sb.AppendLine("  </div>");
        if (!string.IsNullOrWhiteSpace(caption))
            sb.AppendLine($@"  <figcaption style=""margin-top:.5rem;font-size:.9rem;opacity:.82;"">{System.Web.HttpUtility.HtmlEncode(caption)}</figcaption>");
        sb.Append("</figure>");
        return sb.ToString();
    }

    private static string ResolveMapEmbedSource(Dictionary<string, string> attrs)
    {
        var src = ReadAttr(attrs, "src", "url", "href", "embed");
        if (!string.IsNullOrWhiteSpace(src))
            return src;

        var query = ReadAttr(attrs, "query", "address", "location", "place");
        if (!string.IsNullOrWhiteSpace(query))
            return "https://www.google.com/maps?q=" + Uri.EscapeDataString(query) + "&output=embed";

        var lat = ReadAttr(attrs, "lat", "latitude");
        var lng = ReadAttr(attrs, "lng", "lon", "long", "longitude");
        if (!string.IsNullOrWhiteSpace(lat) && !string.IsNullOrWhiteSpace(lng))
        {
            var zoom = ReadIntAttr(attrs, "zoom", "z");
            var builder = new StringBuilder("https://www.google.com/maps?q=");
            builder.Append(Uri.EscapeDataString(lat + "," + lng));
            if (zoom is > 0)
                builder.Append("&z=").Append(zoom.Value);
            builder.Append("&output=embed");
            return builder.ToString();
        }

        return string.Empty;
    }

    internal static string RenderScreenshot(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        EnsureMediaBaseCss(context);
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
        var srcSet = ReadAttr(attrs, "srcset");
        var sizes = ReadAttr(attrs, "sizes");
        var loading = NormalizeLoading(ReadAttr(attrs, "loading"), "lazy");
        var decoding = NormalizeDecoding(ReadAttr(attrs, "decoding"), "async");
        var fetchPriority = NormalizeFetchPriority(ReadAttr(attrs, "fetchpriority", "priority"), "auto");

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
            srcSet,
            sizes,
            loading,
            decoding,
            fetchPriority,
            inCollection: false);
    }

    internal static string RenderScreenshots(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        EnsureMediaBaseCss(context);
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
            var srcSet = ReadMapAttr(map, "srcset");
            var sizes = ReadMapAttr(map, "sizes");
            var loading = NormalizeLoading(ReadMapAttr(map, "loading"), "lazy");
            var decoding = NormalizeDecoding(ReadMapAttr(map, "decoding"), "async");
            var fetchPriority = NormalizeFetchPriority(ReadMapAttr(map, "fetchpriority", "priority"), "auto");
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
                srcSet,
                sizes,
                loading,
                decoding,
                fetchPriority,
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
        string srcSet,
        string sizes,
        string loading,
        string decoding,
        string fetchPriority,
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
        var srcSetAttr = string.IsNullOrWhiteSpace(srcSet) ? string.Empty : $@" srcset=""{System.Web.HttpUtility.HtmlEncode(srcSet)}""";
        var sizesAttr = string.IsNullOrWhiteSpace(sizes) ? string.Empty : $@" sizes=""{System.Web.HttpUtility.HtmlEncode(sizes)}""";
        var loadingAttr = string.IsNullOrWhiteSpace(loading) ? string.Empty : $@" loading=""{System.Web.HttpUtility.HtmlEncode(loading)}""";
        var decodingAttr = string.IsNullOrWhiteSpace(decoding) ? string.Empty : $@" decoding=""{System.Web.HttpUtility.HtmlEncode(decoding)}""";
        var fetchPriorityAttr = string.IsNullOrWhiteSpace(fetchPriority) || fetchPriority.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $@" fetchpriority=""{System.Web.HttpUtility.HtmlEncode(fetchPriority)}""";
        var img = $@"<img src=""{System.Web.HttpUtility.HtmlEncode(src)}"" alt=""{System.Web.HttpUtility.HtmlEncode(alt)}""{loadingAttr}{decodingAttr}{fetchPriorityAttr}{srcSetAttr}{sizesAttr}{widthAttr}{heightAttr} style=""{System.Web.HttpUtility.HtmlEncode(imgStyle.ToString())}"" />";
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
        if (value.Contains("google.com/maps", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("maps.app.goo.gl", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("openstreetmap.org", StringComparison.OrdinalIgnoreCase))
            return "map";

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

    private static void EnsureMediaBaseCss(ShortcodeRenderContext context)
    {
        EnsurePageMetaBlock(context, MediaMetaCssKey, MediaBaseCssMarker, MediaBaseCss);
    }

    private static void EnsurePageScript(ShortcodeRenderContext context, string marker, string scriptHtml)
    {
        EnsurePageMetaBlock(context, MediaMetaScriptsKey, marker, scriptHtml);
    }

    private static void EnsurePageMetaBlock(ShortcodeRenderContext context, string metaKey, string marker, string html)
    {
        if (context?.FrontMatter?.Meta is null ||
            string.IsNullOrWhiteSpace(metaKey) ||
            string.IsNullOrWhiteSpace(marker) ||
            string.IsNullOrWhiteSpace(html))
            return;

        var meta = context.FrontMatter.Meta;
        var existing = GetMetaString(meta, metaKey);
        if (!string.IsNullOrWhiteSpace(existing) &&
            existing.Contains(marker, StringComparison.OrdinalIgnoreCase))
            return;

        AppendMetaHtml(meta, metaKey, html);
    }

    private static void AppendMetaHtml(Dictionary<string, object?> meta, string key, string html)
    {
        if (meta is null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(html))
            return;

        if (!meta.TryGetValue(key, out var existing) || existing is null)
        {
            meta[key] = html.Trim();
            return;
        }

        var prior = existing switch
        {
            string s => s,
            IEnumerable<object?> list => string.Join(Environment.NewLine, list.Select(static i => i?.ToString()).Where(static s => !string.IsNullOrWhiteSpace(s))),
            _ => existing.ToString() ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(prior))
        {
            meta[key] = html.Trim();
            return;
        }

        if (prior.Contains(html, StringComparison.Ordinal))
            return;

        meta[key] = prior.TrimEnd() + Environment.NewLine + html.Trim();
    }

    private static string GetMetaString(Dictionary<string, object?> meta, string key)
    {
        if (meta is null || string.IsNullOrWhiteSpace(key))
            return string.Empty;
        if (!meta.TryGetValue(key, out var value) || value is null)
            return string.Empty;

        return value switch
        {
            string s => s,
            IEnumerable<object?> list => string.Join(Environment.NewLine, list.Select(static i => i?.ToString()).Where(static s => !string.IsNullOrWhiteSpace(s))),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string NormalizeLoading(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "lazy" or "eager" ? normalized : fallback;
    }

    private static string NormalizeDecoding(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "sync" or "async" or "auto" ? normalized : fallback;
    }

    private static string NormalizeFetchPriority(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "high" or "low" or "auto" ? normalized : fallback;
    }
}
