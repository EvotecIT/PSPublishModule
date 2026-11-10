using System;
using PowerForge;

namespace PowerForge.ConsoleUi;

/// <summary>
/// Maps engine progress events (release items, project-build items, step states) onto <see cref="ProgressView"/>
/// rows. The view owns order, timing and nesting; this only translates states, labels and details.
/// </summary>
internal static class ProgressLedger
{
    /// <summary>The row label of an engine item: its scoped counter ("Package 03/12") and title.</summary>
    internal static string Label(string? counterLabel, int position, int total, string title)
        => position > 0 && total > 0
            ? ProgressCounterFormatter.Format(counterLabel, position, total) + " " + title
            : title;

    internal static string Label(PowerForgeReleaseProgressItem item, string? counterLabel = null)
        => Label(counterLabel ?? item.CounterLabel, item.Position, item.Total, item.Title);

    /// <summary>Applies one state change; a running row with numeric progress shows "value/maximum" first.</summary>
    internal static void Apply(
        ProgressView view,
        string key,
        ProgressState state,
        string? detail,
        double value = 0,
        double maximum = 0,
        TimeSpan? duration = null)
    {
        switch (state)
        {
            case ProgressState.Started:
                view.Start(key, WithProgress(detail, value, maximum));
                break;
            case ProgressState.Completed:
            case ProgressState.Failed:
            case ProgressState.Skipped:
                view.Finish(key, state, detail, duration);
                break;
        }
    }

    internal static ProgressState From(PowerForgeReleaseProgressItemState state) => state switch
    {
        PowerForgeReleaseProgressItemState.Started => ProgressState.Started,
        PowerForgeReleaseProgressItemState.Completed => ProgressState.Completed,
        PowerForgeReleaseProgressItemState.Failed => ProgressState.Failed,
        PowerForgeReleaseProgressItemState.Skipped => ProgressState.Skipped,
        _ => ProgressState.Planned
    };

    internal static ProgressState From(ProjectBuildProgressItemState state) => state switch
    {
        ProjectBuildProgressItemState.Started => ProgressState.Started,
        ProjectBuildProgressItemState.Completed => ProgressState.Completed,
        ProjectBuildProgressItemState.Failed => ProgressState.Failed,
        ProjectBuildProgressItemState.Skipped => ProgressState.Skipped,
        _ => ProgressState.Planned
    };

    private static string? WithProgress(string? detail, double value, double maximum)
    {
        if (maximum <= 0) return detail;
        var counter = $"{Math.Max(0, Math.Min(value, maximum)):0}/{maximum:0}";
        return string.IsNullOrWhiteSpace(detail) ? counter : counter + " " + detail;
    }
}
