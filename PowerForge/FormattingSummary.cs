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

    internal static bool IsErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        if (message.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) return true;
        if (message.Equals("No result returned", StringComparison.OrdinalIgnoreCase)) return true;
        // Treat formatter pipeline runtime failures as errors (not merely "skipped").
        if (message.Contains("PSSA failed", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    internal static bool IsSkippedMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        return message.StartsWith("Skipped:", StringComparison.OrdinalIgnoreCase);
    }
}
