namespace PowerForge;

/// <summary>
/// Minimal logging abstraction that maps cleanly to PowerShell Write-Verbose/Host styles.
/// Implementations should be fast, thread-safe for simple writes, and avoid logging secrets.
/// </summary>
public interface ILogger
{
    /// <summary>
    /// Logs an informational message (common console prefixes: <c>ℹ️</c> or <c>[i]</c>).
    /// </summary>
    /// <param name="message">Text to log.</param>
    void Info(string message);

    /// <summary>
    /// Logs a success message (common console prefixes: <c>✅</c> or <c>[+]</c>).
    /// </summary>
    /// <param name="message">Text to log.</param>
    void Success(string message);

    /// <summary>
    /// Logs a warning (common console prefixes: <c>⚠️</c> or <c>[-]</c>).
    /// </summary>
    /// <param name="message">Text to log.</param>
    void Warn(string message);

    /// <summary>
    /// Logs an error (common console prefixes: <c>❌</c> or <c>[e]</c>).
    /// </summary>
    /// <param name="message">Text to log.</param>
    void Error(string message);

    /// <summary>
    /// Logs a verbose/diagnostic message if <see cref="IsVerbose"/> is true (common console prefixes: <c>🔎</c> or <c>[v]</c>).
    /// </summary>
    /// <param name="message">Text to log.</param>
    void Verbose(string message);

    /// <summary>
    /// Indicates whether verbose/diagnostic messages should be emitted.
    /// </summary>
    bool IsVerbose { get; }
}
