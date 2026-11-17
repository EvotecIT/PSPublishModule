using System;
using System.Management.Automation;
using System.Threading;
using ConsoleMonster.Layout;
using ConsoleMonster.PowerShell.Display;
using PowerForge.ConsoleUi;

namespace PSPublishModule;

/// <summary>
/// Sends the module's console output through the calling cmdlet's PowerShell host, so transcripts, the ISE,
/// jobs and remoting receive it. Install it around cmdlet code that renders: <c>using (CmdletConsole.Use(this))</c>.
/// </summary>
internal static class CmdletConsole
{
    public static IDisposable Use(PSCmdlet cmdlet) => ConsoleOutput.Use(new Host(cmdlet));

    // Host writes are only valid on the pipeline thread. Writes from engine worker threads keep going straight
    // to the console, as they always have.
    private sealed class Host(PSCmdlet cmdlet) : IConsoleHost
    {
        private readonly int _pipelineThread = Thread.CurrentThread.ManagedThreadId;

        private bool OnPipelineThread => Thread.CurrentThread.ManagedThreadId == _pipelineThread;

        public void Write(IWidget widget)
        {
            if (OnPipelineThread) HostOutput.Write(cmdlet, widget);
            else ConsoleOutput.TerminalHost.Instance.Write(widget);
        }

        public void WriteMarkup(string markup, bool newLine)
        {
            if (OnPipelineThread) HostOutput.WriteMarkup(cmdlet, markup, newLine);
            else ConsoleOutput.TerminalHost.Instance.WriteMarkup(markup, newLine);
        }

        public void WriteOutside(Action write)
        {
            if (OnPipelineThread) HostOutput.Run(cmdlet, write);
            else ConsoleOutput.TerminalHost.Instance.WriteOutside(write);
        }

        public bool CanRenderLive => HostOutput.CanRenderLive(cmdlet);
    }
}
