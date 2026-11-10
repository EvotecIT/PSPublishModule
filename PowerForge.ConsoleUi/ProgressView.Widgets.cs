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
    private (string? Label, string? Item, string? Detail, TimeSpan Quiet, bool Signing, TimeSpan Idle, bool Started, bool Finished) CurrentWork()
    {
        lock (_gate)
        {
            var now = _clock();
            var running = _rows.Values
                .Where(row => row.State == ProgressState.Started)
                .OrderBy(row => row.Index).ThenBy(row => row.ItemIndex)
                .LastOrDefault();
            var started = _steps.Any(step => step.State != ProgressState.Planned);
            if (running is null) return (null, null, null, TimeSpan.Zero, false, now - _idleSince, started, _finished);

            var step = running.Step ?? running;
            var label = running.Step is null ? running.Label : running.Step.Label;
            var item = running.Step is null ? null : running.Label;
            var detail = running.Detail ?? (running.Step is null ? null : running.Step.Detail);
            var activity = running.Step is null ? running.LastActivity : Max(running.LastActivity, running.Step.LastActivity);
            var signing = IsSigning(running.Kind) || IsSigning(step.Kind);
            return (label, item, detail, now - activity, signing, TimeSpan.Zero, started, _finished);
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static bool IsSigning(string? kind)
        => kind?.Trim().ToLowerInvariant() is "signing" or "packagesigning" or "msisign";

    // The live area: the ledger takes the rows left after "Current:" and the message log.
    private sealed class Layout(ProgressView view) : IContainer
    {
        public IReadOnlyList<IWidget> Children => new IWidget[] { view._ledger, view._log };

        public Size Measure(Constraints constraints, RenderContext context)
        {
            var footer = FooterHeight(constraints.MaxWidth, context);
            var available = constraints.IsHeightUnbounded ? Constraints.Unbounded : Math.Max(0, constraints.MaxHeight - footer);
            var ledger = view._ledger.Measure(new Constraints(constraints.MaxWidth, available), context).Height;
            var height = ledger + footer;
            return new Size(constraints.MaxWidth, constraints.IsHeightUnbounded ? height : Math.Min(height, constraints.MaxHeight));
        }

        public void Render(Region region, RenderContext context)
        {
            var current = CurrentRows(view, region.Width, context);
            var messages = MessageRows();
            var ledgerHeight = Math.Max(0, region.Height - current.Count - messages);
            ledgerHeight = Math.Min(ledgerHeight, view._ledger.Measure(new Constraints(region.Width, ledgerHeight), context).Height);
            if (ledgerHeight > 0) view._ledger.Render(region.Sub(0, 0, region.Width, ledgerHeight), context);

            var y = ledgerHeight;
            foreach (var line in current)
            {
                if (y >= region.Height) return;
                region.Write(0, y++, line);
            }

            if (messages == 0 || y >= region.Height) return;
            var total = view._log.Entries.Count;
            region.Write(0, y++, $"Messages ({total}; the complete log follows progress)", context.Style(ThemeToken.Muted));
            var logHeight = Math.Min(messages - 1, region.Height - y);
            if (logHeight > 0) view._log.Render(region.Sub(0, y, region.Width, logHeight), context);
        }

        private int FooterHeight(int width, RenderContext context) => CurrentRows(view, width, context).Count + MessageRows();

        private int MessageRows()
        {
            var entries = view._log.Entries.Count;
            return entries == 0 ? 0 : 1 + Math.Min(view._log.PreferredHeight, entries);
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
        if (work.Item is not null) spans.Add(new TextSpan(" " + context.Glyphs.Get(Glyph.Chevron) + " " + work.Item, context.Style(ThemeToken.Text)));
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
