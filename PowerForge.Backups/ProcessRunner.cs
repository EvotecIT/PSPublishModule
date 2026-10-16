using System.Diagnostics;
using System.Text;

namespace PowerForge.Backups;

internal static class ProcessRunner
{
    internal static Process Start(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new IOException("Could not start backup tool.");
    }

    internal static async Task<string> RunAsync(string executable, IEnumerable<string> arguments,
        Func<Stream, Task>? input, CancellationToken token)
    {
        using var process = Start(executable, arguments);
        using var registration = token.Register(() => Stop(process));
        var output = ReadBoundedAsync(process.StandardOutput, process, true, token);
        var error = ReadBoundedAsync(process.StandardError, process, false, token);
        async Task SendInputAsync()
        {
            try { if (input != null) await input(process.StandardInput.BaseStream); }
            catch { Stop(process); throw; }
            finally { process.StandardInput.Close(); }
        }
        try
        {
            await Task.WhenAll(output, error, SendInputAsync(), process.WaitForExitAsync(token));
            if (process.ExitCode != 0) throw new IOException("Backup tool failed with exit " + process.ExitCode + ".");
            return await output;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, Process process, bool capture, CancellationToken token)
    {
        var buffer = new char[4096];
        var text = capture ? new StringBuilder() : null;
        var count = 0;
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
            {
                count += read;
                if (count > 1024 * 1024) throw new IOException("Unexpected backup tool output size.");
                text?.Append(buffer, 0, read);
            }
            return text?.ToString() ?? "";
        }
        catch { Stop(process); throw; }
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (InvalidOperationException) { }
    }

    internal static string[] SshArguments(SshDestination destination, string command) =>
    ["-C", "-i", destination.IdentityFile, "-p", destination.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "-o", "IdentitiesOnly=yes", "-o", "BatchMode=yes",
        "-o", "StrictHostKeyChecking=yes", "-o", "UserKnownHostsFile=" + destination.KnownHostsFile,
        "-o", "ConnectTimeout=15", "-o", "ServerAliveInterval=30", "-o", "ServerAliveCountMax=3",
        destination.User + "@" + destination.Host, command];
}
