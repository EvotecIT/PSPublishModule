using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PowerForge.ConsoleShared;

/// <summary>
/// Shows the latest message below live progress; the complete log is written on completion.
/// </summary>
internal sealed class SpectreProgressDiagnostics(IRenderable message, int count, int maximumRows) : IRenderable
{
    private IRenderable Header => new Rows(new Text(string.Empty),
        new Text($"Messages ({count}; complete log follows progress)", new Style(Color.Grey)));

    public Measurement Measure(RenderOptions options, int maxWidth)
        => ((IRenderable)new Rows(Header, message)).Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        foreach (var segment in Header.Render(options, maxWidth)) yield return segment;
        yield return Segment.LineBreak;
        var headerRows = Segment.SplitLines(Header.Render(options, maxWidth)).Count;
        var lines = Segment.SplitLines(message.Render(options, maxWidth));
        var available = Math.Max(1, maximumRows - headerRows);
        var truncated = lines.Count > available;
        var shown = truncated ? Math.Max(0, available - 1) : lines.Count;
        for (var row = 0; row < shown; row++)
        {
            foreach (var segment in lines[row]) yield return segment;
            if (row < shown - 1 || truncated) yield return Segment.LineBreak;
        }
        if (truncated)
            foreach (var segment in ((IRenderable)new Text("… full message follows progress", new Style(Color.Grey))).Render(options, maxWidth))
                yield return segment;
    }
}
