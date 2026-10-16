using System;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

/// <summary>Shows elapsed time and diagnostics for operations without detailed progress events.</summary>
internal static class SpectreStatusConsoleUi
{
    internal static T Run<T>(string title, Func<T> action, Func<T, bool>? succeeded = null)
        => Run(AnsiConsole.Console, title, action, succeeded);

    internal static T Run<T>(IAnsiConsole console, string title, Func<T> action, Func<T, bool>? succeeded = null)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        var presentation = SpectreProgressPresentation.Create(console);
        T? result = default;
        console.WriteLine();
        SpectreProgressDisplay.Run(console, presentation.CreateColumns(), context =>
        {
            var item = new SpectreProgressLedgerItem { Title = title, Kind = "Action" };
            var task = context.AddTask(presentation.BuildLabel(item, null));
            task.IsIndeterminate = true;
            presentation.SetDetail(task, item, SpectreProgressLedgerState.Started, null);
            presentation.MarkStarted(task, item.Kind);
            SpectreProgressDisplay.Refresh(context);
            try
            {
                result = action();
                task.StopTask();
                presentation.MarkTerminal(task, succeeded is null || succeeded(result)
                    ? SpectreProgressLedgerState.Completed
                    : SpectreProgressLedgerState.Failed);
            }
            catch
            {
                task.StopTask();
                presentation.MarkTerminal(task, SpectreProgressLedgerState.Failed);
                throw;
            }
            finally { SpectreProgressDisplay.Refresh(context); }
        }, activeStatus: presentation.RenderActiveStatus);
        return result!;
    }
}
