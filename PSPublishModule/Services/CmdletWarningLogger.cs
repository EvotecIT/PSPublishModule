using System;
using System.Management.Automation;
using System.Runtime.ExceptionServices;
using PowerForge;

namespace PSPublishModule;

// PowerShell stream preferences must remain synchronous while progress and error output can be buffered.
internal sealed class CmdletWarningLogger(PSCmdlet cmdlet, ILogger output) : ILogger
{
    private ExceptionDispatchInfo? _warningStop;
    public bool IsVerbose => output.IsVerbose;
    public void Info(string message) => output.Info(message);
    public void Success(string message) => output.Success(message);
    public void Error(string message) => output.Error(message);
    public void Verbose(string message) => output.Verbose(message);
    public void Warn(string message)
    {
        try { cmdlet.WriteWarning(message); }
        catch (Exception ex) when (ex is ActionPreferenceStopException or PipelineStoppedException)
        {
            _warningStop = ExceptionDispatchInfo.Capture(ex);
            throw;
        }
    }
    public void RethrowWarningStop() => _warningStop?.Throw();
}
