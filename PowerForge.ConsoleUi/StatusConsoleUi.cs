using System;

namespace PowerForge.ConsoleUi;

/// <summary>
/// Shows one running row with its elapsed time, the messages logged meanwhile and the final state, for operations
/// without detailed progress events.
/// </summary>
internal static class StatusConsoleUi
{
    private const string Key = "operation";

    internal static T Run<T>(string title, Func<T> action, Func<T, bool>? succeeded = null)
        => Run(new ProgressView(), title, action, succeeded);

    internal static T Run<T>(ProgressView view, string title, Func<T> action, Func<T, bool>? succeeded = null)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (action is null) throw new ArgumentNullException(nameof(action));
        T? result = default;
        view.Step(Key, title, "Action");
        view.Run(() =>
        {
            view.Start(Key);
            try
            {
                result = action();
                view.Finish(Key, succeeded is null || succeeded(result) ? ProgressState.Completed : ProgressState.Failed);
            }
            catch
            {
                view.Finish(Key, ProgressState.Failed);
                throw;
            }
        });
        return result!;
    }
}
