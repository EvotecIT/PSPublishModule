using PowerForge;

namespace PowerForge.ConsoleUi;

internal class TerminalOutputLogger : ILogger
{
    public bool IsVerbose { get; set; }

    public void Info(string message) => Write("info", message);
    public void Success(string message) => Write("pass", message);
    public void Warn(string message) => Write("warn", message);
    public void Error(string message) => Write("err", message);

    public void Verbose(string message)
    {
        if (IsVerbose) Write("verbose", message);
    }

    protected virtual void Write(string level, string message)
        => ConsoleOutput.Write(new MessageLine(level, message ?? string.Empty));
}
