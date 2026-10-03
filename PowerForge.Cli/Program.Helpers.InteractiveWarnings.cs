using PowerForge;

internal static partial class Program
{
    private static void ReplayInteractiveWarnings(BufferedLogger? buffer, ILogger logger)
    {
        if (buffer is null) return;
        foreach (var entry in buffer.Entries)
            if (entry.Level == "warn") logger.Warn(entry.Message);
    }
}
