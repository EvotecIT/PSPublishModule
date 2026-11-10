using System;
using System.Collections.Generic;
using System.Linq;
using ConsoleMonster.Canvas;
using ConsoleMonster.Glyphs;
using Glyph = ConsoleMonster.Glyphs.Glyph;
using ConsoleMonster.Layout;
using ConsoleMonster.Styling;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;

namespace PowerForge.ConsoleUi;

internal sealed partial class ProgressView
{
    private const int CurrentLines = 3;
    private static readonly TimeSpan QuietAfter = TimeSpan.FromSeconds(5);

    // What "Current:" shows, read under the view's lock.
    // The furthest running row names the work: its step, then the stage and item under it, and the nearest detail.
    private (string? Label, string[] Items, string? Detail, TimeSpan Quiet, bool Signing, TimeSpan Idle, bool Started, bool Finished) CurrentWork()
    {
        lock (_gate)
        {
            var now = _clock();
            var running = InOrder(_rows.Values.Where(row => row.State == ProgressState.Started)).LastOrDefault();
            var started = _steps.Any(step => step.State != ProgressState.Planned);
            if (running is null) return (null, Array.Empty<string>(), null, TimeSpan.Zero, false, now - _idleSince, started, _finished);

            var chain = new[] { running.Step, running.Parent, running }.Where(row => row is not null).Select(row => row!).Distinct().ToArray();
            var step = chain[0];
            var detail = chain.Reverse().Select(row => row.Detail).FirstOrDefault(value => value is not null);
            var activity = chain.Max(row => row.LastActivity);
            var signing = chain.Any(row => IsSigning(row.Kind));
            return (step.Label, chain.Skip(1).Select(row => row.Label).ToArray(), detail, now - activity, signing, TimeSpan.Zero, started, _finished);
        }
    }


    private static bool IsSigning(string? kind)
        => kind?.Trim().ToLowerInvariant() is "signing" or "packagesigning" or "msisign";

    // The live area: the ledger takes the rows left after "Current:" and the message log, which takes none until a
    // message arrives.
    private Rows LiveLayout()
        => new Rows()
            .Add(_ledger, LayoutSize.Fraction())
            .Add(new Lines((width, context) => CurrentRows(this, width, context)))
            .Add(new Lines((_, context) => _log.Entries.Count == 0
                ? new List<StyledLine>()
                : [StyledLine.From($"Messages ({_log.Entries.Count}; the complete log follows progress)", context.Style(ThemeToken.Muted))]))
            .Add(_log);

    // Rows computed for the width they are drawn at.
    private sealed class Lines(Func<int, RenderContext, List<StyledLine>> lines) : IWidget
    {
        public Size Measure(Constraints constraints, RenderContext context)
        {
            var count = lines(constraints.MaxWidth, context).Count;
            return new Size(constraints.MaxWidth, constraints.IsHeightUnbounded ? count : Math.Min(count, constraints.MaxHeight));
        }

        public void Render(Region region, RenderContext context)
        {
            var rows = lines(region.Width, context);
            for (var y = 0; y < rows.Count && y < region.Height; y++) region.Write(0, y, rows[y]);
        }
    }

    private static List<StyledLine> CurrentRows(ProgressView view, int width, RenderContext context)
    {
        var rows = new List<StyledLine>();
        var work = view.CurrentWork();
        if (work.Finished || width <= 0) return rows;
        var muted = context.Style(ThemeToken.Muted);
        var dot = " " + context.Glyphs.Get(Glyph.Dot) + " ";

        if (work.Label is null)
        {
            var waiting = work.Started ? "Preparing next step" : "Starting";
            rows.Add(new StyledLine(new[]
            {
                new TextSpan(context.Spinner() + " ", context.Style(ThemeToken.Accent)),
                new TextSpan(waiting + dot + NumberFormat.Duration(work.Idle), muted)
            }));
            return rows;
        }

        var spans = new List<TextSpan>
        {
            new("Current: ", context.Style(ThemeToken.Accent)),
            new(work.Label, context.Style(ThemeToken.Text))
        };
        foreach (var item in work.Items) spans.Add(new TextSpan(" " + context.Glyphs.Get(Glyph.Chevron) + " " + item, context.Style(ThemeToken.Text)));
        if (work.Detail is not null) spans.Add(new TextSpan(" - " + work.Detail, context.Style(ThemeToken.Text)));
        if (work.Quiet >= QuietAfter) spans.Add(new TextSpan(dot + "last activity " + NumberFormat.Duration(work.Quiet) + " ago", muted));
        var wrapped = TextWrapper.Wrap(new StyledLine(spans), width);
        rows.AddRange(wrapped.Take(CurrentLines));
        if (wrapped.Count > CurrentLines)
            rows[CurrentLines - 1] = TextWrapper.Truncate(rows[CurrentLines - 1], Math.Max(1, width - 1), context.Ellipsis);

        if (work.Signing)
            rows.AddRange(TextWrapper.Wrap(StyledLine.From(ModuleSigningProgressProtocol.UserGuidance, context.Style(ThemeToken.Warn)), width));
        return rows;
    }
}

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
