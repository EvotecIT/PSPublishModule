using System;
using System.Collections.Generic;
using ConsoleMonster;
using ConsoleMonster.Canvas;
using ConsoleMonster.Glyphs;
using ConsoleMonster.Styling;
using ConsoleMonster.Widgets;
using ConsoleMonster.Layout;
using ConsoleMonster.Text;

namespace PowerForge.ConsoleUi;

/// <summary>One log message: a level marker and the whole message, wrapped under itself.</summary>
internal sealed class MessageLine(string level, string text) : IWidget
{
    public Size Measure(Constraints constraints, RenderContext context)
    {
        var lines = Lines(constraints.MaxWidth, context).Count;
        return new Size(constraints.MaxWidth, constraints.IsHeightUnbounded ? lines : Math.Min(lines, constraints.MaxHeight));
    }

    public void Render(Region region, RenderContext context)
    {
        var lines = Lines(region.Width, context);
        for (var y = 0; y < lines.Count && y < region.Height; y++) region.Write(0, y, lines[y]);
    }

    private List<StyledLine> Lines(int width, RenderContext context)
    {
        var (status, role) = level switch
        {
            "pass" => (StatusKind.Pass, ThemeToken.Text),
            "warn" => (StatusKind.Warn, ThemeToken.Warn),
            "err" => (StatusKind.Fail, ThemeToken.Fail),
            "verbose" => (StatusKind.Skip, ThemeToken.Muted),
            _ => (StatusKind.Info, ThemeToken.Text)
        };
        var marker = context.Glyphs.StatusMarker(status) + " ";
        var indent = DisplayWidth.Measure(marker);
        var result = new List<StyledLine>();
        var body = Math.Max(1, width - indent);
        foreach (var part in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            foreach (var line in TextWrapper.Wrap(StyledLine.From(part, context.Style(role)), body))
            {
                var spans = new List<TextSpan> { new(result.Count == 0 ? marker : new string(' ', indent), context.Style(status)) };
                spans.AddRange(line);
                result.Add(new StyledLine(spans));
            }
        }
        return result;
    }
}
