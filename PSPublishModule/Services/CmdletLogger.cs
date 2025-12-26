using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal sealed class CmdletLogger : ILogger
{
    private readonly PSCmdlet _cmdlet;
    private readonly bool _warningsAsVerbose;

    public CmdletLogger(PSCmdlet cmdlet, bool isVerbose, bool warningsAsVerbose = false)
    {
        _cmdlet = cmdlet;
        _warningsAsVerbose = warningsAsVerbose;
        IsVerbose = isVerbose;
    }

    public void Info(string message) => _cmdlet.WriteVerbose(message);
    public void Success(string message) => _cmdlet.WriteVerbose(message);
    public void Warn(string message)
    {
        if (_warningsAsVerbose) _cmdlet.WriteVerbose(message);
        else _cmdlet.WriteWarning(message);
    }
    public void Error(string message) => _cmdlet.WriteVerbose(message);
    public void Verbose(string message)
    {
        if (!IsVerbose) return;
        _cmdlet.WriteVerbose(message);
    }

    public bool IsVerbose { get; }
}
