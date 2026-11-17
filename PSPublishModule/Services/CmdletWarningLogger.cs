using System;
using System.Management.Automation;
using System.Runtime.ExceptionServices;
using System.Threading;
using PowerForge;

namespace PSPublishModule;

// PowerShell stream preferences must remain synchronous while progress and error output can be buffered.
// Warnings raised on the pipeline thread go through WriteWarning, so -WarningAction and $WarningPreference apply.
// A synchronous cmdlet cannot write from another thread (process output readers, timers), so those warnings go to
// the wrapped logger instead of throwing.
internal sealed class CmdletWarningLogger(PSCmdlet cmdlet, ILogger output) : ILogger
{
    private readonly int _pipelineThread = Thread.CurrentThread.ManagedThreadId;
    private ExceptionDispatchInfo? _warningStop;
    public bool IsVerbose => output.IsVerbose;
    public void Info(string message) => output.Info(message);
    public void Success(string message) => output.Success(message);
    public void Error(string message) => output.Error(message);
    public void Verbose(string message) => output.Verbose(message);
    public void Warn(string message)
    {
        if (cmdlet is not AsyncPSCmdlet && Thread.CurrentThread.ManagedThreadId != _pipelineThread)
        {
            output.Warn(message);
            return;
        }

        try
        {
            PowerForge.ConsoleUi.ConsoleOutput.WriteOutside(() => WriteWarning(message));
        }
        catch (Exception ex) when (ex is ActionPreferenceStopException or PipelineStoppedException)
        {
            _warningStop = ExceptionDispatchInfo.Capture(ex);
            throw;
        }
    }
    public void RethrowWarningStop() => _warningStop?.Throw();

    private void WriteWarning(string message)
    {
        if (cmdlet is AsyncPSCmdlet asyncCmdlet) asyncCmdlet.WriteWarning(message);
        else cmdlet.WriteWarning(message);
    }
}
