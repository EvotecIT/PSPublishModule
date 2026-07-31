using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PowerForge.ConsoleShared;

/// <summary>
/// Projects a bounded live viewport over a complete, canonically ordered task list.
/// The underlying task collection and the final frame remain unchanged.
/// </summary>
internal static class SpectreProgressViewport
{
    internal static IRenderable Project(
        IRenderable renderable,
        IReadOnlyList<ProgressTask> tasks,
        int maximumRows)
    {
        if (renderable is null) throw new ArgumentNullException(nameof(renderable));
        if (tasks is null) throw new ArgumentNullException(nameof(tasks));

        maximumRows = Math.Max(1, maximumRows);
        if (tasks.Count <= maximumRows)
            return renderable;

        var activeIndex = FindActiveIndex(tasks);
        var startIndex = Math.Min(
            tasks.Count - maximumRows,
            Math.Max(0, activeIndex - maximumRows + 1));
        return new WindowedRenderable(renderable, startIndex, maximumRows, tasks.Count);
    }

    private static int FindActiveIndex(IReadOnlyList<ProgressTask> tasks)
    {
        for (var index = tasks.Count - 1; index >= 0; index--)
        {
            if (tasks[index].IsStarted && !tasks[index].IsFinished)
                return index;
        }

        for (var index = tasks.Count - 1; index >= 0; index--)
        {
            if (tasks[index].IsStarted)
                return index;
        }

        return 0;
    }

    private sealed class WindowedRenderable : IRenderable
    {
        private readonly IRenderable _inner;
        private readonly int _taskStartIndex;
        private readonly int _maximumRows;
        private readonly int _taskCount;

        internal WindowedRenderable(
            IRenderable inner,
            int taskStartIndex,
            int maximumRows,
            int taskCount)
        {
            _inner = inner;
            _taskStartIndex = taskStartIndex;
            _maximumRows = maximumRows;
            _taskCount = taskCount;
        }

        public Measurement Measure(RenderOptions options, int maxWidth)
            => _inner.Measure(options, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            var lines = Segment.SplitLines(_inner.Render(options, maxWidth));
            if (lines.Count <= _maximumRows)
            {
                foreach (var segment in _inner.Render(options, maxWidth))
                    yield return segment;
                yield break;
            }

            // Progress currently renders one line per task. Preserve any leading
            // structural lines if Spectre adds them in a future release.
            var taskLineOffset = Math.Max(0, lines.Count - _taskCount);
            var startLine = Math.Min(
                Math.Max(0, lines.Count - _maximumRows),
                taskLineOffset + _taskStartIndex);
            var selected = lines
                .Skip(startLine)
                .Take(_maximumRows)
                .ToArray();

            for (var lineIndex = 0; lineIndex < selected.Length; lineIndex++)
            {
                foreach (var segment in selected[lineIndex])
                    yield return segment;

                if (lineIndex < selected.Length - 1)
                    yield return Segment.LineBreak;
            }
        }
    }
}
