namespace PowerForge;

/// <summary>
/// Simple console logger that prefixes messages with PSPublishModule-style markers.
/// </summary>
public sealed class ConsoleLogger : ILogger
{
    /// <inheritdoc />
    public bool IsVerbose { get; set; }
    /// <inheritdoc />
    public void Info(string message) => Write("[i] ", message);
    /// <inheritdoc />
    public void Success(string message) => Write("[+] ", message);
    /// <inheritdoc />
    public void Warn(string message) => Write("[-] ", message);
    /// <inheritdoc />
    public void Error(string message) => Write("[e] ", message);
    /// <inheritdoc />
    public void Verbose(string message) { if (IsVerbose) Write("[v] ", message); }

    private static void Write(string prefix, string message)
    {
        Console.WriteLine(prefix + message);
    }
}
