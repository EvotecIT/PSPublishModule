namespace PowerForge;

/// <summary>
/// Aggregated summary for a set of formatting results.
/// </summary>
public sealed class FormattingSummary
{
    /// <summary>Total number of formatter results included in this summary.</summary>
    public int Total { get; }
    /// <summary>Number of files whose content was modified.</summary>
    public int Changed { get; }
    /// <summary>Number of files that were skipped (e.g., missing runtime/tools, timeout).</summary>
    public int Skipped { get; }
    /// <summary>Number of files that failed to process (errors).</summary>
    public int Errors { get; }
    /// <summary>Aggregate status derived from <see cref="Errors"/> and <see cref="Skipped"/>.</summary>
    public CheckStatus Status { get; }

    private FormattingSummary(int total, int changed, int skipped, int errors, CheckStatus status)
    {
        Total = total;
        Changed = changed;
        Skipped = skipped;
        Errors = errors;
        Status = status;
    }

    /// <summary>
    /// Builds a summary from a sequence of <see cref="FormatterResult"/> items.
    /// </summary>
    public static FormattingSummary FromResults(IEnumerable<FormatterResult>? results)
    {
        if (results is null) return new FormattingSummary(0, 0, 0, 0, CheckStatus.Pass);

        int total = 0, changed = 0, skipped = 0, errors = 0;
        foreach (var r in results)
        {
            if (r is null) continue;
            total++;
            if (r.Changed) changed++;

            var msg = r.Message ?? string.Empty;
            if (IsErrorMessage(msg))
            {
                errors++;
            }
            else if (IsSkippedMessage(msg))
            {
                skipped++;
            }
        }

        var status =
            errors > 0 ? CheckStatus.Fail :
            skipped > 0 ? CheckStatus.Warning :
            CheckStatus.Pass;

        return new FormattingSummary(total, changed, skipped, errors, status);
    }

    /// <summary>
    /// Returns the worst status of two values (<c>Fail</c> &gt; <c>Warning</c> &gt; <c>Pass</c>).
    /// </summary>
    public static CheckStatus Worst(CheckStatus a, CheckStatus b)
        => (a == CheckStatus.Fail || b == CheckStatus.Fail) ? CheckStatus.Fail
            : (a == CheckStatus.Warning || b == CheckStatus.Warning) ? CheckStatus.Warning
            : CheckStatus.Pass;

    /// <summary>
    /// Formats a summary part for logs without markup.
    /// </summary>
    public static string FormatPartPlain(string label, FormattingSummary s)
    {
        label ??= string.Empty;
        if (s is null) return $"{label} 0/0";

        var extras = new List<string>(2);
        if (s.Skipped > 0) extras.Add($"skipped {s.Skipped}");
        if (s.Errors > 0) extras.Add($"errors {s.Errors}");
        var suffix = extras.Count == 0 ? string.Empty : $" ({string.Join(", ", extras)})";
        return $"{label} {s.Changed}/{s.Total}{suffix}";
    }

    /// <summary>
    /// Formats a summary part using Spectre.Console markup.
    /// </summary>
    public static string FormatPartMarkup(string label, FormattingSummary s)
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

    internal static bool IsErrorMessage(string message)
    {
        var token = LeadingToken(message);
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (IsSkippedToken(token)) return false;

        if (token.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) return true;
        if (token.Equals("No result returned", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    internal static bool IsSkippedMessage(string message)
    {
        var token = LeadingToken(message);
        return IsSkippedToken(token);
    }

    private static bool IsSkippedToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        return token.StartsWith("Skipped:", StringComparison.OrdinalIgnoreCase);
    }

    private static string LeadingToken(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        var m = message!;

        // FormattingPipeline appends diagnostic details using "; <key>=<0|1>..." suffix.
        // Preserve classification by only inspecting the leading token.
        var idx = m.IndexOf(';');
        return (idx < 0 ? m : m.Substring(0, idx)).Trim();
    }
}
