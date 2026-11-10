using System.Collections.Generic;

namespace PowerForge.ConsoleUi;

/// <summary>Console markup for <see cref="FormattingSummary"/> parts in the pipeline summary.</summary>
internal static class FormattingSummaryMarkup
{
    public static string FormatPart(string label, FormattingSummary s)
    {
        label ??= string.Empty;
        if (s is null) return $"{label} [grey]0[/]/0";

        var c = s.Changed > 0 ? $"[green]{s.Changed}[/]" : "[grey]0[/]";
        var baseText = $"{label} {c}[grey]/{s.Total}[/]";

        var extras = new List<string>(2);
        if (s.Skipped > 0) extras.Add($"skipped [yellow]{s.Skipped}[/]");
        if (s.Errors > 0) extras.Add($"errors [red]{s.Errors}[/]");
        if (extras.Count > 0) baseText += $" [grey]({string.Join(", ", extras)})[/]";
        return baseText;
    }
}
