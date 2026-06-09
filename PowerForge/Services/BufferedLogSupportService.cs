namespace PowerForge;

/// <summary>
/// Shared helpers for replaying buffered logs and formatting elapsed durations.
/// </summary>
public sealed class BufferedLogSupportService
{
    /// <summary>
    /// Replays the last buffered log entries through the provided logger.
    /// </summary>
    public void WriteTail(IReadOnlyList<BufferedLogEntry>? entries, ILogger logger, int maxEntries = 80)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));
        if (entries is null || entries.Count == 0)
            return;

        maxEntries = Math.Max(1, maxEntries);
        var total = entries.Count;
        var start = Math.Max(0, total - maxEntries);
        var shown = total - start;

        logger.Warn($"Last {shown}/{total} log lines:");
        for (int i = start; i < total; i++)
        {
            var entry = entries[i];
            var message = entry?.Message ?? string.Empty;
            switch (entry?.Level)
            {
                case "success":
                    logger.Success(message);
                    break;
                case "warn":
                    logger.Warn(message);
                    break;
                case "error":
                    logger.Error(message);
                    break;
                case "verbose":
                    logger.Verbose(message);
                    break;
                default:
                    logger.Info(message);
                    break;
            }
        }
    }

    /// <summary>
    /// Formats a duration into a short human-readable string.
    /// </summary>
    public string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalDays >= 1)
            return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h {elapsed.Minutes}m {elapsed.Seconds}s";
        if (elapsed.TotalHours >= 1)
            return $"{elapsed.Hours}h {elapsed.Minutes}m {elapsed.Seconds}s";
        if (elapsed.TotalMinutes >= 1)
            return $"{elapsed.Minutes}m {elapsed.Seconds}s";
        if (elapsed.TotalSeconds >= 1)
            return $"{elapsed.Seconds}s {elapsed.Milliseconds}ms";
        return $"{elapsed.Milliseconds}ms";
    }
}
