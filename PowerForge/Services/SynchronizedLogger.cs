using System;

namespace PowerForge;

internal sealed class SynchronizedLogger : ILogger
{
    private readonly ILogger _inner;
    private readonly object _sync;

    internal SynchronizedLogger(ILogger inner, object sync)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
    }

    public bool IsVerbose => _inner.IsVerbose;

    public void Info(string message) => Write(static (logger, value) => logger.Info(value), message);

    public void Success(string message) => Write(static (logger, value) => logger.Success(value), message);

    public void Warn(string message) => Write(static (logger, value) => logger.Warn(value), message);

    public void Error(string message) => Write(static (logger, value) => logger.Error(value), message);

    public void Verbose(string message) => Write(static (logger, value) => logger.Verbose(value), message);

    private void Write(Action<ILogger, string> write, string message)
    {
        lock (_sync)
            write(_inner, message);
    }
}
