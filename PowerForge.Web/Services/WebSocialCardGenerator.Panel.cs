using System.Text;

namespace PowerForge.Web;

// Panel layout: a left accent bar, an identity row (logo and site name), an uppercase eyebrow, a large title that
// steps down when it runs long, a short description, a row of chips with the site address, and a right panel that
// shows the page image (for example a product screenshot) or up to four stats.
internal static partial class WebSocialCardGenerator
{
    private const int PanelMaxStatRows = 4;

    private static int PanelPixels(SocialCardRenderState state, double basePixels)
    {
        var scale = Math.Min(state.Width / 1200d, state.Height / 630d);
        return (int)Math.Round(basePixels * scale);
    }

    private static bool PanelShowsImage(SocialCardRenderState state)
        => !string.IsNullOrWhiteSpace(state.InlineImageDataUri) &&
           IsRenderableImageSource(state.InlineImageDataUri, state.AllowRemoteMediaFetch);

    // Stats are numbers and short values. Yes/no flags (for example "Docs: yes") read better as chips, and an open-issue
    // count is not a headline number, so both stay out of the panel. Release tags are shortened to their version.
    private static IReadOnlyList<SocialCardMetricSpec> GetPanelStatRows(SocialCardRenderState state)
        => state.Metrics
            .Where(static metric => !string.IsNullOrWhiteSpace(metric.Value) &&
                                    !string.Equals(metric.Value, "yes", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(metric.Value, "no", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(ResolveMetricIconKey(metric), "issue", StringComparison.OrdinalIgnoreCase))
            .Select(static metric => string.Equals(ResolveMetricIconKey(metric), "tag", StringComparison.OrdinalIgnoreCase)
                ? new SocialCardMetricSpec { Icon = metric.Icon, Label = metric.Label, Color = metric.Color, Value = ShortenReleaseTag(metric.Value!) }
                : metric)
            .Take(PanelMaxStatRows)
            .ToArray();

    internal static string ShortenReleaseTag(string value)
    {
        var match = System.Text.RegularExpressions.Regex.Match(value, @"v?\d+(?:\.\d+)+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success)
            return value;
        return match.Value.StartsWith('v') ? match.Value : "v" + match.Value;
    }

    private static SocialRect? GetPanelVisualRect(SocialCardRenderState state)
    {
        if (PanelShowsImage(state))
            return new SocialRect(PanelPixels(state, 716), PanelPixels(state, 120), PanelPixels(state, 424), PanelPixels(state, 392), PanelPixels(state, 22));
        if (GetPanelStatRows(state).Count > 0)
            return new SocialRect(PanelPixels(state, 790), PanelPixels(state, 128), PanelPixels(state, 350), PanelPixels(state, 376), PanelPixels(state, 22));
        return null;
    }

    private static SocialRect GetPanelImageRect(SocialRect panel, SocialCardRenderState state)
    {
        var inset = PanelPixels(state, 14);
        return new SocialRect(panel.X + inset, panel.Y + inset, Math.Max(1, panel.Width - (inset * 2)), Math.Max(1, panel.Height - (inset * 2)), Math.Max(4, panel.Radius - inset));
    }

    private static SocialRect GetPanelLogoFrame(SocialCardRenderState state)
        => new(PanelPixels(state, 84), PanelPixels(state, 56), PanelPixels(state, 52), PanelPixels(state, 52), PanelPixels(state, 12));

    private static void AppendPanelDefs(StringBuilder svg, SocialCardRenderState state)
    {
        svg.AppendLine(@"    <radialGradient id=""panelGlow"" cx=""85%"" cy=""8%"" r=""65%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{state.Palette.Accent}"" stop-opacity=""0.28""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{state.Palette.Accent}"" stop-opacity=""0""/>");
        svg.AppendLine(@"    </radialGradient>");

        var panel = GetPanelVisualRect(state);
        if (panel is null || !PanelShowsImage(state))
            return;

        var image = GetPanelImageRect(panel, state);
        svg.AppendLine(@"    <clipPath id=""panelMediaClip"">");
        svg.AppendLine($@"      <rect x=""{image.X}"" y=""{image.Y}"" width=""{image.Width}"" height=""{image.Height}"" rx=""{image.Radius}"" ry=""{image.Radius}""/>");
        svg.AppendLine(@"    </clipPath>");
    }

    private static void AppendPanelBackground(StringBuilder svg, SocialCardRenderState state)
    {
        svg.AppendLine($@"  <rect width=""{state.Width}"" height=""{state.Height}"" fill=""url(#bg)""/>");
        svg.AppendLine($@"  <rect width=""{state.Width}"" height=""{state.Height}"" fill=""url(#panelGlow)""/>");
        svg.AppendLine($@"  <rect x=""0"" y=""0"" width=""{Math.Max(4, PanelPixels(state, 10))}"" height=""{state.Height}"" fill=""{state.Palette.Accent}""/>");
    }

    private static void AppendPanelLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var x = PanelPixels(state, 84);
        var rightEdge = state.Width - PanelPixels(state, 60);
        var panel = GetPanelVisualRect(state);
        var textWidth = panel is null ? rightEdge - x : Math.Max(PanelPixels(state, 320), panel.X - PanelPixels(state, 56) - x);

        // Identity row: logo and site name.
        var logo = GetPanelLogoFrame(state);
        AppendLogo(svg, state, logo.X, logo.Y, logo.Width);
        var nameFontSize = PanelPixels(state, 28);
        svg.AppendLine($@"  <text x=""{logo.X + logo.Width + PanelPixels(state, 16)}"" y=""{GetCenteredTextBaseline(logo.Y, logo.Height, nameFontSize)}"" fill=""{state.Palette.TextPrimary}"" font-size=""{nameFontSize}"" font-family=""{EscapeXml(state.Typography.TitleFontFamily)}"" font-weight=""800"">{EscapeXml(TrimSingleLine(state.Eyebrow, 32))}</text>");

        // Eyebrow: what kind of page this is (product category, project type).
        var eyebrowFontSize = PanelPixels(state, 20);
        svg.AppendLine($@"  <text x=""{x}"" y=""{PanelPixels(state, 170)}"" fill=""{state.Palette.AccentSoft}"" font-size=""{eyebrowFontSize}"" font-family=""{EscapeXml(state.Typography.EyebrowFontFamily)}"" font-weight=""700"" letter-spacing=""{PanelPixels(state, 1.6)}"">{EscapeXml(TrimSingleLine(state.Badge.ToUpperInvariant(), 40))}</text>");

        // Title: steps down from 54 to 44 when it is truncated or needs more than two lines.
        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 54, 30, "socialCard", "panelTitleFontSize");
        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, PanelPixels(state, 59), textWidth, 3, state.Width, state.Height);
        if (titleLines.Count > 2 && titleFontSize == baseTitleFontSize)
        {
            titleFontSize = Math.Max(24, (int)Math.Round(baseTitleFontSize * 0.82));
            titleLineHeight = EnsureReadableLineHeight(titleFontSize, (int)Math.Round(titleFontSize * 1.12));
            titleLines = WrapText(state.Title, GetTitleWrapWidth(textWidth, titleFontSize), 3);
        }

        var titleTop = PanelPixels(state, 192);
        var titleBaseline = titleTop + (int)Math.Round(titleFontSize * 0.86);
        AppendTitle(svg, state, titleLines, x, titleBaseline, titleFontSize, titleLineHeight);
        var lastTitleBaseline = titleBaseline + (Math.Max(0, titleLines.Count - 1) * titleLineHeight);

        // Chips row position is fixed, so the description fills whatever space the title leaves.
        var chipHeight = PanelPixels(state, 42);
        var chipsY = PanelPixels(state, 538);
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 24, 14, "socialCard", "panelDescriptionFontSize");
        var descLineHeight = PanelPixels(state, 33);
        var descTop = lastTitleBaseline + PanelPixels(state, 26);
        var descBottom = chipsY - PanelPixels(state, 26);
        var descLineCount = descBottom - descTop < descFontSize
            ? 0
            : Math.Clamp(((descBottom - descTop - descFontSize) / Math.Max(1, descLineHeight)) + 1, 0, 3);
        if (descLineCount > 0)
        {
            var descLines = WrapText(state.Description, GetDescriptionWrapWidth(textWidth, descFontSize), descLineCount);
            AppendDescription(svg, state, descLines, x, descTop + descFontSize, descFontSize, descLineHeight);
        }

        // Site address on the right, chips on the left. Chips that do not fit are dropped, not shortened.
        var chipFontSize = PanelPixels(state, 19);
        var chipsLimit = rightEdge;
        if (!string.IsNullOrWhiteSpace(state.SiteAddress))
        {
            var address = TrimSingleLine(state.SiteAddress, 40);
            svg.AppendLine($@"  <text x=""{rightEdge}"" y=""{GetCenteredTextBaseline(chipsY, chipHeight, chipFontSize)}"" fill=""{state.Palette.TextSecondary}"" font-size=""{chipFontSize}"" font-family=""{EscapeXml(state.Typography.FooterFontFamily)}"" font-weight=""700"" text-anchor=""end"">{EscapeXml(address)}</text>");
            chipsLimit = rightEdge - EstimateTextWidth(address, chipFontSize, glyphFactor: 0.58) - PanelPixels(state, 28);
        }

        var chipX = x;
        var chipGap = PanelPixels(state, 10);
        var chipPadding = PanelPixels(state, 18);
        foreach (var chip in state.Chips)
        {
            var label = TrimSingleLine(chip, 28);
            var chipWidth = EstimateTextWidth(label, chipFontSize, glyphFactor: 0.58) + (chipPadding * 2);
            if (chipX + chipWidth > chipsLimit)
                break;

            svg.AppendLine($@"  <rect x=""{chipX}"" y=""{chipsY}"" width=""{chipWidth}"" height=""{chipHeight}"" rx=""{chipHeight / 2}"" fill=""{state.Palette.ChipBackground}"" fill-opacity=""0.72"" stroke=""{state.Palette.ChipBorder}"" stroke-opacity=""0.9""/>");
            svg.AppendLine($@"  <text x=""{chipX + (chipWidth / 2)}"" y=""{GetCenteredTextBaseline(chipsY, chipHeight, chipFontSize)}"" fill=""{state.Palette.ChipText}"" font-size=""{chipFontSize}"" font-family=""{EscapeXml(state.Typography.BodyFontFamily)}"" font-weight=""700"" text-anchor=""middle"">{EscapeXml(label)}</text>");
            chipX += chipWidth + chipGap;
        }

        if (panel is not null)
            AppendPanelVisual(svg, state, panel);
    }

    private static void AppendPanelVisual(StringBuilder svg, SocialCardRenderState state, SocialRect panel)
    {
        svg.AppendLine($@"  <rect x=""{panel.X}"" y=""{panel.Y}"" width=""{panel.Width}"" height=""{panel.Height}"" rx=""{panel.Radius}"" fill=""{state.Palette.Surface}"" fill-opacity=""0.55"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.6""/>");

        if (PanelShowsImage(state))
        {
            // In the raster pass the image is composited afterwards (see CompositeMedia).
            if (state.EmbedReferencedMediaInSvg)
            {
                var image = GetPanelImageRect(panel, state);
                var href = EscapeXml(state.InlineImageDataUri ?? string.Empty);
                svg.AppendLine($@"  <image href=""{href}"" xlink:href=""{href}"" x=""{image.X}"" y=""{image.Y}"" width=""{image.Width}"" height=""{image.Height}"" preserveAspectRatio=""xMidYMid meet"" clip-path=""url(#panelMediaClip)""/>");
            }
            return;
        }

        // One stat per row: the value on the left, its label on the right, divided by hairlines.
        var rows = GetPanelStatRows(state);
        var inset = PanelPixels(state, 30);
        var rowHeight = (panel.Height - (inset * 2)) / Math.Max(1, rows.Count);
        var valueFontSize = Math.Min(PanelPixels(state, rows.Count <= 3 ? 50 : 44), (int)Math.Round(rowHeight * 0.62));
        var labelFontSize = PanelPixels(state, 20);
        var x = panel.X + inset;
        var right = panel.X + panel.Width - inset;
        var top = panel.Y + inset;
        for (var i = 0; i < rows.Count; i++)
        {
            var metric = rows[i];
            var rowTop = top + (i * rowHeight);
            var baseline = GetCenteredTextBaseline(rowTop, rowHeight, valueFontSize);
            var color = IsSafeCssColor(metric.Color) ? metric.Color!.Trim() : state.Palette.AccentSoft;
            svg.AppendLine($@"  <text x=""{x}"" y=""{baseline}"" fill=""{color}"" font-size=""{valueFontSize}"" font-family=""{EscapeXml(state.Typography.TitleFontFamily)}"" font-weight=""800"">{EscapeXml(TrimSingleLine(metric.Value, 10))}</text>");
            if (!string.IsNullOrWhiteSpace(metric.Label))
                svg.AppendLine($@"  <text x=""{right}"" y=""{baseline}"" fill=""{state.Palette.TextSecondary}"" font-size=""{labelFontSize}"" font-family=""{EscapeXml(state.Typography.BodyFontFamily)}"" font-weight=""700"" text-anchor=""end"">{EscapeXml(TrimSingleLine(metric.Label, 14))}</text>");
            if (i < rows.Count - 1)
                svg.AppendLine($@"  <rect x=""{x}"" y=""{rowTop + rowHeight}"" width=""{right - x}"" height=""1"" fill=""{state.Palette.SurfaceStroke}"" fill-opacity=""0.7""/>");
        }
    }

    // A page-specific accent (for example a product's brand colour) replaces the theme accent and its soft/strong tones.
    private static SocialPalette ApplyAccentOverride(SocialPalette palette, string? accentColor)
    {
        if (!TryParseHexColor(accentColor, out _, out _, out _))
            return palette;

        var accent = accentColor!.Trim();
        return new SocialPalette(
            palette.BackgroundStart,
            palette.BackgroundMid,
            palette.BackgroundEnd,
            palette.Surface,
            palette.SurfaceStroke,
            accent,
            BlendHexColor(accent, "#ffffff", 0.38),
            BlendHexColor(accent, "#000000", 0.2),
            palette.TextPrimary,
            palette.TextSecondary,
            palette.ChipBackground,
            palette.ChipBorder,
            palette.ChipText);
    }
}
