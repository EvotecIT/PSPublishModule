using System;

namespace PowerForge;

internal sealed class PrivateGalleryHostLogger : ILogger
{
    private readonly IPrivateGalleryHost _host;

    public PrivateGalleryHostLogger(IPrivateGalleryHost host)
        => _host = host ?? throw new ArgumentNullException(nameof(host));

    public bool IsVerbose => true;

    public void Verbose(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _host.WriteVerbose(message);
    }

    public void Info(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _host.WriteVerbose(message);
    }

    public void Warn(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _host.WriteWarning(message);
    }

    public void Error(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _host.WriteWarning(message);
    }

    public void Success(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _host.WriteVerbose(message);
    }
}
