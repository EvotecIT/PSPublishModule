using System.Diagnostics;
using System.Text;

namespace PowerForge.Backups.Tests;

public sealed partial class DatabaseBackupContracts
{
    [Fact]
    public async Task FailedInputStopsTheChildWithoutWaitingForItsEofBehavior()
    {
        var pidFile = Path.Combine(_root, "input-failure-tool.pid");
        var command = ChildCommand(pidFile,
            "[Console]::In.ReadToEnd()|Out-Null;Start-Sleep -Seconds 60;",
            "cat >/dev/null; sleep 60");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<IOException>(() => ProcessRunner.RunAsync(command.Executable, command.Arguments,
            async input =>
            {
                while (!File.Exists(pidFile) || new FileInfo(pidFile).Length == 0) await Task.Delay(10, timeout.Token);
                throw new IOException("Synthetic source read failure");
            }, timeout.Token));
        Assert.False(timeout.IsCancellationRequested);
        AssertChildExited(pidFile);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExcessiveToolOutputStopsTheChildEvenWhileInputIsBlocked(bool standardError)
    {
        var pidFile = Path.Combine(_root, "tool.pid");
        var block = new string('x', 1024);
        var command = ChildCommand(pidFile,
            $"while($true){{[Console]::{(standardError ? "Error" : "Out")}.Write('{block}');}}",
            $"while :; do printf '%s' '{block}'{(standardError ? " >&2" : "")}; done");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await Assert.ThrowsAsync<IOException>(() => ProcessRunner.RunAsync(command.Executable, command.Arguments,
            async input =>
            {
                var buffer = new byte[8192];
                while (true) await input.WriteAsync(buffer, timeout.Token);
            }, timeout.Token));
        Assert.False(timeout.IsCancellationRequested);
        AssertChildExited(pidFile);
    }

    [Fact]
    public async Task NormalToolReceivesInputAndReturnsOnlyItsStandardOutput()
    {
        var pidFile = Path.Combine(_root, "normal-tool.pid");
        var command = ChildCommand(pidFile,
            "[Console]::Out.Write([Console]::In.ReadToEnd().Trim());[Console]::Error.Write('synthetic diagnostic');",
            "read -r line; printf '%s' \"$line\"; printf '%s' 'synthetic diagnostic' >&2");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await ProcessRunner.RunAsync(command.Executable, command.Arguments,
            async input => await input.WriteAsync(Encoding.UTF8.GetBytes("receipt\n"), timeout.Token), timeout.Token);
        Assert.Equal("receipt", result);
        AssertChildExited(pidFile);
    }

    private static (string Executable, string[] Arguments) ChildCommand(string pidFile, string windows, string posix)
    {
        if (OperatingSystem.IsWindows())
            return (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32/WindowsPowerShell/v1.0/powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-Command", $"[IO.File]::WriteAllText('{pidFile.Replace("'", "''")}',[string]$PID);" + windows]);
        return ("/bin/sh", ["-c", "printf '%s' $$ > '" + pidFile.Replace("'", "'\"'\"'") + "';" + posix]);
    }

    private static void AssertChildExited(string pidFile)
    {
        var id = int.Parse(File.ReadAllText(pidFile), System.Globalization.CultureInfo.InvariantCulture);
        Process? process;
        try { process = Process.GetProcessById(id); }
        catch (ArgumentException) { return; }
        using (process) Assert.True(process.HasExited, "The synthetic backup tool must not remain running.");
    }
}
