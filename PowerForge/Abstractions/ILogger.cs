namespace PowerForge;

/// <summary>
/// Minimal logging abstraction that maps cleanly to PowerShell Write-Verbose/Host styles.
/// Implementations should be fast, thread-safe for simple writes, and avoid logging secrets.
/// </summary>
public interface ILogger
{
    /// <summary>
    /// Logs an informational message (prefix <c>[i]</c> in typical console implementations).
    /// </summary>
    /// <param name="message">Text to log.</param>
    void Info(string message);

    /// <summary>
    /// Logs a success message (prefix <c>[+]</c> in typical console implementations).
    /// </summary>
    /// <param name="message">Text to log.</param>
    void Success(string message);

    /// <summary>
    /// Logs a warning (prefix <c>[-]</c> in typical console implementations).
    /// </summary>
    /// <param name="message">Text to log.</param>
    void Warn(string message);

    /// <summary>
    /// Logs an error (prefix <c>[e]</c> in typical console implementations).
    /// </summary>
    /// <param name="message">Text to log.</param>
    void Error(string message);

    /// <summary>
    /// Logs a verbose/diagnostic message if <see cref="IsVerbose"/> is true (prefix <c>[v]</c>).
    /// </summary>
    /// <param name="message">Text to log.</param>
    void Verbose(string message);

    /// <summary>
    /// Indicates whether verbose/diagnostic messages should be emitted.
    /// </summary>
    bool IsVerbose { get; }
}
