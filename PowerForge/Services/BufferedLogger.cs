using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Captures log messages in memory so callers can replay or serialize them later.
/// </summary>
public sealed class BufferedLogger : ILogger
{
    /// <summary>
    /// Gets or sets whether verbose messages should be captured.
    /// </summary>
    public bool IsVerbose { get; set; }

    /// <summary>
    /// Gets the buffered log entries.
    /// </summary>
    public List<BufferedLogEntry> Entries { get; } = new();

    /// <summary>
    /// Records an informational message.
    /// </summary>
    public void Info(string message) => Entries.Add(new BufferedLogEntry("info", message));

    /// <summary>
    /// Records a success message.
    /// </summary>
    public void Success(string message) => Entries.Add(new BufferedLogEntry("success", message));

    /// <summary>
    /// Records a warning message.
    /// </summary>
    public void Warn(string message) => Entries.Add(new BufferedLogEntry("warn", message));

    /// <summary>
    /// Records an error message.
    /// </summary>
    public void Error(string message) => Entries.Add(new BufferedLogEntry("error", message));

    /// <summary>
    /// Records a verbose message when <see cref="IsVerbose"/> is enabled.
    /// </summary>
    public void Verbose(string message)
    {
        if (!IsVerbose) return;
        Entries.Add(new BufferedLogEntry("verbose", message));
    }
}
