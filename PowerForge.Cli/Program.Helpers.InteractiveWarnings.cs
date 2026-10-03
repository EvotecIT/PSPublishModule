using PowerForge;

internal static partial class Program
{
    private static void ReplayInteractiveWarnings(BufferedLogger? buffer, ILogger logger)
    {
        if (buffer is null) return;
        foreach (var entry in buffer.Entries)
            if (entry.Level == "warn") logger.Warn(entry.Message);
    }

    private static void WriteInteractiveFailureTail(BufferedLogger buffer, ILogger logger)
    {
        // Warnings are replayed separately, including those older than the bounded diagnostic tail.
        var output = new BufferedLogger { IsVerbose = buffer.IsVerbose };
        foreach (var entry in buffer.Entries)
            if (entry.Level != "warn") output.Entries.Add(entry);
        WriteLogTail(output, logger);
    }
}
