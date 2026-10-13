using System.Diagnostics;

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
        using var registration = token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try
        {
            if (input != null) await input(process.StandardInput.BaseStream);
            process.StandardInput.Close();
            await process.WaitForExitAsync(token);
            await error;
            var text = await output;
            if (process.ExitCode != 0) throw new IOException("Backup tool failed with exit " + process.ExitCode + ".");
            if (text.Length > 1024 * 1024) throw new IOException("Unexpected backup tool output size.");
            return text;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }

    internal static string[] SshArguments(SshDestination destination, string command) =>
    ["-C", "-i", destination.IdentityFile, "-p", destination.Port.ToString(), "-o", "IdentitiesOnly=yes", "-o", "BatchMode=yes",
        "-o", "StrictHostKeyChecking=yes", "-o", "UserKnownHostsFile=" + destination.KnownHostsFile,
        "-o", "ConnectTimeout=15", "-o", "ServerAliveInterval=30", "-o", "ServerAliveCountMax=3",
        destination.User + "@" + destination.Host, command];
}
