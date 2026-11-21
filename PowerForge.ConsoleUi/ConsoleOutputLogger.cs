namespace PowerForge.ConsoleUi;

/// <summary>Routes terminal messages through the current progress view while it is active.</summary>
internal sealed class ConsoleOutputLogger : TerminalOutputLogger
{
    protected override void Write(string level, string message)
    {
        if (ProgressView.Current?.TryMessage(level, message ?? string.Empty) != true)
            base.Write(level, message ?? string.Empty);
    }
}
