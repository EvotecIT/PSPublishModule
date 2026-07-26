using System;
using System.Collections.Concurrent;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PowerForge.ConsoleShared;

/// <summary>
/// Creates the common Spectre column layout for build and release progress views.
/// </summary>
internal static class SpectreBuildProgressColumns
{
    /// <summary>
    /// Creates a left-anchored progress layout matching the module pipeline presentation.
    /// </summary>
    public static ProgressColumn[] CreateStandard()
        =>
        [
            new LeftMarkupDescriptionColumn(),
            new ProgressBarColumn
            {
                CompletedStyle = new Style(Color.Green),
                FinishedStyle = new Style(Color.Green)
            },
            new PercentageColumn(),
            new ElapsedTimeColumn(),
            new SpinnerColumn()
        ];

    /// <summary>
    /// Creates the common detailed-step layout used by module and executable pipelines.
    /// </summary>
    public static ProgressColumn[] CreateDetailed(
        bool includeBar,
        bool includeElapsed,
        int barWidth,
        ConcurrentDictionary<ProgressTask, string> iconLookup,
        ConcurrentDictionary<ProgressTask, DateTimeOffset> startLookup,
        ConcurrentDictionary<ProgressTask, TimeSpan> doneLookup)
    {
        if (iconLookup is null) throw new ArgumentNullException(nameof(iconLookup));
        if (startLookup is null) throw new ArgumentNullException(nameof(startLookup));
        if (doneLookup is null) throw new ArgumentNullException(nameof(doneLookup));

        var columns = new System.Collections.Generic.List<ProgressColumn>
        {
            new StepIconColumn(iconLookup),
            new LeftDescriptionColumn()
        };

        if (includeBar)
        {
            columns.Add(new ProgressBarColumn
            {
                Width = barWidth,
                CompletedStyle = new Style(Color.Green),
                FinishedStyle = new Style(Color.Green),
                IndeterminateStyle = new Style(Color.Grey)
            });
        }

        columns.Add(new PercentageColumn());

        if (includeElapsed)
            columns.Add(new FixedElapsedColumn(startLookup, doneLookup));

        columns.Add(new SpinnerColumn());
        return columns.ToArray();
    }

    private sealed class StepIconColumn : ProgressColumn
    {
        private readonly ConcurrentDictionary<ProgressTask, string> _icons;

        public StepIconColumn(ConcurrentDictionary<ProgressTask, string> icons)
            => _icons = icons;

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            var icon = _icons.TryGetValue(task, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : string.Empty;
            return new Panel(new Markup(icon))
            {
                Border = BoxBorder.None,
                Padding = new Padding(0, 0, 0, 0),
                Width = 2
            };
        }
    }

    private sealed class LeftDescriptionColumn : ProgressColumn
    {
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            var text = new Text(task.Description ?? string.Empty);
            try { text.Overflow = Overflow.Ellipsis; } catch { }
            return text;
        }
    }

    private sealed class LeftMarkupDescriptionColumn : ProgressColumn
    {
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
            => new Markup(task.Description ?? string.Empty)
            {
                Justification = Justify.Left,
                Overflow = Overflow.Ellipsis
            };
    }

    private sealed class FixedElapsedColumn : ProgressColumn
    {
        private readonly ConcurrentDictionary<ProgressTask, DateTimeOffset> _start;
        private readonly ConcurrentDictionary<ProgressTask, TimeSpan> _done;

        public FixedElapsedColumn(
            ConcurrentDictionary<ProgressTask, DateTimeOffset> start,
            ConcurrentDictionary<ProgressTask, TimeSpan> done)
        {
            _start = start;
            _done = done;
        }

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            if (_done.TryGetValue(task, out var elapsed))
                return new Markup($"[blue]{elapsed:mm\\:ss}[/]");

            if (_start.TryGetValue(task, out var startedAt))
                return new Markup($"[blue]{(DateTimeOffset.Now - startedAt):mm\\:ss}[/]");

            return new Markup("[blue]00:00[/]");
        }
    }
}
