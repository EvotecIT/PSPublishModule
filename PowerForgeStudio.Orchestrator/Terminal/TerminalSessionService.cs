namespace PowerForgeStudio.Orchestrator.Terminal;

public sealed class TerminalSessionService : IAsyncDisposable
{
    private readonly List<ITerminalSession> _activeSessions = [];
    private readonly object _lock = new();

    public ITerminalSession CreateSession(string workingDirectory, int cols = 120, int rows = 30)
    {
        var shell = ResolveShell();
        var session = new ConPtySession(shell, workingDirectory, cols, rows);

        lock (_lock)
        {
            _activeSessions.Add(session);
        }

        session.Exited += _ =>
        {
            lock (_lock)
            {
                _activeSessions.Remove(session);
            }
        };

        return session;
    }

    public async ValueTask DisposeAsync()
    {
        List<ITerminalSession> sessions;
        lock (_lock)
        {
            sessions = [.. _activeSessions];
            _activeSessions.Clear();
        }

        foreach (var session in sessions)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string ResolveShell()
    {
        // Prefer pwsh (PowerShell 7+), fall back to powershell, then cmd
        var candidates = new[] { "pwsh.exe", "powershell.exe", "cmd.exe" };
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var candidate in candidates)
        {
            foreach (var dir in pathDirs)
            {
                var fullPath = Path.Combine(dir, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return "cmd.exe";
    }
}
