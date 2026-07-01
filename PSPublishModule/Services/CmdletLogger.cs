using System;
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

    public void Info(string message) => WriteVerbose(message);
    public void Success(string message) => WriteVerbose(message);
    public void Warn(string message)
    {
        if (_warningsAsVerbose) WriteVerbose(message);
        else WriteWarning(message);
    }
    public void Error(string message)
    {
        try
        {
            WriteError(new ErrorRecord(
                exception: new InvalidOperationException(message),
                errorId: "PowerForgeError",
                errorCategory: ErrorCategory.NotSpecified,
                targetObject: null));
        }
        catch
        {
            // Best effort fallbacks; never throw from logging.
            try { WriteWarning(message); } catch { }
        }
    }
    public void Verbose(string message)
    {
        if (!IsVerbose) return;
        WriteVerbose(message);
    }

    public bool IsVerbose { get; }

    private void WriteVerbose(string message)
    {
        if (_cmdlet is AsyncPSCmdlet asyncCmdlet)
            asyncCmdlet.WriteVerbose(message);
        else
            _cmdlet.WriteVerbose(message);
    }

    private void WriteWarning(string message)
    {
        if (_cmdlet is AsyncPSCmdlet asyncCmdlet)
            asyncCmdlet.WriteWarning(message);
        else
            _cmdlet.WriteWarning(message);
    }

    private void WriteError(ErrorRecord errorRecord)
    {
        if (_cmdlet is AsyncPSCmdlet asyncCmdlet)
            asyncCmdlet.WriteError(errorRecord);
        else
            _cmdlet.WriteError(errorRecord);
    }
}
