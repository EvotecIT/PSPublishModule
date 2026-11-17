namespace PowerForge;

/// <summary>
/// Bounded views of a child PowerShell process's output, so warnings and failure causes reach the user
/// without dumping an unbounded transcript into the log.
/// </summary>
internal static class ChildProcessOutput
{
    private const string WarningPrefix = "WARNING:";
    internal const int DefaultMaxLines = 20;
    internal const int DefaultMaxChars = 2000;

    /// <summary>The last non-empty lines of <paramref name="text"/>, at most <paramref name="maxLines"/> lines and <paramref name="maxChars"/> characters.</summary>
    internal static string? Tail(string? text, int maxLines = DefaultMaxLines, int maxChars = DefaultMaxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lines = SplitLines(text).ToArray();
        if (lines.Length == 0) return null;

        var kept = lines.Skip(Math.Max(0, lines.Length - maxLines)).ToArray();
        var tail = string.Join(Environment.NewLine, kept);
        if (tail.Length > maxChars) tail = "..." + tail.Substring(tail.Length - maxChars);
        else if (kept.Length < lines.Length) tail = "..." + Environment.NewLine + tail;
        return tail;
    }

    /// <summary>True when <paramref name="line"/> is a PowerShell warning ("WARNING: ..."); <paramref name="message"/> is the text after the prefix.</summary>
    internal static bool TryGetWarning(string? line, out string message)
    {
        message = string.Empty;
        var text = line?.Trim();
        if (text is null || !text.StartsWith(WarningPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        message = text.Substring(WarningPrefix.Length).Trim();
        return message.Length > 0;
    }

    /// <summary>
    /// Logs, at Warn, the warnings a successful child process printed to standard output and anything it wrote to
    /// standard error, each prefixed with <paramref name="source"/>. Output is bounded to <see cref="DefaultMaxLines"/> lines.
    /// </summary>
    internal static void WarnAboutDiagnostics(ILogger logger, string source, string? stdOut, string? stdErr)
    {
        if (logger is null) return;

        var warnings = SplitLines(stdOut)
            .Select(static line => TryGetWarning(line, out var message) ? message : null)
            .Where(static message => message is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var warning in warnings.Take(DefaultMaxLines))
            logger.Warn($"{source}: {warning}");
        if (warnings.Length > DefaultMaxLines)
            logger.Warn($"{source}: {warnings.Length - DefaultMaxLines} more warning(s) omitted.");

        var errors = Tail(stdErr);
        if (errors is not null)
            logger.Warn($"{source} wrote to the error stream:{Environment.NewLine}{errors}");
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimEnd())
            .Where(static line => line.Trim().Length > 0);
}
