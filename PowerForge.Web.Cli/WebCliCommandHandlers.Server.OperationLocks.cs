using System.Diagnostics;
using System.Text;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    internal static string BuildRemoteOperationLockCommand(IEnumerable<string> paths)
    {
        var locks = GetRemoteOperationLocks(paths);
        if (locks.Length == 0)
            throw new InvalidOperationException("At least one operation lock is required.");

        var builder = new StringBuilder("set -e; ");
        foreach (var path in locks)
            builder.Append("flock -n ").Append(ShellQuote(path)).Append(' ');
        builder.Append("sh -c ").Append(ShellQuote("printf 'POWERFORGE_OPERATION_LOCKED\\n'; cat >/dev/null"));
        return builder.ToString();
    }

    private static IDisposable? AcquireRemoteOperationLocks(
        string sshCommand,
        string target,
        IEnumerable<string> paths)
    {
        var locks = GetRemoteOperationLocks(paths);
        if (locks.Length == 0)
            return null;

        var process = CreateProcess(sshCommand, BuildSshArguments(target, BuildRemoteOperationLockCommand(locks)));
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        if (!process.Start())
            throw new InvalidOperationException("Failed to start the remote operation lock session.");

        var markerTask = process.StandardOutput.ReadLineAsync();
        bool markerReady;
        try
        {
            markerReady = markerTask.Wait(TimeSpan.FromSeconds(30));
        }
        catch (Exception exception)
        {
            var stderr = StopFailedOperationLockProcess(process);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? "Failed while acquiring the remote operation lock."
                    : $"Failed while acquiring the remote operation lock: {stderr}",
                exception);
        }
        if (!markerReady)
        {
            StopFailedOperationLockProcess(process);
            throw new InvalidOperationException("Timed out while acquiring the remote operation lock.");
        }
        if (!string.Equals(markerTask.Result, "POWERFORGE_OPERATION_LOCKED", StringComparison.Ordinal))
        {
            var stderr = StopFailedOperationLockProcess(process);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? "Another host operation holds a recovery operation lock."
                : $"Failed to acquire the remote operation lock: {stderr}");
        }

        return new RemoteOperationLock(process);
    }

    private static string StopFailedOperationLockProcess(Process process)
    {
        try
        {
            try { process.StandardInput.Close(); } catch (InvalidOperationException) { }
            if (!process.HasExited && !process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            return process.StandardError.ReadToEnd().Trim();
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string[] GetRemoteOperationLocks(IEnumerable<string> paths)
    {
        var locks = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (var path in locks)
        {
            const string prefix = "/var/lock/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal) ||
                !path.EndsWith(".lock", StringComparison.Ordinal) ||
                path[prefix.Length..].Contains('/', StringComparison.Ordinal) ||
                path[prefix.Length..].Length > 131 ||
                path.Any(static character => !(IsAsciiLetterOrDigit(character) || character is '/' or '.' or '_' or '-')))
            {
                throw new InvalidOperationException($"Operation lock contains unsupported characters or location: {path}");
            }
        }
        return locks;
    }

    private sealed class RemoteOperationLock : IDisposable
    {
        private readonly Process _process;
        private bool _disposed;

        internal RemoteOperationLock(Process process) => _process = process;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(10000))
                    _process.Kill(entireProcessTree: true);
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
