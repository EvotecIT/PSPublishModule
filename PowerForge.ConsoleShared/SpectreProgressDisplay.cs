using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PowerForge.ConsoleShared;

/// <summary>
/// Runs a Spectre progress display and preserves its final frame as normal terminal output.
/// </summary>
internal static class SpectreProgressDisplay
{
    private static readonly AsyncLocal<DisplaySession?> Current = new();
    private static readonly ConditionalWeakTable<ProgressContext, DisplaySession> Sessions = new();

    // PowerShell stream writes bypass Spectre's render hook. Clear the live area
    // and serialize its refreshes while the host writes (or prompts) synchronously.
    internal static void WriteExternal(Action write)
    {
        if (Current.Value is { } session) session.WriteExternal(write);
        else write();
    }

    internal static void Refresh(ProgressContext context)
    {
        if (Sessions.TryGetValue(context, out var session)) session.Refresh();
        else context.Refresh();
    }

    public static void Run(
        ProgressColumn[] columns,
        Action<ProgressContext> action)
        => Run(AnsiConsole.Console, columns, action);

    internal static void Run(
        IAnsiConsole console,
        ProgressColumn[] columns,
        Action<ProgressContext> action,
        Action<IReadOnlyList<ProgressTask>>? taskObserver = null,
        Func<IReadOnlyList<ProgressTask>, IRenderable?>? activeStatus = null)
    {
        if (console is null) throw new ArgumentNullException(nameof(console));
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (action is null) throw new ArgumentNullException(nameof(action));

        IRenderable? finalFrame = null;
        var viewport = new SpectreProgressViewportState();
        DisplaySession? session = null;
        console.Progress()
            .AutoRefresh(false)
            .AutoClear(true)
            .HideCompleted(false)
            .Columns(columns)
            .UseRenderHook((renderable, tasks) =>
            {
                if (session?.Suspended == true) return new Text(string.Empty);
                finalFrame = renderable;
                taskObserver?.Invoke(tasks);
                var status = activeStatus?.Invoke(tasks);
                var frame = status is null
                    ? viewport.Project(renderable, tasks, Math.Max(1, console.Profile.Height - 2))
                    : new ActiveProgressRenderable(renderable, tasks, status, viewport, console.Profile.Height);
                return new TrackedRenderable(frame, session, console.Profile.Height);
            })
            .Start(context =>
            {
                using var display = new DisplaySession(console, context);
                session = display;
                Sessions.Add(context, display);
                var previous = Current.Value;
                Current.Value = display;
                try { action(context); }
                finally
                {
                    Current.Value = previous;
                    Sessions.Remove(context);
                }
            });

        if (finalFrame is not null)
            console.Write(finalFrame);
    }

    private sealed class TrackedRenderable(IRenderable inner, DisplaySession? session, int height) : IRenderable
    {
        public Measurement Measure(RenderOptions options, int maxWidth) => inner.Measure(options, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            var segments = inner.Render(options, maxWidth).ToArray();
            // Spectre pads the live frame above and below, and retains its maximum height.
            session?.TrackHeight(Math.Min(height, Segment.SplitLines(segments).Count + 2));
            return segments;
        }
    }

    // Measure the wrapped detail at the actual render width. A fixed footer height
    // lets long paths or PIN guidance scroll the live area on compact terminals.
    private sealed class ActiveProgressRenderable(
        IRenderable progress,
        IReadOnlyList<ProgressTask> tasks,
        IRenderable status,
        SpectreProgressViewportState viewport,
        int height) : IRenderable
    {
        public Measurement Measure(RenderOptions options, int maxWidth)
            => ((IRenderable)new Rows(progress, status)).Measure(options, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            var statusHeight = Segment.SplitLines(status.Render(options, maxWidth)).Count;
            return ((IRenderable)new Rows(
                viewport.Project(progress, tasks, Math.Max(1, height - statusHeight - 2)),
                status)).Render(options, maxWidth);
        }
    }

    private sealed class DisplaySession : IDisposable
    {
        private readonly object _gate = new();
        private readonly ProgressContext _context;
        private readonly IAnsiConsole _console;
        private readonly Timer _timer;
        private bool _closed;
        private int _height;
        internal bool Suspended { get; private set; }

        internal DisplaySession(IAnsiConsole console, ProgressContext context)
        {
            _console = console;
            _context = context;
            _timer = new Timer(_ =>
            {
                try { Refresh(); } catch { /* A refresh must not terminate the build. */ }
            }, null, 100, 100);
        }

        internal void TrackHeight(int height) => _height = Math.Max(_height, height);

        internal void Refresh()
        {
            lock (_gate)
                if (!_closed) _context.Refresh();
        }

        internal void WriteExternal(Action write)
        {
            lock (_gate)
            {
                Suspended = true;
                try
                {
                    _context.Refresh();
                    if (_console.Profile.Capabilities.Ansi)
                        _console.WriteAnsi(writer =>
                        {
                            writer.Write("\r");
                            if (_height > 1) writer.CursorUp(_height - 1);
                            writer.ShowCursor();
                        });
                    write();
                }
                finally
                {
                    Suspended = false;
                    // Raw host output moves the cursor without notifying Spectre.
                    // Reserve a fresh live area below it so later refreshes cannot erase the warning.
                    if (_console.Profile.Capabilities.Ansi)
                        _console.WriteAnsi(writer =>
                        {
                            for (var row = 1; row < _height; row++) writer.Write("\r\n");
                            writer.HideCursor();
                        });
                    if (!_closed) _context.Refresh();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate) _closed = true;
            _timer.Dispose();
        }
    }
}
