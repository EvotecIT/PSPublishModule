using PowerForge;

namespace PowerForge.ConsoleUi;

/// <summary>
/// The human-facing logger of the CLI and the module. While a <see cref="ProgressView"/> runs, messages go to its
/// log and are printed in full when it ends; otherwise each message is written at once through
/// <see cref="ConsoleOutput"/>.
/// </summary>
internal class ConsoleOutputLogger : ILogger
{
    public bool IsVerbose { get; set; }

    public void Info(string message) => Write("info", message);
    public void Success(string message) => Write("pass", message);
    public void Warn(string message) => Write("warn", message);
    public void Error(string message) => Write("err", message);

    public void Verbose(string message)
    {
        if (!IsVerbose) return;
        Write("verbose", message);
    }

    private static void Write(string level, string message)
    {
        if (ProgressView.Current?.TryMessage(level, message ?? string.Empty) == true) return;
        ConsoleOutput.Write(new MessageLine(level, message ?? string.Empty));
    }
}
