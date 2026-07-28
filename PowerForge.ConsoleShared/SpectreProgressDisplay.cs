using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PowerForge.ConsoleShared;

/// <summary>
/// Runs a Spectre progress display and preserves its final frame as normal terminal output.
/// </summary>
internal static class SpectreProgressDisplay
{
    public static void Run(
        ProgressColumn[] columns,
        Action<ProgressContext> action)
        => Run(AnsiConsole.Console, columns, action);

    internal static void Run(
        IAnsiConsole console,
        ProgressColumn[] columns,
        Action<ProgressContext> action,
        Action<IReadOnlyList<ProgressTask>>? taskObserver = null)
    {
        if (console is null) throw new ArgumentNullException(nameof(console));
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (action is null) throw new ArgumentNullException(nameof(action));

        IRenderable? finalFrame = null;
        console.Progress()
            .AutoRefresh(true)
            .AutoClear(true)
            .HideCompleted(false)
            .Columns(columns)
            .UseRenderHook((renderable, tasks) =>
            {
                finalFrame = renderable;
                taskObserver?.Invoke(tasks);
                return renderable;
            })
            .Start(action);

        if (finalFrame is not null)
            console.Write(finalFrame);
    }
}
