using System.Net;
using System.Net.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ImageMagick;
using ImageMagick.Drawing;

namespace PowerForge.Web;

internal static partial class WebSocialCardGenerator
{
    // Bump whenever raster-visible rendering or media sanitization changes.
    internal const string RendererVersion = "social-card-renderer-v9";
    internal const int MaxRemoteImageBytes = 10 * 1024 * 1024;
    internal const int MaxRemoteImageCacheEntries = 128;
    internal const long MaxRemoteImageCacheBytes = 100L * 1024 * 1024;

    private static readonly HttpClient SocialImageHttpClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    }, disposeHandler: true)
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> RemoteImageByteCache = new(StringComparer.Ordinal);
    // Keys are limited to normalized title font, font size, and first glyph, so this stays tiny in normal builds.
    private static readonly ConcurrentDictionary<string, int> TitleGlyphInsetCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> CenteredTextOffsetCache = new(StringComparer.Ordinal);

    internal static byte[]? RenderPng(SocialCardRenderOptions options)
    {
        var embeddedOptions = CloneRenderOptions(options, embedReferencedMediaInSvg: true);
        var embeddedState = CreateState(embeddedOptions);
        var embeddedSvg = RenderSvg(embeddedState);
        if (!string.IsNullOrWhiteSpace(embeddedSvg))
        {
            var externalPng = TryRenderPngWithExternalImageMagick(embeddedSvg, embeddedState.Width, embeddedState.Height);
            if (externalPng is { Length: > 0 })
                return externalPng;
        }

        if (OperatingSystem.IsLinux())
        {
            Trace.TraceWarning("Social card PNG render skipped: external ImageMagick is unavailable or failed on Linux, and in-process SVG rendering is disabled to avoid native renderer crashes.");
            return null;
        }

        var rasterOptions = CloneRenderOptions(options, embedReferencedMediaInSvg: false);
        var state = CreateState(rasterOptions);
        var svg = RenderSvg(state);
        if (string.IsNullOrWhiteSpace(svg))
            return null;

        try
        {
            EnsureMagickFontConfigInitialized();
            var settings = new MagickReadSettings
            {
                Width = (uint)Math.Clamp(options.Width, 600, 2400),
                Height = (uint)Math.Clamp(options.Height, 315, 1400),
                Format = MagickFormat.Svg
            };

            using var image = new MagickImage(Encoding.UTF8.GetBytes(svg), settings);
            CompositeMedia(image, state);
            image.Format = MagickFormat.Png;
            image.Strip();
            using var stream = new MemoryStream();
            image.Write(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Social card PNG render failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static byte[]? TryRenderPngWithExternalImageMagick(string svg, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(svg))
            return null;

        var command = ResolveExternalImageMagickCommand();
        if (command is null)
            return null;

        var tempRoot = Path.Combine(Path.GetTempPath(), "powerforge-social-card-" + Guid.NewGuid().ToString("N"));
        var svgPath = Path.Combine(tempRoot, "card.svg");
        var pngPath = Path.Combine(tempRoot, "card.png");

        try
        {
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(svgPath, svg, Encoding.UTF8);

            var arguments = new[] { "-background", "none", "-size", $"{width}x{height}", svgPath, $"PNG32:{pngPath}" };
            var startInfo = new ProcessStartInfo(command.Executable)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            var errorTask = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExit(30_000);
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort cleanup for a timed-out renderer process.
                }

                Trace.TraceWarning("Social card external ImageMagick render timed out.");
                return null;
            }

            if (process.ExitCode != 0 || !File.Exists(pngPath))
            {
                var error = errorTask.GetAwaiter().GetResult().Trim();
                if (!string.IsNullOrWhiteSpace(error))
                    Trace.TraceWarning($"Social card external ImageMagick render failed: {error}");
                return null;
            }

            return File.ReadAllBytes(pngPath);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Social card external ImageMagick render failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Temp cleanup is best-effort.
            }
        }
    }

    private static ExternalImageMagickCommand? ResolveExternalImageMagickCommand()
    {
        if (OperatingSystem.IsWindows())
            return null;

        var magick = FindExecutableOnPath("magick");
        if (!string.IsNullOrWhiteSpace(magick))
            return new ExternalImageMagickCommand(magick);

        var convert = FindExecutableOnPath("convert");
        if (!string.IsNullOrWhiteSpace(convert))
            return new ExternalImageMagickCommand(convert);

        return null;
    }

    private static string? FindExecutableOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    internal static string RenderSvg(SocialCardRenderOptions options)
    {
        var state = CreateState(options);
        return RenderSvg(state);
    }

    private static string RenderSvg(SocialCardRenderState state)
    {
        var svg = new StringBuilder();
        svg.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" xmlns:xlink=""http://www.w3.org/1999/xlink"" width=""{state.Width}"" height=""{state.Height}"" viewBox=""0 0 {state.Width} {state.Height}"">");
        svg.AppendLine($@"  <!-- renderer:{RendererVersion} layout:{state.LayoutKey} style:{state.StyleKey} variant:{state.VariantKey} -->");
        svg.AppendLine(@"  <defs>");
        AppendDefs(svg, state);
        if (string.Equals(state.LayoutKey, "inline-image", StringComparison.OrdinalIgnoreCase))
        {
            var media = GetInlineMediaFrame(state);
            svg.AppendLine(@"    <clipPath id=""mediaClip"">");
            svg.AppendLine($@"      <rect x=""{media.X}"" y=""{media.Y}"" width=""{media.Width}"" height=""{media.Height}"" rx=""{media.Radius}"" ry=""{media.Radius}""/>");
            svg.AppendLine(@"    </clipPath>");
        }
        svg.AppendLine(@"  </defs>");
        AppendBackground(svg, state);

        switch (state.LayoutKey)
        {
            case "spotlight":
                AppendSpotlightLayout(svg, state);
                break;
            case "shelf":
                AppendShelfLayout(svg, state);
                break;
            case "reference":
                AppendReferenceLayout(svg, state);
                break;
            case "inline-image":
                AppendInlineImageLayout(svg, state);
                break;
            case "connect":
                AppendConnectLayout(svg, state);
                break;
            default:
                AppendEditorialOrProductLayout(svg, state);
                break;
        }

        svg.AppendLine(@"</svg>");
        return svg.ToString();
    }

    private static SocialCardRenderState CreateState(SocialCardRenderOptions options)
    {
        var width = Math.Clamp(options.Width, 600, 2400);
        var height = Math.Clamp(options.Height, 315, 1400);
        var title = string.IsNullOrWhiteSpace(options.Title) ? "PowerForge Web" : options.Title!;
        var description = string.IsNullOrWhiteSpace(options.Description)
            ? "Static content with docs, API references, editorial streams, and project landing pages."
            : options.Description!;
        var eyebrow = string.IsNullOrWhiteSpace(options.Eyebrow) ? "PowerForge" : options.Eyebrow!;
        var badge = string.IsNullOrWhiteSpace(options.Badge) ? "PAGE" : options.Badge!;
        var footerLabel = string.IsNullOrWhiteSpace(options.FooterLabel) ? "/" : options.FooterLabel!;
        var styleKey = NormalizeStyle(options.StyleKey) ?? ClassifyStyle(badge, footerLabel);
        var variantKey = NormalizeVariant(options.VariantKey) ?? ClassifyVariant(styleKey, badge, footerLabel);
        var normalizedBadge = NormalizeBadgeLabel(badge, footerLabel, styleKey, variantKey);
        var normalizedFooter = NormalizeFooterLabel(footerLabel, normalizedBadge, styleKey);
        var hasRenderableInlineImage = IsRenderableImageSource(options.InlineImageDataUri, options.AllowRemoteMediaFetch);
        var layoutKey = ResolveLayoutKey(styleKey, variantKey, hasRenderableInlineImage);
        var frameInset = ResolveTokenPixels(options.ThemeTokens, width, height, 0, 0, "socialCard", "frameInset");
        var panelInset = ResolveTokenPixels(options.ThemeTokens, width, height, 0, 0, "socialCard", "panelInset");
        var contentPadding = ResolveTokenPixels(options.ThemeTokens, width, height, 28, 18, "socialCard", "contentPadding");
        var safeMarginX = Math.Max(
            frameInset + panelInset + contentPadding,
            ResolveTokenPixels(options.ThemeTokens, width, height, 72, 40, "socialCard", "safeMarginX"));
        var safeMarginY = Math.Max(
            frameInset + panelInset + contentPadding,
            ResolveTokenPixels(options.ThemeTokens, width, height, 72, 40, "socialCard", "safeMarginY"));

        return new SocialCardRenderState
        {
            Width = width,
            Height = height,
            Title = title,
            Description = description,
            Eyebrow = eyebrow,
            Badge = normalizedBadge,
            FooterLabel = normalizedFooter,
            StyleKey = styleKey,
            VariantKey = variantKey,
            LayoutKey = layoutKey,
            Palette = SelectPalette(styleKey, string.Join("|", styleKey, variantKey, title, description, eyebrow, normalizedBadge, normalizedFooter, width, height), options.ThemeTokens, options.ColorScheme),
            Typography = ResolveTypography(options.ThemeTokens, options.PreferRasterSafeFonts),
            ThemeTokens = options.ThemeTokens,
            LogoDataUri = options.LogoDataUri,
            InlineImageDataUri = options.InlineImageDataUri,
            Metrics = NormalizeSocialCardMetrics(options.Metrics),
            AllowRemoteMediaFetch = options.AllowRemoteMediaFetch,
            EmbedReferencedMediaInSvg = options.EmbedReferencedMediaInSvg,
            CtaLabel = ResolveCtaLabel(styleKey, normalizedBadge),
            FrameInset = frameInset,
            PanelInset = panelInset,
            ContentPadding = contentPadding,
            FrameRadius = ResolveRadiusPixels(options.ThemeTokens, width, height, 24, 0, "frameRadius"),
            PanelRadius = ResolveRadiusPixels(options.ThemeTokens, width, height, 16, 8, "panelRadius"),
            SafeMarginX = safeMarginX,
            SafeMarginY = safeMarginY
        };
    }

    private static string ResolveLayoutKey(string styleKey, string variantKey, bool hasInlineImage)
    {
        if (string.Equals(variantKey, "spotlight", StringComparison.OrdinalIgnoreCase))
            return "spotlight";
        if (string.Equals(variantKey, "shelf", StringComparison.OrdinalIgnoreCase))
            return "shelf";
        if (string.Equals(variantKey, "reference", StringComparison.OrdinalIgnoreCase))
            return "reference";
        if (string.Equals(variantKey, "connect", StringComparison.OrdinalIgnoreCase))
            return "connect";
        if (string.Equals(variantKey, "metrics", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(variantKey, "timeline", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(variantKey, "feed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(variantKey, "code", StringComparison.OrdinalIgnoreCase))
        {
            return "product";
        }
        if (string.Equals(variantKey, "inline-image", StringComparison.OrdinalIgnoreCase))
            return hasInlineImage ? "inline-image" : "editorial";

        return styleKey switch
        {
            "home" => "spotlight",
            "docs" => "shelf",
            "api" => "reference",
            "contact" => "connect",
            "examples" => "product",
            "downloads" => "product",
            "release" => "product",
            "feed" => "product",
            "benchmark" => "product",
            "code" => "product",
            "blog" when hasInlineImage => "inline-image",
            "blog" => "editorial",
            _ => "product"
        };
    }

    internal static bool IsRenderableImageSource(string? source, bool allowRemoteMediaFetch, Func<string, byte[]?>? remoteFetcher = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IsRemoteMediaSource(source))
            return GetRemoteImageBytes(source, allowRemoteMediaFetch, remoteFetcher) is { Length: > 0 };

        if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri) && absoluteUri.IsFile)
            return File.Exists(absoluteUri.LocalPath);

        return File.Exists(source);
    }

    private static string ResolveCtaLabel(string styleKey, string badge)
    {
        if (string.Equals(styleKey, "contact", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(badge, "CONTACT", StringComparison.OrdinalIgnoreCase))
            return "Get in Touch";
        if (string.Equals(styleKey, "api", StringComparison.OrdinalIgnoreCase))
            return "Explore API";
        if (string.Equals(styleKey, "docs", StringComparison.OrdinalIgnoreCase))
            return "Read Docs";
        return "Learn More";
    }

    private static SocialCardRenderOptions CloneRenderOptions(SocialCardRenderOptions options, bool embedReferencedMediaInSvg)
    {
        return new SocialCardRenderOptions
        {
            Title = options.Title,
            Description = options.Description,
            Eyebrow = options.Eyebrow,
            Badge = options.Badge,
            FooterLabel = options.FooterLabel,
            Width = options.Width,
            Height = options.Height,
            StyleKey = options.StyleKey,
            VariantKey = options.VariantKey,
            ThemeTokens = options.ThemeTokens,
            LogoDataUri = options.LogoDataUri,
            InlineImageDataUri = options.InlineImageDataUri,
            Metrics = options.Metrics,
            ColorScheme = options.ColorScheme,
            AllowRemoteMediaFetch = options.AllowRemoteMediaFetch,
            EmbedReferencedMediaInSvg = embedReferencedMediaInSvg,
            PreferRasterSafeFonts = !embedReferencedMediaInSvg
        };
    }

    // ── Defs & Background ─────────────────────────────────────────────

    private static void AppendDefs(StringBuilder svg, SocialCardRenderState state)
    {
        svg.AppendLine(@"    <linearGradient id=""bg"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""100%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{state.Palette.BackgroundStart}""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{state.Palette.BackgroundEnd}""/>");
        svg.AppendLine(@"    </linearGradient>");
    }

    private static void AppendBackground(StringBuilder svg, SocialCardRenderState state)
    {
        var accentBarHeight = GetScaledPixels(state.Width, state.Height, 6, 4);
        svg.AppendLine($@"  <rect width=""{state.Width}"" height=""{state.Height}"" fill=""url(#bg)""/>");

        if (state.FrameInset > 0)
        {
            var frameWidth = Math.Max(1, state.Width - (state.FrameInset * 2));
            var frameHeight = Math.Max(1, state.Height - (state.FrameInset * 2));
            svg.AppendLine($@"  <rect x=""{state.FrameInset}"" y=""{state.FrameInset}"" width=""{frameWidth}"" height=""{frameHeight}"" rx=""{state.FrameRadius}"" fill=""{state.Palette.Surface}"" fill-opacity=""0.06"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.16""/>");
        }

        if (state.FrameInset > 0 || state.PanelInset > 0)
        {
            var panel = GetPanelRect(state);
            svg.AppendLine($@"  <rect x=""{panel.X}"" y=""{panel.Y}"" width=""{panel.Width}"" height=""{panel.Height}"" rx=""{panel.Radius}"" fill=""{state.Palette.Surface}"" fill-opacity=""0.12"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.12""/>");
        }

        // Bottom accent bar — full width, solid accent color
        svg.AppendLine($@"  <rect x=""0"" y=""{state.Height - accentBarHeight}"" width=""{state.Width}"" height=""{accentBarHeight}"" fill=""{state.Palette.Accent}""/>");
    }

    // ── Shared layout helpers ──────────────────────────────────────────

    private static void AppendBadge(StringBuilder svg, SocialCardRenderState state, int x, int y)
    {
        var badgeText = state.Badge.ToUpperInvariant();
        var fontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 14, 10, "socialCard", "badgeFontSize");
        var height = GetScaledPixels(state.Width, state.Height, 40, 26);
        var paddingX = GetScaledPixels(state.Width, state.Height, 18, 12);
        var textWidth = EstimateTextWidth(badgeText, fontSize, glyphFactor: 0.6);
        var width = textWidth + (paddingX * 2);
        var radius = Math.Max(GetScaledPixels(state.Width, state.Height, 6, 4), height / 6);
        var textY = GetCenteredTextBaseline(y, height, fontSize);
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{width}"" height=""{height}"" rx=""{radius}"" fill=""{state.Palette.Accent}""/>");
        svg.AppendLine($@"  <text x=""{x + (width / 2)}"" y=""{textY}"" fill=""{state.Palette.BackgroundStart}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.BadgeFontFamily)}"" font-weight=""800"" letter-spacing=""0.6"" text-anchor=""middle"">{EscapeXml(badgeText)}</text>");
    }

    private static void AppendEyebrowText(StringBuilder svg, SocialCardRenderState state, int x, int y)
    {
        var fontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "eyebrowFontSize");
        svg.AppendLine($@"  <text x=""{x}"" y=""{y}"" fill=""{state.Palette.Accent}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.EyebrowFontFamily)}"" font-weight=""700"">{EscapeXml(TrimSingleLine(state.Eyebrow, 56))}</text>");
    }

    private static void AppendTitle(StringBuilder svg, SocialCardRenderState state, IReadOnlyList<string> lines, int x, int y, int fontSize, int lineHeight)
    {
        var titleX = x + ResolveTitleOpticalOffset(state, fontSize, lines.Count > 0 ? lines[0] : state.Title);
        for (var i = 0; i < lines.Count; i++)
            svg.AppendLine($@"  <text x=""{titleX}"" y=""{y + (i * lineHeight)}"" fill=""{state.Palette.TextPrimary}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.TitleFontFamily)}"" font-weight=""800"">{EscapeXml(lines[i])}</text>");
    }

    private static void AppendDescription(StringBuilder svg, SocialCardRenderState state, IReadOnlyList<string> lines, int x, int y, int fontSize, int lineHeight)
    {
        for (var i = 0; i < lines.Count; i++)
            svg.AppendLine($@"  <text x=""{x}"" y=""{y + (i * lineHeight)}"" fill=""{state.Palette.TextSecondary}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.BodyFontFamily)}"" font-weight=""500"">{EscapeXml(lines[i])}</text>");
    }

    private static void AppendFooterRoute(StringBuilder svg, SocialCardRenderState state, int x, int y)
    {
        var label = TrimSingleLine(state.FooterLabel, 64).Trim();
        if (string.IsNullOrWhiteSpace(label) || label == "/")
            return;
        var fontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 11, "socialCard", "footerFontSize");
        svg.AppendLine($@"  <text x=""{x}"" y=""{y}"" fill=""{state.Palette.TextSecondary}"" fill-opacity=""0.6"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.FooterFontFamily)}"" font-weight=""500"">{EscapeXml(label)}</text>");
    }

    private static int ResolveTitleOpticalOffset(SocialCardRenderState state, int fontSize, string? titleLine)
    {
        var configured = ReadThemeToken(state.ThemeTokens, "socialCard", "titleOpticalOffset");
        if (TryParsePixelishInt(configured, out var value))
            return value;

        var offsetMode = ReadThemeToken(state.ThemeTokens, "socialCard", "titleOpticalOffsetMode");
        if (string.Equals(offsetMode, "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(offsetMode, "off", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var firstGlyph = GetLeadingTitleGlyph(titleLine);
        if (firstGlyph is null)
            return 0;

        var fontFamily = NormalizeFontFamilyForRaster(state.Typography.TitleFontFamily, state.Typography.TitleFontFamily);
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{fontFamily}|{fontSize}|{firstGlyph.Value}");

        var measuredOffset = TitleGlyphInsetCache.GetOrAdd(
            cacheKey,
            _ => MeasureRenderedTitleGlyphInset(fontFamily, fontSize, firstGlyph.Value));
        var targetInkInset = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 1, 0, "socialCard", "titleInkAlignInset");
        return measuredOffset + targetInkInset;
    }

    private static char? GetLeadingTitleGlyph(string? titleLine)
    {
        if (string.IsNullOrWhiteSpace(titleLine))
            return null;

        foreach (var character in titleLine)
        {
            if (char.IsLetterOrDigit(character))
                return char.ToUpperInvariant(character);
        }

        return null;
    }

    private static int MeasureRenderedTitleGlyphInset(string fontFamily, int fontSize, char glyph)
    {
        try
        {
            EnsureMagickFontConfigInitialized();
            const int startX = 96;
            const int alphaThreshold = 12;
            var canvasWidth = Math.Max(256, fontSize * 3);
            var canvasHeight = Math.Max(256, fontSize * 3);
            var baselineY = Math.Min(canvasHeight - 32, Math.Max(fontSize * 2, 128));

            using var image = new MagickImage(MagickColors.Transparent, (uint)canvasWidth, (uint)canvasHeight);
            new Drawables()
                .Font(fontFamily, FontStyleType.Normal, FontWeight.ExtraBold, FontStretch.Normal)
                .FontPointSize(fontSize)
                .FillColor(MagickColors.White)
                .Text(startX, baselineY, glyph.ToString(CultureInfo.InvariantCulture))
                .Draw(image);

            var pixels = image.GetPixels().ToByteArray(PixelMapping.RGBA);
            if (pixels is null || pixels.Length == 0)
                return DefaultTitleGlyphInset();
            var width = (int)image.Width;
            var height = (int)image.Height;
            var minX = width;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var alpha = pixels[((y * width) + x) * 4 + 3];
                    if (alpha > alphaThreshold && x < minX)
                        minX = x;
                }
            }

            if (minX >= width)
                return DefaultTitleGlyphInset();

            return Math.Clamp(startX - minX, -fontSize / 2, fontSize / 2);
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"Social card title glyph measurement fell back to default inset: {ex.GetType().Name}: {ex.Message}");
            return DefaultTitleGlyphInset();
        }
    }

    private static int DefaultTitleGlyphInset()
    {
        return 0;
    }

    private static int ResolveCenteredTextInkOffset(string fontFamily, int fontSize, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || fontSize <= 0)
            return 0;

        var normalizedFontFamily = NormalizeFontFamilyForRaster(fontFamily, fontFamily);
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{normalizedFontFamily}|{fontSize}|{text}");

        return CenteredTextOffsetCache.GetOrAdd(
            cacheKey,
            _ => MeasureCenteredTextInkOffset(normalizedFontFamily, fontSize, text));
    }

    private static int MeasureCenteredTextInkOffset(string fontFamily, int fontSize, string text)
    {
        try
        {
            EnsureMagickFontConfigInitialized();
            const int startX = 96;
            const int alphaThreshold = 12;
            var canvasWidth = Math.Max(320, fontSize * Math.Max(4, text.Length + 2));
            var canvasHeight = Math.Max(256, fontSize * 3);
            var baselineY = Math.Min(canvasHeight - 32, Math.Max(fontSize * 2, 128));
            var drawables = new Drawables()
                .Font(fontFamily, FontStyleType.Normal, FontWeight.ExtraBold, FontStretch.Normal)
                .FontPointSize(fontSize);
            var metrics = drawables.FontTypeMetrics(text);
            if (metrics is null)
                return 0;

            using var image = new MagickImage(MagickColors.Transparent, (uint)canvasWidth, (uint)canvasHeight);
            drawables
                .FillColor(MagickColors.White)
                .Text(startX, baselineY, text)
                .Draw(image);

            var pixels = image.GetPixels().ToByteArray(PixelMapping.RGBA);
            if (pixels is null || pixels.Length == 0)
                return 0;

            var minX = canvasWidth;
            var maxX = -1;
            for (var y = 0; y < canvasHeight; y++)
            {
                for (var x = 0; x < canvasWidth; x++)
                {
                    var index = ((y * canvasWidth) + x) * 4;
                    if (pixels[index + 3] <= alphaThreshold)
                        continue;

                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }
            }

            if (maxX < minX)
                return 0;

            var inkCenter = (((double)minX + maxX) / 2d) - startX;
            var advanceCenter = metrics.TextWidth / 2d;
            return (int)Math.Round(Math.Clamp(advanceCenter - inkCenter, -fontSize / 3d, fontSize / 3d));
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"Social card centered text measurement fell back to zero offset: {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    private static void AppendLogo(StringBuilder svg, SocialCardRenderState state, int x, int y, int size)
    {
        var radius = GetScaledPixels(state.Width, state.Height, 20, 10);
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{size}"" height=""{size}"" rx=""{radius}"" fill=""{state.Palette.Surface}""/>");
        if (!string.IsNullOrWhiteSpace(state.LogoDataUri) && state.EmbedReferencedMediaInSvg)
        {
            var inset = Math.Max(6, size / 8);
            svg.AppendLine($@"  <image href=""{EscapeXml(state.LogoDataUri)}"" xlink:href=""{EscapeXml(state.LogoDataUri)}"" x=""{x + inset}"" y=""{y + inset}"" width=""{Math.Max(12, size - (inset * 2))}"" height=""{Math.Max(12, size - (inset * 2))}"" preserveAspectRatio=""xMidYMid meet""/>");
            return;
        }

        if (!string.IsNullOrWhiteSpace(state.LogoDataUri) &&
            IsRenderableImageSource(state.LogoDataUri, state.AllowRemoteMediaFetch))
        {
            return;
        }

        var monogram = BuildMonogram(state.Eyebrow, state.Badge);
        var monogramFontSize = Math.Max(18, size * 2 / 5);
        var monogramX = x + (size / 2) + ResolveMonogramOpticalOffset(state, monogramFontSize, monogram);
        svg.AppendLine($@"  <text x=""{monogramX}"" y=""{GetCenteredTextBaseline(y, size, monogramFontSize)}"" fill=""{state.Palette.TextPrimary}"" font-size=""{monogramFontSize}"" font-family=""{EscapeXml(state.Typography.TitleFontFamily)}"" font-weight=""800"" text-anchor=""middle"">{EscapeXml(monogram)}</text>");
    }

    private static int ResolveMonogramOpticalOffset(SocialCardRenderState state, int fontSize, string monogram)
    {
        var configured = ReadThemeToken(state.ThemeTokens, "socialCard", "monogramOpticalOffset");
        if (TryParsePixelishInt(configured, out var value))
            return value;

        return ResolveCenteredTextInkOffset(state.Typography.TitleFontFamily, fontSize, monogram) -
               GetScaledPixels(state.Width, state.Height, 2, 1);
    }

    private static void AppendSeparator(StringBuilder svg, SocialCardRenderState state, int x, int y, int width)
    {
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{width}"" height=""1"" fill=""{state.Palette.SurfaceStroke}"" fill-opacity=""0.3""/>");
    }

    private static bool HasMetrics(SocialCardRenderState state) => state.Metrics.Count > 0;

    private static int GetMetricRowHeight(SocialCardRenderState state)
    {
        return HasMetrics(state)
            ? ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 54, 36, "socialCard", "metricRowHeight")
            : 0;
    }

    private static void AppendMetricRow(StringBuilder svg, SocialCardRenderState state, int x, int y, int width, int rowHeight)
    {
        if (!HasMetrics(state) || width <= 0 || rowHeight <= 0)
            return;

        var metrics = state.Metrics.Take(5).ToArray();
        var columnWidth = Math.Max(GetScaledPixels(state.Width, state.Height, 84, 62), width / metrics.Length);
        var valueFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 15, "socialCard", "metricValueFontSize");
        var labelFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 13, 10, "socialCard", "metricLabelFontSize");
        var iconSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 18, 12, "socialCard", "metricIconSize");
        var labelY = y + rowHeight - Math.Max(4, labelFontSize / 3);
        var valueY = y + Math.Max(valueFontSize, rowHeight / 2);
        var gap = GetScaledPixels(state.Width, state.Height, 9, 5);

        for (var i = 0; i < metrics.Length; i++)
        {
            var metric = metrics[i];
            var columnX = x + (i * columnWidth);
            var color = IsSafeCssColor(metric.Color) ? metric.Color!.Trim() : state.Palette.TextPrimary;
            var valueX = columnX + iconSize + gap;
            var iconY = valueY - iconSize + 1;
            AppendMetricIcon(svg, ResolveMetricIconKey(metric), columnX, iconY, iconSize, color);

            svg.AppendLine($@"  <text x=""{valueX}"" y=""{valueY}"" fill=""{color}"" font-size=""{valueFontSize}"" font-family=""{EscapeXml(state.Typography.BodyFontFamily)}"" font-weight=""800"">{EscapeXml(metric.Value ?? string.Empty)}</text>");
            if (!string.IsNullOrWhiteSpace(metric.Label))
                svg.AppendLine($@"  <text x=""{columnX}"" y=""{labelY}"" fill=""{state.Palette.TextSecondary}"" fill-opacity=""0.72"" font-size=""{labelFontSize}"" font-family=""{EscapeXml(state.Typography.BodyFontFamily)}"" font-weight=""600"">{EscapeXml(metric.Label)}</text>");
        }
    }

    private static string ResolveMetricIconKey(SocialCardMetricSpec metric)
    {
        var raw = string.IsNullOrWhiteSpace(metric.Icon) ? metric.Label : metric.Icon;
        var key = NormalizeMetricIconKey(raw);
        if (!string.IsNullOrWhiteSpace(key))
            return key;

        return NormalizeMetricIconKey(metric.Label) switch
        {
            "stars" => "star",
            "issues" => "issue",
            "forks" => "fork",
            "downloads" => "download",
            "contributors" => "users",
            "discussions" => "discussion",
            "pull requests" => "pull-request",
            "pull-requests" => "pull-request",
            "prs" => "pull-request",
            "releases" => "tag",
            "versions" => "tag",
            "commits" => "commit",
            "security" => "shield",
            "coverage" => "shield",
            "build" => "activity",
            "status" => "activity",
            "docs" => "book",
            "documentation" => "book",
            "license" => "scale",
            "uptime" => "clock",
            _ => "dot"
        };
    }

    private static string NormalizeMetricIconKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant().Replace("_", "-", StringComparison.Ordinal);
        return normalized switch
        {
            "*" or "star" or "stars" => "star",
            "!" or "issue" or "issues" or "bug" or "bugs" => "issue",
            "y" or "fork" or "forks" or "branch" or "branches" => "fork",
            "user" or "users" or "contributor" or "contributors" => "users",
            "download" or "downloads" => "download",
            "discussion" or "discussions" or "comment" or "comments" => "discussion",
            "pull-request" or "pull-requests" or "pr" or "prs" or "merge" => "pull-request",
            "commit" or "commits" => "commit",
            "tag" or "tags" or "release" or "releases" or "version" or "versions" => "tag",
            "shield" or "security" or "coverage" or "verified" => "shield",
            "clock" or "time" or "uptime" or "duration" => "clock",
            "book" or "docs" or "documentation" or "guide" or "guides" => "book",
            "code" or "language" or "languages" => "code",
            "globe" or "website" or "site" or "web" => "globe",
            "activity" or "build" or "status" or "pipeline" or "ci" => "activity",
            "scale" or "license" => "scale",
            "lock" or "private" or "secure" => "lock",
            "rocket" or "deploy" or "deployment" => "rocket",
            "alert" or "warning" or "failed" => "alert",
            "x" or "error" or "fail" => "x",
            "check" or "success" or "passed" => "check",
            "package" or "packages" or "module" or "modules" => "package",
            _ => string.Empty
        };
    }

    private static void AppendMetricIcon(StringBuilder svg, string iconKey, int x, int y, int size, string color)
    {
        var scale = size / 24d;
        svg.AppendLine($@"  <g transform=""translate({x} {y}) scale({scale.ToString("0.###", CultureInfo.InvariantCulture)})"" fill=""none"" stroke=""{color}"" stroke-opacity=""0.78"" stroke-width=""2.2"" stroke-linecap=""round"" stroke-linejoin=""round"">");
        switch (iconKey)
        {
            case "star":
                svg.AppendLine(@"    <path d=""M12 2.6l2.9 5.9 6.5.9-4.7 4.6 1.1 6.5L12 17.5l-5.8 3 1.1-6.5-4.7-4.6 6.5-.9L12 2.6z""/>");
                break;
            case "issue":
                svg.AppendLine(@"    <circle cx=""12"" cy=""12"" r=""8""/>");
                svg.AppendLine(@"    <circle cx=""12"" cy=""12"" r=""1.6"" fill=""currentColor"" stroke=""none""/>".Replace("currentColor", color, StringComparison.Ordinal));
                break;
            case "fork":
                svg.AppendLine(@"    <circle cx=""6"" cy=""5"" r=""2.2""/>");
                svg.AppendLine(@"    <circle cx=""18"" cy=""5"" r=""2.2""/>");
                svg.AppendLine(@"    <circle cx=""12"" cy=""19"" r=""2.2""/>");
                svg.AppendLine(@"    <path d=""M6 7.2v3.2c0 2.2 1.8 4 4 4h2""/>");
                svg.AppendLine(@"    <path d=""M18 7.2v3.2c0 2.2-1.8 4-4 4h-2v2.4""/>");
                break;
            case "users":
                svg.AppendLine(@"    <circle cx=""9"" cy=""8"" r=""3""/>");
                svg.AppendLine(@"    <path d=""M3.5 20c.8-3.5 3-5.2 5.5-5.2s4.7 1.7 5.5 5.2""/>");
                svg.AppendLine(@"    <path d=""M15.8 11.2c1.7.2 3.2 1.6 3.2 3.3"" opacity=""0.7""/>");
                svg.AppendLine(@"    <path d=""M16.5 20c.5-1.9 1.7-3.2 3.5-3.8"" opacity=""0.7""/>");
                break;
            case "download":
                svg.AppendLine(@"    <path d=""M12 3v10""/>");
                svg.AppendLine(@"    <path d=""M8 9l4 4 4-4""/>");
                svg.AppendLine(@"    <path d=""M5 19h14""/>");
                break;
            case "discussion":
                svg.AppendLine(@"    <path d=""M5 6h14v9H9l-4 4V6z""/>");
                break;
            case "pull-request":
                svg.AppendLine(@"    <circle cx=""6"" cy=""6"" r=""2.3""/>");
                svg.AppendLine(@"    <circle cx=""18"" cy=""18"" r=""2.3""/>");
                svg.AppendLine(@"    <path d=""M6 8.3V18""/>");
                svg.AppendLine(@"    <path d=""M10 6h4a4 4 0 0 1 4 4v5.7""/>");
                svg.AppendLine(@"    <path d=""M12 4l-2 2 2 2""/>");
                break;
            case "commit":
                svg.AppendLine(@"    <circle cx=""12"" cy=""12"" r=""4""/>");
                svg.AppendLine(@"    <path d=""M3 12h5""/>");
                svg.AppendLine(@"    <path d=""M16 12h5""/>");
                break;
            case "tag":
                svg.AppendLine(@"    <path d=""M4 5v6.2L13.8 21 21 13.8 11.2 4H5a1 1 0 0 0-1 1z""/>");
                svg.AppendLine(@"    <circle cx=""8"" cy=""8"" r=""1.4""/>");
                break;
            case "shield":
                svg.AppendLine(@"    <path d=""M12 3l7 3v5.5c0 4.3-2.8 7.7-7 9.5-4.2-1.8-7-5.2-7-9.5V6l7-3z""/>");
                break;
            case "clock":
                svg.AppendLine(@"    <circle cx=""12"" cy=""12"" r=""8""/>");
                svg.AppendLine(@"    <path d=""M12 7v5l3.5 2""/>");
                break;
            case "book":
                svg.AppendLine(@"    <path d=""M5 4h9a3 3 0 0 1 3 3v13H8a3 3 0 0 0-3 3V4z""/>");
                svg.AppendLine(@"    <path d=""M8 4v15""/>");
                break;
            case "code":
                svg.AppendLine(@"    <path d=""M8 8l-4 4 4 4""/>");
                svg.AppendLine(@"    <path d=""M16 8l4 4-4 4""/>");
                svg.AppendLine(@"    <path d=""M14 5l-4 14""/>");
                break;
            case "globe":
                svg.AppendLine(@"    <circle cx=""12"" cy=""12"" r=""8""/>");
                svg.AppendLine(@"    <path d=""M4 12h16""/>");
                svg.AppendLine(@"    <path d=""M12 4c2.2 2.4 3.2 5 3.2 8s-1 5.6-3.2 8c-2.2-2.4-3.2-5-3.2-8s1-5.6 3.2-8z""/>");
                break;
            case "activity":
                svg.AppendLine(@"    <path d=""M3 12h4l2-5 5 11 3-6h4""/>");
                break;
            case "scale":
                svg.AppendLine(@"    <path d=""M12 3v18""/>");
                svg.AppendLine(@"    <path d=""M5 6h14""/>");
                svg.AppendLine(@"    <path d=""M6 6l-3 6h6L6 6z""/>");
                svg.AppendLine(@"    <path d=""M18 6l-3 6h6l-3-6z""/>");
                break;
            case "lock":
                svg.AppendLine(@"    <rect x=""5"" y=""10"" width=""14"" height=""10"" rx=""2""/>");
                svg.AppendLine(@"    <path d=""M8 10V7a4 4 0 0 1 8 0v3""/>");
                break;
            case "rocket":
                svg.AppendLine(@"    <path d=""M13 4c3.5.5 5.8 2.8 6.3 6.3L13 16.6 7.4 11 13 4z""/>");
                svg.AppendLine(@"    <path d=""M7.4 11H4l3 3v3.4l3-3""/>");
                svg.AppendLine(@"    <circle cx=""14.7"" cy=""8.6"" r=""1.3""/>");
                break;
            case "alert":
                svg.AppendLine(@"    <path d=""M12 4l9 16H3L12 4z""/>");
                svg.AppendLine(@"    <path d=""M12 9v5""/>");
                svg.AppendLine(@"    <path d=""M12 17h.1""/>");
                break;
            case "x":
                svg.AppendLine(@"    <path d=""M6 6l12 12""/>");
                svg.AppendLine(@"    <path d=""M18 6L6 18""/>");
                break;
            case "check":
                svg.AppendLine(@"    <path d=""M4.5 12.5l5 5L20 7""/>");
                break;
            case "package":
                svg.AppendLine(@"    <path d=""M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3z""/>");
                svg.AppendLine(@"    <path d=""M4.5 7.8L12 12l7.5-4.2""/>");
                svg.AppendLine(@"    <path d=""M12 12v8.5""/>");
                break;
            default:
                svg.AppendLine(@"    <circle cx=""12"" cy=""12"" r=""4""/>");
                break;
        }

        svg.AppendLine("  </g>");
    }

    private static bool IsSafeCssColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            if (trimmed.Length is not (4 or 7 or 9))
                return false;

            for (var i = 1; i < trimmed.Length; i++)
            {
                if (!Uri.IsHexDigit(trimmed[i]))
                    return false;
            }

            return true;
        }

        return trimmed.All(static c => char.IsLetter(c) || c is '-' or ' ');
    }

    private static IReadOnlyList<SocialCardMetricSpec> NormalizeSocialCardMetrics(IEnumerable<SocialCardMetricSpec>? metrics)
    {
        return SocialCardMetricNormalizer.Normalize(metrics);
    }

    // ── Layout: Spotlight (Home) ───────────────────────────────────────

    private static void AppendSpotlightLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var padX = state.SafeMarginX;
        var padY = state.SafeMarginY;
        var contentInsetX = GetScaledPixels(state.Width, state.Height, 34, 20);
        var contentInsetY = GetScaledPixels(state.Width, state.Height, 44, 26);
        var x = padX + contentInsetX;
        var brandPanel = GetBrandPanelRect(state);
        var gapX = GetScaledPixels(state.Width, state.Height, 48, 28);
        var contentWidth = brandPanel is null
            ? Math.Max(320, (int)Math.Round(state.Width * 0.58))
            : Math.Max(320, brandPanel.X - x - gapX);

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 62, 32, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 68, 36);
        var eyebrowFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "eyebrowFontSize");
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 30, 19);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);
        var gapBadgeToEyebrow = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 28, 18, "socialCard", "badgeToEyebrowGap");
        var gapEyebrowToTitle = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 10, 6, "socialCard", "eyebrowToTitleGap");
        var gapTitleToDescription = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 24, 14, "socialCard", "titleToDescriptionGap");
        var metricRowHeight = GetMetricRowHeight(state);
        var gapDescriptionToMetrics = HasMetrics(state)
            ? ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 30, 18, "socialCard", "descriptionToMetricsGap")
            : 0;
        var footerLabel = TrimSingleLine(state.FooterLabel, 64).Trim();
        var hasFooterLabel = !string.IsNullOrWhiteSpace(footerLabel) && footerLabel != "/";
        var footerFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 11, "socialCard", "footerFontSize");
        var gapDescriptionToFooter = hasFooterLabel
            ? ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 54, 30, "socialCard", "descriptionToFooterGap")
            : 0;

        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 3);

        var blockHeight = badgeHeight +
                          gapBadgeToEyebrow +
                          eyebrowFontSize +
                          gapEyebrowToTitle +
                          titleFontSize +
                          (Math.Max(0, titleLines.Count - 1) * titleLineHeight) +
                          gapTitleToDescription +
                          descFontSize +
                          (Math.Max(0, descLines.Count - 1) * descLineHeight) +
                          (HasMetrics(state) ? gapDescriptionToMetrics + metricRowHeight : 0) +
                          (hasFooterLabel ? gapDescriptionToFooter + footerFontSize : 0);

        var availableTop = padY;
        var availableBottom = state.Height - padY;
        var blockStartY = ResolveVerticalBlockStart(availableTop, availableBottom, blockHeight);

        if (brandPanel is not null)
            AppendBrandPanel(svg, state, brandPanel);

        // Badge
        var badgeY = blockStartY;
        AppendBadge(svg, state, x, badgeY);

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + gapBadgeToEyebrow + eyebrowFontSize;
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + gapEyebrowToTitle + titleFontSize;
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (Math.Max(0, titleLines.Count - 1) * titleLineHeight) + gapTitleToDescription + descFontSize;
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);

        var nextY = descY + (Math.Max(0, descLines.Count - 1) * descLineHeight);
        if (HasMetrics(state))
        {
            var metricsY = nextY + gapDescriptionToMetrics;
            AppendMetricRow(svg, state, x, metricsY, contentWidth, metricRowHeight);
            nextY = metricsY + metricRowHeight;
        }

        // Footer route
        if (hasFooterLabel)
        {
            var footerY = nextY + gapDescriptionToFooter + footerFontSize;
            AppendFooterRoute(svg, state, x, footerY);
        }

        var logoFrame = ResolveLogoFrame(state);
        if (logoFrame is not null)
            AppendLogo(svg, state, logoFrame.X, logoFrame.Y, logoFrame.Width);
    }

    // ── Layout: Shelf (Docs) ───────────────────────────────────────────

    private static void AppendShelfLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var padX = state.SafeMarginX;
        var padY = state.SafeMarginY;
        var x = padX;
        var contentWidth = state.Width - (padX * 2);

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 48, 26, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 54, 30);
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 20, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 28, 18);

        // Badge
        var badgeY = padY;
        AppendBadge(svg, state, x, badgeY);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);

        // Route label on the right
        var routeLabel = TrimSingleLine(state.FooterLabel, 40).Trim();
        if (!string.IsNullOrWhiteSpace(routeLabel) && routeLabel != "/")
        {
            var routeFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 11, "socialCard", "footerFontSize");
            svg.AppendLine($@"  <text x=""{state.Width - padX}"" y=""{badgeY + (badgeHeight / 2)}"" fill=""{state.Palette.TextSecondary}"" fill-opacity=""0.5"" font-size=""{routeFontSize}"" font-family=""{EscapeXml(state.Typography.FooterFontFamily)}"" font-weight=""500"" dominant-baseline=""central"" text-anchor=""end"">{EscapeXml(routeLabel)}</text>");
        }

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 40, 24);
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 50, 30);
        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 24, 14);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 3);
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);
    }

    // ── Layout: Reference (API) ────────────────────────────────────────

    private static void AppendReferenceLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var padX = state.SafeMarginX;
        var padY = state.SafeMarginY;
        var codeWidth = Math.Max(GetScaledPixels(state.Width, state.Height, 340, 220), (int)Math.Round(state.Width * 0.35));
        var codeX = state.Width - padX - codeWidth;
        var gapX = GetScaledPixels(state.Width, state.Height, 32, 18);
        var x = padX;
        var contentWidth = Math.Max(240, codeX - x - gapX);

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 44, 26, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 50, 28);
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 20, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 27, 18);

        // Badge
        var badgeY = padY;
        AppendBadge(svg, state, x, badgeY);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 40, 24);
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 50, 30);
        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 24, 14);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 3);
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);

        // Footer route
        var footerY = state.Height - padY - GetScaledPixels(state.Width, state.Height, 6, 4);
        AppendFooterRoute(svg, state, x, footerY);

        // Code pane on right
        AppendCodePane(svg, state, codeX, padY, codeWidth, state.Height - (padY * 2) - GetScaledPixels(state.Width, state.Height, 6, 4));
    }

    // ── Layout: Inline Image (Blog with image) ────────────────────────

    private static void AppendInlineImageLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var padX = state.SafeMarginX;
        var padY = state.SafeMarginY;
        var media = GetInlineMediaFrame(state);
        var gapX = GetScaledPixels(state.Width, state.Height, 28, 16);
        var x = padX;
        var contentWidth = Math.Max(240, media.X - x - gapX);

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 44, 26, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 50, 28);
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 20, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 27, 18);

        // Badge
        var badgeY = padY;
        AppendBadge(svg, state, x, badgeY);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 40, 24);
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 50, 30);
        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 24, 14);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 4);
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);

        // Footer route
        var footerY = state.Height - padY - GetScaledPixels(state.Width, state.Height, 6, 4);
        AppendFooterRoute(svg, state, x, footerY);

        // Inline image on right
        var imgRadius = GetScaledPixels(state.Width, state.Height, 12, 6);
        svg.AppendLine($@"  <rect x=""{media.X}"" y=""{media.Y}"" width=""{media.Width}"" height=""{media.Height}"" rx=""{imgRadius}"" fill=""{state.Palette.Surface}""/>");
        if (state.EmbedReferencedMediaInSvg)
            svg.AppendLine($@"  <image href=""{EscapeXml(state.InlineImageDataUri ?? string.Empty)}"" xlink:href=""{EscapeXml(state.InlineImageDataUri ?? string.Empty)}"" x=""{media.X}"" y=""{media.Y}"" width=""{media.Width}"" height=""{media.Height}"" preserveAspectRatio=""xMidYMid slice"" clip-path=""url(#mediaClip)""/>");
    }

    // ── Layout: Connect (Contact) ──────────────────────────────────────

    private static void AppendConnectLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var padX = state.SafeMarginX;
        var padY = state.SafeMarginY;
        var mapWidth = Math.Max(GetScaledPixels(state.Width, state.Height, 380, 220), (int)Math.Round(state.Width * 0.38));
        var mapX = state.Width - mapWidth;
        var gapX = GetScaledPixels(state.Width, state.Height, 28, 16);
        var x = padX;
        var contentWidth = Math.Max(240, mapX - x - gapX);

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 48, 28, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 54, 30);
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 20, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 27, 18);

        // Stylized map on the right side (behind content)
        AppendMapVisual(svg, state, mapX, 0, mapWidth, state.Height);

        // Badge
        var badgeY = padY;
        AppendBadge(svg, state, x, badgeY);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 40, 24);
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 50, 30);
        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 24, 14);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 4);
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);

        // Footer route
        var footerY = state.Height - padY - GetScaledPixels(state.Width, state.Height, 6, 4);
        AppendFooterRoute(svg, state, x, footerY);

        if (!string.IsNullOrWhiteSpace(state.LogoDataUri))
        {
            var frame = ResolveLogoFrame(state);
            if (frame is not null)
                AppendLogo(svg, state, frame.X, frame.Y, frame.Width);
        }
    }

    private static void AppendMapVisual(StringBuilder svg, SocialCardRenderState state, int x, int y, int width, int height)
    {
        // Subtle grid pattern resembling a map
        var gridSpacing = GetScaledPixels(state.Width, state.Height, 48, 30);
        var lineOpacity = "0.06";
        var accentLineOpacity = "0.12";

        // Vertical grid lines
        for (var gx = x + gridSpacing; gx < x + width; gx += gridSpacing)
            svg.AppendLine($@"  <rect x=""{gx}"" y=""{y}"" width=""1"" height=""{height}"" fill=""{state.Palette.Accent}"" fill-opacity=""{lineOpacity}""/>");

        // Horizontal grid lines
        for (var gy = y + gridSpacing; gy < y + height; gy += gridSpacing)
            svg.AppendLine($@"  <rect x=""{x}"" y=""{gy}"" width=""{width}"" height=""1"" fill=""{state.Palette.Accent}"" fill-opacity=""{lineOpacity}""/>");

        // Accent "roads" - a few thicker diagonal/crossing lines
        var roadWidth = GetScaledPixels(state.Width, state.Height, 3, 2);
        var cx = x + (width / 2);
        var cy = y + (height / 2);
        svg.AppendLine($@"  <rect x=""{x}"" y=""{cy - (roadWidth / 2)}"" width=""{width}"" height=""{roadWidth}"" fill=""{state.Palette.Accent}"" fill-opacity=""{accentLineOpacity}""/>");
        svg.AppendLine($@"  <rect x=""{cx - (roadWidth / 2)}"" y=""{y}"" width=""{roadWidth}"" height=""{height}"" fill=""{state.Palette.Accent}"" fill-opacity=""{accentLineOpacity}""/>");
        // Diagonal
        svg.AppendLine($@"  <line x1=""{x}"" y1=""{y + height}"" x2=""{x + width}"" y2=""{y}"" stroke=""{state.Palette.Accent}"" stroke-opacity=""{accentLineOpacity}"" stroke-width=""{roadWidth}""/>");

        // Location pin
        var pinSize = GetScaledPixels(state.Width, state.Height, 36, 22);
        var pinX = cx + GetScaledPixels(state.Width, state.Height, 20, 12);
        var pinY = cy - GetScaledPixels(state.Width, state.Height, 40, 24);
        // Pin circle
        svg.AppendLine($@"  <circle cx=""{pinX}"" cy=""{pinY}"" r=""{pinSize}"" fill=""{state.Palette.Accent}"" fill-opacity=""0.22""/>");
        svg.AppendLine($@"  <circle cx=""{pinX}"" cy=""{pinY}"" r=""{pinSize * 2 / 3}"" fill=""{state.Palette.Accent}"" fill-opacity=""0.4""/>");
        svg.AppendLine($@"  <circle cx=""{pinX}"" cy=""{pinY}"" r=""{Math.Max(4, pinSize / 3)}"" fill=""{state.Palette.Accent}""/>");
    }

    // ── Layout: Editorial / Product (Default) ──────────────────────────

    private static void AppendEditorialOrProductLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var padX = state.SafeMarginX;
        var padY = state.SafeMarginY;
        var contentInsetX = GetScaledPixels(state.Width, state.Height, 34, 20);
        var contentInsetY = GetScaledPixels(state.Width, state.Height, 44, 26);
        var x = padX + contentInsetX;
        var brandPanel = GetBrandPanelRect(state);
        var gapX = GetScaledPixels(state.Width, state.Height, 42, 24);
        var contentWidth = brandPanel is null
            ? state.Width - (padX * 2)
            : Math.Max(320, brandPanel.X - x - gapX);

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 50, 28, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 56, 32);
        var eyebrowFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "eyebrowFontSize");
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 20, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 28, 18);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);
        var gapBadgeToEyebrow = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 28, 18, "socialCard", "badgeToEyebrowGap");
        var gapEyebrowToTitle = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 10, 6, "socialCard", "eyebrowToTitleGap");
        var gapTitleToDescription = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "titleToDescriptionGap");
        var metricRowHeight = GetMetricRowHeight(state);
        var gapDescriptionToMetrics = HasMetrics(state)
            ? ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 28, 18, "socialCard", "descriptionToMetricsGap")
            : 0;
        var footerLabel = TrimSingleLine(state.FooterLabel, 64).Trim();
        var hasFooterLabel = !string.IsNullOrWhiteSpace(footerLabel) && footerLabel != "/";
        var footerFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 11, "socialCard", "footerFontSize");
        var gapDescriptionToFooter = hasFooterLabel
            ? ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 50, 28, "socialCard", "descriptionToFooterGap")
            : 0;

        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 4);

        var blockHeight = badgeHeight +
                          gapBadgeToEyebrow +
                          eyebrowFontSize +
                          gapEyebrowToTitle +
                          titleFontSize +
                          (Math.Max(0, titleLines.Count - 1) * titleLineHeight) +
                          gapTitleToDescription +
                          descFontSize +
                          (Math.Max(0, descLines.Count - 1) * descLineHeight) +
                          (HasMetrics(state) ? gapDescriptionToMetrics + metricRowHeight : 0) +
                          (hasFooterLabel ? gapDescriptionToFooter + footerFontSize : 0);

        var availableTop = padY;
        var availableBottom = state.Height - padY;
        var blockStartY = ResolveVerticalBlockStart(availableTop, availableBottom, blockHeight);

        if (brandPanel is not null)
            AppendBrandPanel(svg, state, brandPanel);

        // Badge
        var badgeY = blockStartY;
        AppendBadge(svg, state, x, badgeY);

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + gapBadgeToEyebrow + eyebrowFontSize;
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + gapEyebrowToTitle + titleFontSize;
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (Math.Max(0, titleLines.Count - 1) * titleLineHeight) + gapTitleToDescription + descFontSize;
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);

        var nextY = descY + (Math.Max(0, descLines.Count - 1) * descLineHeight);
        if (HasMetrics(state))
        {
            var metricsY = nextY + gapDescriptionToMetrics;
            AppendMetricRow(svg, state, x, metricsY, contentWidth, metricRowHeight);
            nextY = metricsY + metricRowHeight;
        }

        // Footer route
        if (hasFooterLabel)
        {
            var footerY = nextY + gapDescriptionToFooter + footerFontSize;
            AppendFooterRoute(svg, state, x, footerY);
        }

        var logoFrame = ResolveLogoFrame(state);
        if (logoFrame is not null)
            AppendLogo(svg, state, logoFrame.X, logoFrame.Y, logoFrame.Width);
    }

    // ── Code pane (API reference) ──────────────────────────────────────

    private static void AppendCodePane(StringBuilder svg, SocialCardRenderState state, int x, int y, int width, int height)
    {
        var radius = GetScaledPixels(state.Width, state.Height, 16, 8);
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{width}"" height=""{height}"" rx=""{radius}"" fill=""{state.Palette.Surface}""/>");

        var lang = ResolveLanguageMark(state.FooterLabel, state.Title);
        var cx = x + (width / 2);
        var cy = y + (height / 2);

        // Large language mark - centered, bold, modern
        var markFontSize = Math.Min(width, height) * 2 / 5;
        svg.AppendLine($@"  <text x=""{cx}"" y=""{cy}"" fill=""{state.Palette.Accent}"" fill-opacity=""0.18"" font-size=""{markFontSize}"" font-family=""{EscapeXml(state.Typography.TitleFontFamily)}"" font-weight=""800"" dominant-baseline=""central"" text-anchor=""middle"">{EscapeXml(lang)}</text>");

        // Smaller label below the mark
        var labelFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 11, "socialCard", "codeFontSize");
        var labelText = ResolveLanguageLabel(state.FooterLabel, state.Title);
        svg.AppendLine($@"  <text x=""{cx}"" y=""{cy + (markFontSize / 2) + GetScaledPixels(state.Width, state.Height, 24, 14)}"" fill=""{state.Palette.TextSecondary}"" fill-opacity=""0.5"" font-size=""{labelFontSize}"" font-family=""{EscapeXml(state.Typography.BodyFontFamily)}"" font-weight=""600"" text-anchor=""middle"">{EscapeXml(labelText)}</text>");
    }

    private static string ResolveLanguageMark(string footerLabel, string title)
    {
        var combined = string.Concat(footerLabel ?? "", " ", title ?? "").ToLowerInvariant();
        if (combined.Contains("powershell", StringComparison.Ordinal) || combined.Contains("/ps/", StringComparison.Ordinal))
            return "PS";
        if (combined.Contains("csharp", StringComparison.Ordinal) || combined.Contains("c#", StringComparison.Ordinal) || combined.Contains("/dotnet/", StringComparison.Ordinal))
            return "C#";
        if (combined.Contains("python", StringComparison.Ordinal) || combined.Contains("/py/", StringComparison.Ordinal))
            return "PY";
        if (combined.Contains("javascript", StringComparison.Ordinal) || combined.Contains("/js/", StringComparison.Ordinal))
            return "JS";
        if (combined.Contains("typescript", StringComparison.Ordinal) || combined.Contains("/ts/", StringComparison.Ordinal))
            return "TS";
        if (combined.Contains("rust", StringComparison.Ordinal))
            return "RS";
        if (combined.Contains("golang", StringComparison.Ordinal) || combined.Contains("/go/", StringComparison.Ordinal))
            return "GO";
        return "API";
    }

    private static string ResolveLanguageLabel(string footerLabel, string title)
    {
        var mark = ResolveLanguageMark(footerLabel, title);
        return mark switch
        {
            "PS" => "PowerShell",
            "C#" => "C# / .NET",
            "PY" => "Python",
            "JS" => "JavaScript",
            "TS" => "TypeScript",
            "RS" => "Rust",
            "GO" => "Go",
            _ => "API Reference"
        };
    }

    // ── Geometry helpers ────────────────────────────────────────────────

    private static SocialRect GetInlineMediaFrame(SocialCardRenderState state)
    {
        var padX = state.SafeMarginX;
        var padY = state.SafeMarginY;
        var width = Math.Max(GetScaledPixels(state.Width, state.Height, 340, 220), (int)Math.Round(state.Width * 0.35));
        var imgHeight = state.Height - (padY * 2) - GetScaledPixels(state.Width, state.Height, 6, 4);
        return new SocialRect(
            state.Width - padX - width,
            padY,
            width,
            Math.Max(160, imgHeight),
            GetScaledPixels(state.Width, state.Height, 12, 6));
    }

    private static int GetCenteredTextBaseline(int y, int height, int fontSize)
    {
        return y + (height / 2) + (int)Math.Round(fontSize * 0.35);
    }

    private static int ResolveVerticalBlockStart(int availableTop, int availableBottom, int blockHeight)
    {
        if (availableBottom <= availableTop || blockHeight <= 0)
            return availableTop;

        var availableHeight = availableBottom - availableTop;
        var centered = availableTop + Math.Max(0, (availableHeight - blockHeight) / 2);
        var latestStart = Math.Max(availableTop, availableBottom - blockHeight);
        return Math.Clamp(centered, availableTop, latestStart);
    }

    private static SocialRect? GetBrandPanelRect(SocialCardRenderState state)
    {
        var padX = state.SafeMarginX;
        var padY = state.SafeMarginY;

        if (string.Equals(state.LayoutKey, "spotlight", StringComparison.OrdinalIgnoreCase))
        {
            var width = Math.Max(GetScaledPixels(state.Width, state.Height, 280, 180), (int)Math.Round(state.Width * 0.24));
            var height = Math.Max(GetScaledPixels(state.Width, state.Height, 360, 230), (int)Math.Round(state.Height * 0.60));
            var x = state.Width - padX - width;
            var y = Math.Max(padY, (state.Height - height) / 2);
            return new SocialRect(x, y, width, height, GetScaledPixels(state.Width, state.Height, 30, 18));
        }

        if (string.Equals(state.LayoutKey, "product", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state.LayoutKey, "editorial", StringComparison.OrdinalIgnoreCase))
        {
            var width = Math.Max(GetScaledPixels(state.Width, state.Height, 220, 140), (int)Math.Round(state.Width * 0.20));
            var height = Math.Max(GetScaledPixels(state.Width, state.Height, 250, 160), (int)Math.Round(state.Height * 0.44));
            var x = state.Width - padX - width;
            var y = Math.Max(padY, (state.Height - height) / 2);
            return new SocialRect(x, y, width, height, GetScaledPixels(state.Width, state.Height, 24, 14));
        }

        return null;
    }

    private static void AppendBrandPanel(StringBuilder svg, SocialCardRenderState state, SocialRect panel)
    {
        var accentRadius = Math.Max(16, panel.Width / 5);
        var accentRadiusSmall = Math.Max(12, panel.Width / 8);
        var outlineInset = Math.Max(10, panel.Width / 14);
        var outlineRadius = Math.Max(12, panel.Radius - (outlineInset / 2));

        svg.AppendLine($@"  <rect x=""{panel.X}"" y=""{panel.Y}"" width=""{panel.Width}"" height=""{panel.Height}"" rx=""{panel.Radius}"" fill=""{state.Palette.Surface}"" fill-opacity=""0.42"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.34""/>");
        svg.AppendLine($@"  <rect x=""{panel.X + outlineInset}"" y=""{panel.Y + outlineInset}"" width=""{Math.Max(1, panel.Width - (outlineInset * 2))}"" height=""{Math.Max(1, panel.Height - (outlineInset * 2))}"" rx=""{outlineRadius}"" fill=""none"" stroke=""{state.Palette.Accent}"" stroke-opacity=""0.10""/>");
        svg.AppendLine($@"  <circle cx=""{panel.X + (panel.Width * 24 / 100)}"" cy=""{panel.Y + (panel.Height * 26 / 100)}"" r=""{accentRadius}"" fill=""{state.Palette.Accent}"" fill-opacity=""0.10""/>");
        svg.AppendLine($@"  <circle cx=""{panel.X + (panel.Width * 72 / 100)}"" cy=""{panel.Y + (panel.Height * 72 / 100)}"" r=""{accentRadiusSmall}"" fill=""{state.Palette.AccentStrong}"" fill-opacity=""0.12""/>");
        svg.AppendLine($@"  <line x1=""{panel.X + outlineInset}"" y1=""{panel.Y + panel.Height - outlineInset}"" x2=""{panel.X + panel.Width - outlineInset}"" y2=""{panel.Y + outlineInset}"" stroke=""{state.Palette.AccentSoft}"" stroke-opacity=""0.10"" stroke-width=""{Math.Max(2, panel.Width / 48)}""/>");
    }

    private static List<string> BuildReferenceLines(SocialCardRenderState state, int maxChars = 32)
    {
        var routeMaxChars = Math.Max(12, maxChars - "namespace ".Length - 1);
        var route = TrimSingleLine(state.FooterLabel.Trim('/').Replace('/', '.'), routeMaxChars);
        if (string.IsNullOrWhiteSpace(route))
            route = "api.reference";

        var titleTokens = NormalizeDisplayTextForWrap(state.Title)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(4)
            .ToArray();
        var signatureMaxChars = Math.Max(12, maxChars - "public sealed class ".Length);
        var signature = titleTokens.Length == 0
            ? "Invoke.Reference()"
            : string.Concat(titleTokens.Select(static token => char.ToUpperInvariant(token[0]) + token[1..])) + "()";

        var commentMaxChars = Math.Max(8, maxChars - 2);
        return
        [
            TrimSingleLine($"namespace {route};", maxChars),
            TrimSingleLine($"public sealed class {TrimSingleLine(signature, signatureMaxChars)}", maxChars),
            "{",
            TrimSingleLine("  // docs, examples, and parameters", commentMaxChars),
            "}"
        ];
    }

    private static string BuildMonogram(string eyebrow, string badge)
    {
        var value = NormalizeDisplayTextForWrap(eyebrow);
        if (string.IsNullOrWhiteSpace(value))
            value = NormalizeDisplayTextForWrap(badge);
        if (string.IsNullOrWhiteSpace(value))
            return "PF";

        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2)
            return string.Concat(tokens[0][0], tokens[1][0]).ToUpperInvariant();

        return tokens[0].Length >= 2 ? tokens[0][..2].ToUpperInvariant() : tokens[0].ToUpperInvariant();
    }

    private static int MeasurePillWidth(string text, int fontSize, int minimumWidth, int maximumWidth)
    {
        return Math.Clamp(
            EstimateTextWidth(TrimSingleLine(text, 56), fontSize, glyphFactor: 0.58) + GetScaledPixels(1200, 630, 34, 22),
            minimumWidth,
            maximumWidth);
    }

    // ── Image compositing (PNG pass) ───────────────────────────────────

    private static void CompositeMedia(MagickImage canvas, SocialCardRenderState state)
    {
        if (!string.IsNullOrWhiteSpace(state.InlineImageDataUri) &&
            string.Equals(state.LayoutKey, "inline-image", StringComparison.OrdinalIgnoreCase))
        {
            var media = GetInlineMediaFrame(state);
            using var image = TryLoadImageSource(state.InlineImageDataUri, media.Width, media.Height, state.AllowRemoteMediaFetch);
            if (image is not null)
            {
                image.Resize(new MagickGeometry((uint)media.Width, (uint)media.Height) { FillArea = true });
                image.Crop((uint)media.Width, (uint)media.Height, Gravity.Center);
                canvas.Composite(image, media.X, media.Y, CompositeOperator.Over);
            }
        }

        if (string.IsNullOrWhiteSpace(state.LogoDataUri))
            return;

        var frame = ResolveLogoFrame(state);
        if (frame is null)
            return;

        using var logo = TryLoadImageSource(state.LogoDataUri, frame.Width, frame.Height, state.AllowRemoteMediaFetch);
        if (logo is null)
            return;

        var inset = Math.Max(6, frame.Width / 8);
        var targetWidth = Math.Max(12, frame.Width - (inset * 2));
        var targetHeight = Math.Max(12, frame.Height - (inset * 2));
        logo.Resize(new MagickGeometry((uint)targetWidth, (uint)targetHeight));
        var offsetX = frame.X + ((frame.Width - (int)logo.Width) / 2);
        var offsetY = frame.Y + ((frame.Height - (int)logo.Height) / 2);
        canvas.Composite(logo, offsetX, offsetY, CompositeOperator.Over);
    }

    private static SocialRect? ResolveLogoFrame(SocialCardRenderState state)
    {
        var padX = state.SafeMarginX;
        var padY = state.SafeMarginY;
        var radius = GetScaledPixels(state.Width, state.Height, 20, 10);

        if (string.Equals(state.LayoutKey, "spotlight", StringComparison.OrdinalIgnoreCase))
        {
            var panel = GetBrandPanelRect(state);
            if (panel is null)
            {
                var fallbackSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 160, 90, "socialCard", "logoSize");
                return new SocialRect(state.Width - padX - fallbackSize, padY, fallbackSize, fallbackSize, radius);
            }

            var size = Math.Min(
                ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 160, 90, "socialCard", "logoSize"),
                Math.Min(
                    Math.Max(72, panel.Width - GetScaledPixels(state.Width, state.Height, 72, 44)),
                    Math.Max(72, panel.Height - GetScaledPixels(state.Width, state.Height, 72, 44))));
            return new SocialRect(
                panel.X + ((panel.Width - size) / 2),
                panel.Y + ((panel.Height - size) / 2),
                size,
                size,
                radius);
        }

        if (string.Equals(state.LayoutKey, "connect", StringComparison.OrdinalIgnoreCase))
        {
            var mapWidth = Math.Max(GetScaledPixels(state.Width, state.Height, 380, 220), (int)Math.Round(state.Width * 0.38));
            var maxSize = Math.Max(48, mapWidth - GetScaledPixels(state.Width, state.Height, 64, 40));
            var size = Math.Min(
                ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 96, 56, "socialCard", "logoSize"),
                maxSize);
            return new SocialRect(state.Width - padX - size, padY, size, size, radius);
        }

        if (string.Equals(state.LayoutKey, "product", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state.LayoutKey, "editorial", StringComparison.OrdinalIgnoreCase))
        {
            var panel = GetBrandPanelRect(state);
            if (panel is null)
            {
                var fallbackSize = GetScaledPixels(state.Width, state.Height, 48, 30);
                return new SocialRect(state.Width - padX - fallbackSize, padY, fallbackSize, fallbackSize, radius);
            }

            var size = Math.Min(
                ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 88, 56, "socialCard", "logoSize"),
                Math.Min(
                    Math.Max(56, panel.Width - GetScaledPixels(state.Width, state.Height, 52, 30)),
                    Math.Max(56, panel.Height - GetScaledPixels(state.Width, state.Height, 52, 30))));
            return new SocialRect(
                panel.X + ((panel.Width - size) / 2),
                panel.Y + ((panel.Height - size) / 2),
                size,
                size,
                radius);
        }

        return null;
    }

    internal static void ClearRemoteImageCache()
    {
        RemoteImageByteCache.Clear();
    }

    internal static (int X, int Y, int Width, int Height)? GetLogoFrameForTesting(SocialCardRenderOptions options)
    {
        var frame = ResolveLogoFrame(CreateState(options));
        return frame is null ? null : (frame.X, frame.Y, frame.Width, frame.Height);
    }

    internal static int GetTitleOpticalOffsetForTesting(int fontSize, string? titleLine, IReadOnlyDictionary<string, object?>? themeTokens = null)
    {
        var state = new SocialCardRenderState
        {
            Width = 1200,
            Height = 630,
            Title = titleLine ?? string.Empty,
            Description = string.Empty,
            Eyebrow = string.Empty,
            Badge = "PAGE",
            FooterLabel = "/",
            StyleKey = "default",
            VariantKey = "product",
            LayoutKey = "product",
            Palette = SelectPalette("default", "test", themeTokens),
            Typography = ResolveTypography(themeTokens, preferRasterSafeFonts: true),
            ThemeTokens = themeTokens,
            CtaLabel = "Learn More",
            FrameInset = 0,
            PanelInset = 0,
            ContentPadding = 0,
            FrameRadius = 0,
            PanelRadius = 0,
            SafeMarginX = 0,
            SafeMarginY = 0,
            AllowRemoteMediaFetch = false,
            EmbedReferencedMediaInSvg = false,
            Metrics = Array.Empty<SocialCardMetricSpec>()
        };

        return ResolveTitleOpticalOffset(state, fontSize, titleLine);
    }

    internal static byte[]? GetRemoteImageBytes(string source, bool allowRemoteMediaFetch, Func<string, byte[]?>? remoteFetcher = null)
    {
        if (!allowRemoteMediaFetch || !IsRemoteMediaSource(source))
            return null;

        var fetch = remoteFetcher ?? FetchRemoteImageBytes;
        if (!RemoteImageByteCache.ContainsKey(source) && RemoteImageByteCache.Count >= MaxRemoteImageCacheEntries)
            RemoteImageByteCache.Clear();

        var lazy = RemoteImageByteCache.GetOrAdd(
            source,
            key => new Lazy<byte[]>(
                () =>
                {
                    var payload = fetch(key);
                    return payload is { Length: > 0 } && payload.Length <= MaxRemoteImageBytes
                        ? payload
                        : throw new InvalidOperationException("Remote image fetch returned no usable data.");
                },
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var payload = lazy.Value;
            TrimRemoteImageCacheIfNeeded(source, payload);
            return payload;
        }
        catch (Exception)
        {
            RemoteImageByteCache.TryRemove(new KeyValuePair<string, Lazy<byte[]>>(source, lazy));
            return null;
        }
    }

    internal static int RemoteImageCacheCountForTesting()
    {
        return RemoteImageByteCache.Count;
    }

    private static void TrimRemoteImageCacheIfNeeded(string source, byte[] payload)
    {
        if (RemoteImageByteCache.Count <= MaxRemoteImageCacheEntries)
        {
            long totalBytes = 0;
            foreach (var entry in RemoteImageByteCache.Values)
            {
                if (!entry.IsValueCreated)
                    continue;

                totalBytes += entry.Value.Length;
                if (totalBytes > MaxRemoteImageCacheBytes)
                    break;
            }

            if (totalBytes <= MaxRemoteImageCacheBytes)
                return;
        }

        RemoteImageByteCache.Clear();
        RemoteImageByteCache[source] = new Lazy<byte[]>(() => payload, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static bool IsRemoteMediaSource(string source)
    {
        return source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? FetchRemoteImageBytes(string source)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        using var response = SocialImageHttpClient.Send(request);
        if (!response.IsSuccessStatusCode)
            return null;

        using var stream = response.Content.ReadAsStream();
        if (response.Content.Headers.ContentLength is > MaxRemoteImageBytes)
            return null;

        return ReadRemoteImageBytes(stream);
    }

    internal static byte[]? ReadRemoteImageBytesForTesting(Stream stream)
    {
        return ReadRemoteImageBytes(stream);
    }

    private static byte[]? ReadRemoteImageBytes(Stream stream)
    {
        if (stream is null)
            return null;

        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > MaxRemoteImageBytes)
                return null;

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static MagickImage? TryLoadImageSource(string source, int widthHint, int heightHint, bool allowRemoteMediaFetch)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;

            if (IsRemoteMediaSource(source))
            {
                var remoteBytes = GetRemoteImageBytes(source, allowRemoteMediaFetch);
                if (remoteBytes is null || remoteBytes.Length == 0)
                    return null;

                return CreateMagickImage(remoteBytes, source, widthHint, heightHint);
            }

            var commaIndex = source.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex > 0 && source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var metadata = source[..commaIndex];
                var payload = source[(commaIndex + 1)..];
                var isBase64 = metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
                var bytes = isBase64
                    ? Convert.FromBase64String(payload)
                    : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));

                return CreateMagickImage(bytes, metadata, widthHint, heightHint);
            }

            if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri) &&
                absoluteUri.IsFile &&
                File.Exists(absoluteUri.LocalPath))
            {
                var localPath = absoluteUri.LocalPath;
                if (Path.GetExtension(localPath).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                    return CreateMagickImage(SanitizeSvgBytes(File.ReadAllBytes(localPath)), localPath, widthHint, heightHint);

                EnsureMagickFontConfigInitialized();
                return new MagickImage(localPath);
            }

            if (File.Exists(source))
            {
                if (Path.GetExtension(source).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                    return CreateMagickImage(SanitizeSvgBytes(File.ReadAllBytes(source)), source, widthHint, heightHint);

                EnsureMagickFontConfigInitialized();
                return new MagickImage(source);
            }

            return null;
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"Social card media source could not be loaded: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static MagickImage CreateMagickImage(byte[] bytes, string sourceHint, int widthHint, int heightHint)
    {
        if (sourceHint.Contains("image/svg+xml", StringComparison.OrdinalIgnoreCase) ||
            sourceHint.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var safeBytes = SanitizeSvgBytes(bytes);
            EnsureMagickFontConfigInitialized();
            var image = new MagickImage(safeBytes, new MagickReadSettings
            {
                Width = (uint)Math.Max(1, widthHint),
                Height = (uint)Math.Max(1, heightHint),
                Format = MagickFormat.Svg,
                BackgroundColor = MagickColors.Transparent
            });

            image.BackgroundColor = MagickColors.Transparent;
            return image;
        }

        EnsureMagickFontConfigInitialized();
        return new MagickImage(bytes);
    }

    internal static string? SanitizeSvgForTesting(string svg)
    {
        var sanitized = SanitizeSvgBytes(Encoding.UTF8.GetBytes(svg));
        return sanitized.Length == 0 ? null : Encoding.UTF8.GetString(sanitized);
    }

    private static byte[] SanitizeSvgBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
            return bytes;

        try
        {
            var svg = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using var stringReader = new StringReader(svg);
            using var xmlReader = XmlReader.Create(stringReader, settings);
            var document = XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root is null)
                return Array.Empty<byte>();

            foreach (var script in root
                .DescendantsAndSelf()
                .Where(static element => string.Equals(element.Name.LocalName, "script", StringComparison.OrdinalIgnoreCase))
                .ToArray())
                script.Remove();

            foreach (var element in root.DescendantsAndSelf())
            {
                foreach (var attribute in element.Attributes().Where(IsUnsafeSvgAttribute).ToArray())
                    attribute.Remove();
            }

            return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Social card SVG media sanitization failed: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    private static bool IsUnsafeSvgAttribute(XAttribute attribute)
    {
        var localName = attribute.Name.LocalName;
        if (localName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            return true;

        var value = attribute.Value.Trim();
        if (string.Equals(localName, "href", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(localName, "src", StringComparison.OrdinalIgnoreCase))
        {
            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("data:image/svg+xml", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(localName, "style", StringComparison.OrdinalIgnoreCase) &&
               (value.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("expression(", StringComparison.OrdinalIgnoreCase));
    }

    // ── Types ──────────────────────────────────────────────────────────

    internal sealed class SocialCardRenderOptions
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Eyebrow { get; set; }
        public string? Badge { get; set; }
        public string? FooterLabel { get; set; }
        public int Width { get; set; } = 1200;
        public int Height { get; set; } = 630;
        public string? StyleKey { get; set; }
        public string? VariantKey { get; set; }
        public IReadOnlyDictionary<string, object?>? ThemeTokens { get; set; }
        public string? LogoDataUri { get; set; }
        public string? InlineImageDataUri { get; set; }
        public IReadOnlyList<SocialCardMetricSpec>? Metrics { get; set; }
        /// <summary>"light", "dark", or null (auto = dark).</summary>
        public string? ColorScheme { get; set; }
        public bool AllowRemoteMediaFetch { get; set; }
        public bool EmbedReferencedMediaInSvg { get; set; } = true;
        public bool PreferRasterSafeFonts { get; set; }
    }

    private sealed class SocialCardRenderState
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string Eyebrow { get; init; }
        public required string Badge { get; init; }
        public required string FooterLabel { get; init; }
        public required string StyleKey { get; init; }
        public required string VariantKey { get; init; }
        public required string LayoutKey { get; init; }
        public required SocialPalette Palette { get; init; }
        public required SocialCardTypography Typography { get; init; }
        public required int FrameInset { get; init; }
        public required int PanelInset { get; init; }
        public required int ContentPadding { get; init; }
        public required int FrameRadius { get; init; }
        public required int PanelRadius { get; init; }
        public required int SafeMarginX { get; init; }
        public required int SafeMarginY { get; init; }
        public required string CtaLabel { get; init; }
        public required bool AllowRemoteMediaFetch { get; init; }
        public required bool EmbedReferencedMediaInSvg { get; init; }
        public IReadOnlyDictionary<string, object?>? ThemeTokens { get; init; }
        public string? LogoDataUri { get; init; }
        public string? InlineImageDataUri { get; init; }
        public required IReadOnlyList<SocialCardMetricSpec> Metrics { get; init; }
    }

    private sealed record SocialRect(int X, int Y, int Width, int Height, int Radius);

    private sealed record ExternalImageMagickCommand(string Executable);

    // ── Compatibility shims for old Append* names used in tests ────────

    private static SocialRect GetPanelRect(SocialCardRenderState state)
    {
        var inset = Math.Max(0, state.FrameInset + state.PanelInset);
        return new(
            inset,
            inset,
            Math.Max(1, state.Width - (inset * 2)),
            Math.Max(1, state.Height - (inset * 2)),
            state.PanelRadius);
    }
}
