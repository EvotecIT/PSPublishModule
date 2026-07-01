namespace PowerForge;

internal sealed class WindowsProcessResult
{
    internal WindowsProcessResult(int exitCode, string stdout, string stderr)
    {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
    }

    internal int ExitCode { get; }

    internal string Stdout { get; }

    internal string Stderr { get; }
}
