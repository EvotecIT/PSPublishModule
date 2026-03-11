namespace PowerForge;

/// <summary>
/// Represents a buffered log entry captured for later replay or JSON output.
/// </summary>
public sealed class BufferedLogEntry
{
    /// <summary>
    /// Gets the logger level associated with the entry.
    /// </summary>
    public string Level { get; }

    /// <summary>
    /// Gets the log message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Creates a new buffered log entry.
    /// </summary>
    public BufferedLogEntry(string level, string message)
    {
        Level = level ?? "info";
        Message = message ?? string.Empty;
    }
}
