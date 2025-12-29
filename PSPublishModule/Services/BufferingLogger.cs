using System;
using System.Collections.Generic;
using PowerForge;

namespace PSPublishModule;

internal sealed class BufferingLogger : ILogger
{
    public bool IsVerbose { get; set; }

    public List<BufferingLogEntry> Entries { get; } = new();

    public void Info(string message) => Entries.Add(new BufferingLogEntry("info", message));
    public void Success(string message) => Entries.Add(new BufferingLogEntry("success", message));
    public void Warn(string message) => Entries.Add(new BufferingLogEntry("warn", message));
    public void Error(string message) => Entries.Add(new BufferingLogEntry("error", message));

    public void Verbose(string message)
    {
        if (!IsVerbose) return;
        Entries.Add(new BufferingLogEntry("verbose", message));
    }
}

internal sealed class BufferingLogEntry
{
    public string Level { get; }
    public string Message { get; }

    public BufferingLogEntry(string level, string message)
    {
        Level = level ?? "info";
        Message = message ?? string.Empty;
    }
}

