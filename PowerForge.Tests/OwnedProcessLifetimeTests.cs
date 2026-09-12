using System.Diagnostics;
using System.Text;

namespace PowerForge.Tests;

public sealed class OwnedProcessLifetimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.OwnedProcess.Tests", Guid.NewGuid().ToString("N"));
    public OwnedProcessLifetimeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Successful_process_without_descendants_returns_its_output()
    {
        var result = await new ProcessRunner(ownProcessTree: true)
            .RunAsync(ShellRequest("echo complete", TimeSpan.FromSeconds(10)));
        Assert.True(result.Succeeded, result.StdErr);
        Assert.Equal("complete", result.StdOut.Trim());
    }

    [Theory]
    [InlineData(false, "utf-32BE")]
    [InlineData(true, "utf-16")]
    public async Task Bom_encoded_output_is_decoded_on_both_streams(bool ownProcessTree, string encodingName)
    {
        const string expected = "zażółć😀漢字";
        var encoding = Encoding.GetEncoding(encodingName);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray();
        var script = OperatingSystem.IsWindows()
            ? "$bytes = [Convert]::FromBase64String('" + Convert.ToBase64String(bytes) + "'); [Console]::OpenStandardOutput().Write($bytes, 0, $bytes.Length); [Console]::OpenStandardError().Write($bytes, 0, $bytes.Length)"
            : "printf '" + string.Concat(bytes.Select(value => "\\" + Convert.ToString(value, 8).PadLeft(3, '0'))) + "'; printf '" + string.Concat(bytes.Select(value => "\\" + Convert.ToString(value, 8).PadLeft(3, '0'))) + "' >&2";

        var result = await new ProcessRunner(ownProcessTree).RunAsync(ShellRequest(script, TimeSpan.FromSeconds(10)));

        Assert.True(result.Succeeded, result.StdErr);
        Assert.Equal(expected, result.StdOut);
        Assert.Equal(expected, result.StdErr);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Timeout_and_cancellation_terminate_descendants_before_and_after_parent_exit(bool parentExits, bool cancel)
    {
        var pidFile = Path.Combine(_root, "child.pid");
        using var cancellation = new CancellationTokenSource();
        var parentExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = ChildRequest(pidFile, parentExits, capture: true, cancel ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5));
        request.SetCompletionBoundary(_ => parentExit.TrySetResult());
        var watch = Stopwatch.StartNew();
        try
        {
            var run = new ProcessRunner(ownProcessTree: true).RunAsync(request, cancellation.Token);
            await WaitForPidAsync(pidFile);
            if (parentExits) await parentExit.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Assert.False(run.IsCompleted, "The live child must retain the redirected pipe until the lifetime boundary.");
            if (cancel) cancellation.Cancel();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(!cancel, result.TimedOut);
            Assert.False(result.Succeeded);
            Assert.Equal(cancel ? 130 : 124, result.ExitCode);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(12), $"Owned execution took {watch.Elapsed}.");
            await AssertChildStoppedAsync(pidFile);
        }
        finally { await CleanupChildAsync(pidFile); }
    }

    [Fact]
    public async Task Normal_return_without_captured_pipes_terminates_remaining_descendants()
    {
        var pidFile = Path.Combine(_root, "child.pid");
        try
        {
            var result = await new ProcessRunner(ownProcessTree: true)
                .RunAsync(ChildRequest(pidFile, parentExits: true, capture: false, TimeSpan.FromSeconds(10)))
                .WaitAsync(TimeSpan.FromSeconds(15));

            Assert.True(result.Succeeded, result.StdErr);
            Assert.True(File.Exists(pidFile));
            await AssertChildStoppedAsync(pidFile);
        }
        finally { await CleanupChildAsync(pidFile); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_preserves_unterminated_unicode_output_on_both_streams(bool ownProcessTree)
    {
        var script = OperatingSystem.IsWindows()
            ? "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); [Console]::Out.Write('zażółć😀'); [Console]::Error.Write('błąd漢字'); Start-Sleep -Seconds 60"
            : "printf 'zażółć😀'; printf 'błąd漢字' >&2; exec sleep 60";
        var request = ShellRequest(script, TimeSpan.FromSeconds(5));

        var result = await new ProcessRunner(ownProcessTree).RunAsync(request).WaitAsync(TimeSpan.FromSeconds(12));

        Assert.True(result.TimedOut);
        Assert.Equal("zażółć😀", result.StdOut);
        Assert.Contains("błąd漢字", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("�", result.StdErr, StringComparison.Ordinal);
    }

    private ProcessRunRequest ChildRequest(string pidFile, bool parentExits, bool capture, TimeSpan timeout)
    {
        var script = OperatingSystem.IsWindows()
            ? "$child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 60' -NoNewWindow -PassThru; [IO.File]::WriteAllText($env:CHILD_PID_FILE, [string]$child.Id); " + (parentExits ? "exit 0" : "Start-Sleep -Seconds 60")
            : "sleep 60 & echo $! > \"$CHILD_PID_FILE\"; " + (parentExits ? "exit 0" : "wait");
        return ShellRequest(script, timeout, capture, new() { ["CHILD_PID_FILE"] = pidFile });
    }

    private ProcessRunRequest ShellRequest(string script, TimeSpan timeout, bool capture = true, Dictionary<string, string?>? environment = null)
    {
        var windows = OperatingSystem.IsWindows();
        var executable = windows
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
            : "/bin/sh";
        string[] arguments = windows
            ? ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]
            : ["-c", script];
        return new(executable, _root, arguments, timeout, environment, captureOutput: capture, captureError: capture);
    }

    private static async Task WaitForPidAsync(string pidFile)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(4))
        {
            if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out _)) return;
            await Task.Delay(25);
        }
        Assert.Fail("The fixture did not report its descendant PID before the deadline.");
    }

    internal static async Task AssertChildStoppedAsync(string pidFile)
    {
        Assert.True(File.Exists(pidFile), "The fixture must prove a child was actually started.");
        var pid = int.Parse(File.ReadAllText(pidFile).Trim());
        var watch = Stopwatch.StartNew();
        while (ChildIsRunning(pid) && watch.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(25);
        Assert.False(ChildIsRunning(pid), $"Descendant {pid} is still running after the owned process completed.");
    }

    private static bool ChildIsRunning(int pid)
    {
        try
        {
            using var child = Process.GetProcessById(pid);
            if (child.HasExited) return false;
            if (OperatingSystem.IsLinux())
            {
                // An orphan zombie is already dead; reaping belongs to the host's init process.
                var stat = File.ReadAllText($"/proc/{pid}/stat");
                if (stat[(stat.LastIndexOf(')') + 2)..].StartsWith("Z ", StringComparison.Ordinal)) return false;
            }
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static async Task CleanupChildAsync(string pidFile)
    {
        if (!File.Exists(pidFile) || !int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid) || !ChildIsRunning(pid)) return;
        try
        {
            using var child = Process.GetProcessById(pid);
            if (child.ProcessName is "sleep" or "powershell")
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            }
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        // Windows can briefly retain the child's current-directory handle after
        // its process handle is signaled. Do not confuse this with a live child.
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try { Directory.Delete(_root, recursive: true); break; }
            catch (IOException) when (watch.Elapsed < TimeSpan.FromSeconds(3)) { Thread.Sleep(25); }
        }
    }
}
