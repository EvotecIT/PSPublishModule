using System;
using System.Management.Automation;
using System.Threading;
using ConsoleMonster.Text;
using PowerForge;
using PowerForge.ConsoleUi;

namespace PSPublishModule;

/// <summary>
/// Engine logger for cmdlets. Info and Success are shown the way Write-Host shows text (an information record
/// tagged PSHOST), so progress lines and heartbeats are visible without -Verbose and land in transcripts. Callers
/// can capture or discard them with the information stream (<c>6&gt;&amp;1</c>, <c>6&gt;$null</c>).
/// Warnings and errors use their own streams; Verbose stays verbose.
/// </summary>
/// <remarks>
/// <see cref="AsyncPSCmdlet"/> marshals writes from any thread. A plain <see cref="PSCmdlet"/> may only write on its
/// pipeline thread, so calls from other threads (heartbeats, process output callbacks) go to the console route instead.
/// </remarks>
internal sealed class CmdletLogger : ILogger
{
    private static readonly string[] HostTags = { "PSHOST" };

    private readonly PSCmdlet _cmdlet;
    private readonly bool _warningsAsVerbose;
    private readonly int _pipelineThread = Thread.CurrentThread.ManagedThreadId;

    public CmdletLogger(PSCmdlet cmdlet, bool isVerbose, bool warningsAsVerbose = false)
    {
        _cmdlet = cmdlet;
        _warningsAsVerbose = warningsAsVerbose;
        IsVerbose = isVerbose;
    }

    public bool IsVerbose { get; }

    private bool CanWrite => _cmdlet is AsyncPSCmdlet || Thread.CurrentThread.ManagedThreadId == _pipelineThread;

    public void Info(string message) => WriteHost(message, color: null, markup: null);

    public void Success(string message) => WriteHost(message, ConsoleColor.Green, "ok");

    public void Warn(string message)
    {
        if (_warningsAsVerbose)
        {
            WriteVerbose(message);
            return;
        }

        if (!CanWrite)
        {
            Fallback(message, "warn");
            return;
        }

        if (_cmdlet is AsyncPSCmdlet asyncCmdlet) asyncCmdlet.WriteWarning(message);
        else _cmdlet.WriteWarning(message);
    }

    public void Error(string message)
    {
        if (!CanWrite)
        {
            Fallback(message, "fail");
            return;
        }

        try
        {
            var record = new ErrorRecord(new InvalidOperationException(message), "PowerForgeError", ErrorCategory.NotSpecified, null);
            if (_cmdlet is AsyncPSCmdlet asyncCmdlet) asyncCmdlet.WriteError(record);
            else _cmdlet.WriteError(record);
        }
        catch (Exception ex) when (ex is not PipelineStoppedException and not ActionPreferenceStopException)
        {
            // Never throw from logging; keep the message visible.
            try { _cmdlet.WriteWarning(message); } catch { }
        }
    }

    public void Verbose(string message)
    {
        if (IsVerbose) WriteVerbose(message);
    }

    private void WriteHost(string message, ConsoleColor? color, string? markup)
    {
        if (!CanWrite)
        {
            Fallback(message, markup);
            return;
        }

        var data = new HostInformationMessage { Message = message ?? string.Empty };
        if (color is { } foreground) data.ForegroundColor = foreground;
        if (_cmdlet is AsyncPSCmdlet asyncCmdlet) asyncCmdlet.WriteInformation(data, HostTags);
        else _cmdlet.WriteInformation(data, HostTags);
    }

    private void WriteVerbose(string message)
    {
        if (_cmdlet is AsyncPSCmdlet asyncCmdlet) asyncCmdlet.WriteVerbose(message);
        else if (CanWrite) _cmdlet.WriteVerbose(message);
    }

    private static void Fallback(string message, string? style)
    {
        var text = Markup.Escape(message ?? string.Empty);
        try { ConsoleOutput.WriteMarkupLine(style is null ? text : $"[{style}]{text}[/]"); } catch { }
    }
}
