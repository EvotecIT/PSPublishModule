using System.Management.Automation;
using System.Threading;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class CmdletWarningLoggerTests
{
    [Fact]
    public void Warn_FromAWorkerThreadOfASynchronousCmdlet_GoesToTheWrappedLoggerInsteadOfThrowing()
    {
        var output = new BufferedLogger();
        var logger = new CmdletWarningLogger(new SynchronousCommand(), output);
        Exception? failure = null;

        // Process output readers and heartbeat timers warn from threads PowerShell does not allow to write.
        var worker = new Thread(() =>
        {
            try { logger.Warn("PSSA: Formatter failed (exit 1)."); }
            catch (Exception ex) { failure = ex; }
        });
        worker.Start();
        worker.Join();

        Assert.Null(failure);
        var entry = Assert.Single(output.Entries);
        Assert.Equal("warn", entry.Level);
        Assert.Equal("PSSA: Formatter failed (exit 1).", entry.Message);
    }

    private sealed class SynchronousCommand : PSCmdlet
    {
    }
}
