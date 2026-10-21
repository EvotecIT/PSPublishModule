using System.Globalization;
using System.Text;
using ImageMagick;

namespace PowerForge.Web;

internal static partial class WebSocialCardGenerator
{
    private static SocialRect GetSplitColumn(SocialCardRenderState state)
    {
        var raw = ReadThemeToken(state.ThemeTokens, "socialCard", "imageShare");
        var share = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0.48;
        if (!double.IsFinite(share) || share is < 0.2 or > 0.65)
            throw new InvalidOperationException("socialCard.imageShare must be between 0.2 and 0.65.");
        var width = (int)Math.Round(state.Width * share);
        return new SocialRect(state.Width - width, 0, width, state.Height, 0);
    }

    private static SocialRect GetSplitImageRect(SocialCardRenderState state)
    {
        var column = GetSplitColumn(state);
        var padding = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 0, 0, "socialCard", "imagePadding");
        padding = Math.Clamp(padding, 0, Math.Min(column.Width, column.Height) / 3);
        return new SocialRect(column.X + padding, padding, column.Width - padding * 2, column.Height - padding * 2, 0);
    }

    private static (string Alignment, Gravity Gravity) GetSplitAnchor(SocialCardRenderState state)
    {
        var anchor = (ReadThemeToken(state.ThemeTokens, "socialCard", "imageAnchor") ?? "center center").Trim().ToLowerInvariant();
        return anchor switch
        {
            "left top" or "top left" => ("xMinYMin", Gravity.Northwest),
            "center top" or "top" => ("xMidYMin", Gravity.North),
            "right top" or "top right" => ("xMaxYMin", Gravity.Northeast),
            "left center" or "left" => ("xMinYMid", Gravity.West),
            "center" or "center center" => ("xMidYMid", Gravity.Center),
            "right center" or "right" => ("xMaxYMid", Gravity.East),
            "left bottom" or "bottom left" => ("xMinYMax", Gravity.Southwest),
            "center bottom" or "bottom" => ("xMidYMax", Gravity.South),
            "right bottom" or "bottom right" => ("xMaxYMax", Gravity.Southeast),
            _ => throw new InvalidOperationException($"Unknown socialCard.imageAnchor '{anchor}'. Use left/center/right and top/center/bottom.")
        };
    }

    private static bool SplitImageCovers(SocialCardRenderState state)
    {
        var fit = ReadThemeToken(state.ThemeTokens, "socialCard", "imageFit") ?? "cover";
        return fit.ToLowerInvariant() switch
        {
            "cover" => true,
            "contain" => false,
            _ => throw new InvalidOperationException($"Unknown socialCard.imageFit '{fit}'. Use cover or contain.")
        };
    }

    private static void AppendSplitDefs(StringBuilder svg, SocialCardRenderState state)
    {
        var media = GetSplitImageRect(state);
        svg.AppendLine($@"    <clipPath id=""splitMediaClip""><rect x=""{media.X}"" y=""{media.Y}"" width=""{media.Width}"" height=""{media.Height}""/></clipPath>");
    }

    private static void AppendSplitLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var column = GetSplitColumn(state);
        var x = PanelPixels(state, 64);
        var textWidth = column.X - x - PanelPixels(state, 40);
        var logo = new SocialRect(x, PanelPixels(state, 54), PanelPixels(state, 48), PanelPixels(state, 48), PanelPixels(state, 12));
        AppendLogo(svg, state, logo.X, logo.Y, logo.Width);
        var nameSize = PanelPixels(state, 25);
        svg.AppendLine($@"  <text x=""{logo.X + logo.Width + PanelPixels(state, 14)}"" y=""{GetCenteredTextBaseline(logo.Y, logo.Height, nameSize)}"" fill=""{state.Palette.TextPrimary}"" font-family=""{EscapeXml(state.Typography.TitleFontFamily)}"" font-size=""{nameSize}"" font-weight=""700"">{EscapeXml(TrimSingleLine(state.Eyebrow, 28))}</text>");
        svg.AppendLine($@"  <text x=""{x}"" y=""{PanelPixels(state, 184)}"" fill=""{state.Palette.AccentSoft}"" font-family=""{EscapeXml(state.Typography.EyebrowFontFamily)}"" font-size=""{PanelPixels(state, 20)}"" font-weight=""700"" letter-spacing=""1.5"">{EscapeXml(state.Badge.ToUpperInvariant())}</text>");
        var baseSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 56, 30, "socialCard", "titleFontSize");
        var (size, lineHeight, lines) = FitCardTitle(state, baseSize, PanelPixels(state, 64), textWidth, 2);
        var baseline = PanelPixels(state, 220) + (int)Math.Round(size * 0.86);
        AppendTitle(svg, state, lines, x, baseline, size, lineHeight);
        var descSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 24, 14, "socialCard", "descriptionFontSize");
        var descY = baseline + (lines.Count - 1) * lineHeight + PanelPixels(state, 34) + descSize;
        var descLines = WrapCardDescription(state, textWidth, descSize, 2);
        AppendDescription(svg, state, descLines, x, descY, descSize, PanelPixels(state, 34));
        svg.AppendLine($@"  <text x=""{x}"" y=""{state.Height - PanelPixels(state, 55)}"" fill=""{state.Palette.TextSecondary}"" font-family=""{EscapeXml(state.Typography.FooterFontFamily)}"" font-size=""{PanelPixels(state, 18)}"" font-weight=""500"">{EscapeXml(string.IsNullOrWhiteSpace(state.SiteAddress) ? state.FooterLabel : state.SiteAddress)}</text>");

        svg.AppendLine($@"  <rect x=""{column.X}"" y=""0"" width=""{column.Width}"" height=""{column.Height}"" fill=""{state.Palette.Surface}""/>");
        if (state.LayoutKey == "code-led")
            AppendCodeColumn(svg, state, column);
        else if (state.EmbedReferencedMediaInSvg && PanelShowsImage(state))
        {
            var media = GetSplitImageRect(state);
            var href = EscapeXml(EmbedSplitImage(state));
            var fit = SplitImageCovers(state) ? "slice" : "meet";
            var (anchor, _) = GetSplitAnchor(state);
            svg.AppendLine($@"  <image href=""{href}"" xlink:href=""{href}"" x=""{media.X}"" y=""{media.Y}"" width=""{media.Width}"" height=""{media.Height}"" preserveAspectRatio=""{anchor} {fit}"" clip-path=""url(#splitMediaClip)""/>");
        }
        svg.AppendLine($@"  <rect x=""{column.X}"" y=""0"" width=""{Math.Max(1, PanelPixels(state, 2))}"" height=""{state.Height}"" fill=""{state.Palette.Accent}"" fill-opacity=""0.5""/>");
    }

    private static void AppendCodeColumn(StringBuilder svg, SocialCardRenderState state, SocialRect column)
    {
        var inset = PanelPixels(state, 38);
        var size = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 25, 14, "socialCard", "codeFontSize");
        var maxLines = int.TryParse(ReadThemeToken(state.ThemeTokens, "socialCard", "codeMaxLines"), out var configuredLines) ? Math.Clamp(configuredLines, 1, 12) : 6;
        var lines = WrapWordsToWidth(string.IsNullOrWhiteSpace(state.Code) ? state.Title : state.Code, column.Width - inset * 2, maxLines,
            text => MeasureCardText(state, text, size, mono: true, title: false));
        var lineHeight = (int)Math.Ceiling(size * 1.6);
        var startY = (state.Height - lines.Count * lineHeight) / 2 + size;
        svg.AppendLine($@"  <text x=""{column.X + inset}"" y=""{startY - PanelPixels(state, 48)}"" fill=""{state.Palette.TextSecondary}"" font-family=""{EscapeXml(state.Typography.MonoFontFamily)}"" font-size=""{PanelPixels(state, 16)}"">{(state.Badge == "PS" ? "POWERSHELL" : "C#")}</text>");
        for (var i = 0; i < lines.Count; i++)
            svg.AppendLine($@"  <text x=""{column.X + inset}"" y=""{startY + i * lineHeight}"" fill=""{state.Palette.AccentSoft}"" font-family=""{EscapeXml(state.Typography.MonoFontFamily)}"" font-size=""{size}"" font-weight=""400"">{EscapeXml(lines[i])}</text>");
    }

    private static void CompositeSplitImage(MagickImage canvas, SocialCardRenderState state)
    {
        var media = GetSplitImageRect(state);
        using var image = CreateSplitImageCanvas(state);
        canvas.Composite(image, media.X, media.Y, CompositeOperator.Over);
    }
}
