using ImageMagick;
using ImageMagick.Drawing;

namespace PowerForge.Web;

internal static partial class WebSocialCardGenerator
{
    internal static List<string> WrapWords(string? value, int maxChars, int maxLines)
        => WrapWordsToWidth(value, maxChars, maxLines, text => text.Length);

    private static List<string> WrapWordsToWidth(string? value, double maxWidth, int maxLines, Func<string, double> measure)
    {
        var words = NormalizeWrapInput(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;
        var omitted = false;
        foreach (var word in words)
        {
            var next = current.Length == 0 ? word : current + " " + word;
            if (measure(next) <= maxWidth)
            {
                current = next;
                continue;
            }
            if (current.Length > 0)
                lines.Add(current);
            if (lines.Count >= maxLines || measure(word) > maxWidth)
            {
                omitted = true;
                current = string.Empty;
                break;
            }
            current = word;
        }
        if (current.Length > 0 && lines.Count < maxLines)
            lines.Add(current);
        if (omitted)
        {
            if (lines.Count == 0)
                lines.Add(string.Empty);
            var last = lines[^1];
            while (last.Length > 0 && measure(last + "…") > maxWidth)
            {
                var boundary = last.LastIndexOf(' ');
                last = boundary < 0 ? string.Empty : last[..boundary];
            }
            lines[^1] = last.TrimEnd() + "…";
        }
        return lines.Count == 0 ? new List<string> { string.Empty } : lines;
    }

    private static List<string> WrapCardDescription(SocialCardRenderState state, int width, int fontSize, int maxLines)
        => state.TextFit || state.LayoutKey is "split" or "code-led"
            ? WrapWordsToWidth(state.Description, width, maxLines, text => MeasureCardText(state, text, fontSize, mono: false, title: false))
            : WrapText(state.Description, GetDescriptionWrapWidth(width, fontSize), maxLines);

    private static (int FontSize, int LineHeight, List<string> Lines) FitCardTitle(SocialCardRenderState state,
        int baseFontSize, int baseLineHeight, int width, int maxLines = 3)
    {
        if (!state.TextFit && state.LayoutKey is not ("split" or "code-led"))
            return AdaptTitleSize(state.Title, baseFontSize, baseLineHeight, width, maxLines, state.Width, state.Height);
        var minSize = Math.Min(baseFontSize, ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 30, 16, "socialCard", "titleMinFontSize"));
        var fontSize = baseFontSize;
        List<string> lines;
        while (true)
        {
            lines = WrapWordsToWidth(state.Title, width, 2, text => MeasureCardText(state, text, fontSize, mono: false, title: true));
            if (!lines[^1].EndsWith('…') || fontSize <= minSize)
                break;
            fontSize = Math.Max(minSize, fontSize - 2);
        }
        return (fontSize, EnsureReadableLineHeight(fontSize, (int)Math.Round(baseLineHeight * fontSize / (double)baseFontSize)), lines);
    }

    private static double MeasureCardText(SocialCardRenderState state, string text, int size, bool mono, bool title)
    {
        var family = mono ? state.Typography.MonoFontFamily : title ? state.Typography.TitleFontFamily : state.Typography.BodyFontFamily;
        var name = ParseFontFamilyStack(family).FirstOrDefault() ?? family;
        var weight = title ? 800 : mono ? 400 : 500;
        var file = state.Fonts is null ? null : SelectFontFile(name, weight, state.Fonts);
        var path = file?.Path;
        if (file is not null)
            weight = Math.Clamp(weight, file.MinWeight, file.MaxWeight);
        try
        {
            EnsureMagickFontConfigInitialized();
            var font = path ?? NormalizeFontFamilyForRaster(family, family, mono);
            return new Drawables().Font(font, FontStyleType.Normal, (FontWeight)weight, FontStretch.Normal).FontPointSize(size).FontTypeMetrics(text)?.TextWidth
                ?? text.Length * size * (mono ? 0.62 : 0.56);
        }
        catch
        {
            return text.Length * size * (mono ? 0.62 : 0.56);
        }
    }
}
