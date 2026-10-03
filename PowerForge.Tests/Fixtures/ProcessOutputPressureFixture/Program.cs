using System.Diagnostics;
using System.Text.Json;
using System.Reflection;
using PowerForge;

// A shell writer isolates pipe capture from nested .NET startup and trusted SDK
// discovery. Those operations are not the reader's worker-pool contract.
var script = "sleep 0.2; " + (args[0] == "inherited" ? "sleep 10 & printf '%s\\n' \"$!\"; " : "")
    + "printf 'parent-output😀'; printf 'parent-error漢字' >&2";
var run = typeof(DotNetPublishPipelineRunner).GetMethod("RunBuildInputEvaluationProcess", BindingFlags.NonPublic | BindingFlags.Static)!;

// Starve only this isolated fixture's pool, never the xUnit host or other tests.
ThreadPool.GetMinThreads(out _, out var minimumIo);
ThreadPool.GetMaxThreads(out _, out var maximumIo);
if (!ThreadPool.SetMinThreads(1, minimumIo) || !ThreadPool.SetMaxThreads(1, maximumIo)) return 2;
using var entered = new ManualResetEventSlim();
using var release = new ManualResetEventSlim();
var pressure = Task.Run(() => { entered.Set(); release.Wait(); });
if (!entered.Wait(TimeSpan.FromSeconds(5))) return 3;
var unblock = new Thread(() => { release.Wait(TimeSpan.FromSeconds(5)); release.Set(); });
unblock.Start();
try
{
    var result = ((int ExitCode, string StdOut, string StdErr, bool TimedOut))run.Invoke(null,
        new object?[] { "/bin/sh", Environment.CurrentDirectory, new[] { "-c", script }, null, TimeSpan.FromSeconds(5), null, null })!;
    Console.WriteLine(JsonSerializer.Serialize(new { result.ExitCode, result.StdOut, result.StdErr, result.TimedOut,
        CompletedWhilePoolBlocked = !release.IsSet }));
    if (args[0] == "inherited" && int.TryParse(result.StdOut.Split('\n')[0].Trim(), out var id))
    {
        try { using var descendant = Process.GetProcessById(id); descendant.Kill(); }
        catch (ArgumentException) { }
    }
    return 0;
}
finally { release.Set(); unblock.Join(); pressure.GetAwaiter().GetResult(); }
