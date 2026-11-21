namespace PowerForge;

/// <summary>
/// Logger implementation that discards all messages.
/// </summary>
public sealed class NullLogger : ILogger
{
    /// <inheritdoc />
    public bool IsVerbose { get; set; }

    /// <inheritdoc />
    public void Info(string message) { }

    /// <inheritdoc />
    public void Success(string message) { }

    /// <inheritdoc />
    public void Warn(string message) { }

    /// <inheritdoc />
    public void Error(string message) { }

    /// <inheritdoc />
    public void Verbose(string message) { }
}

