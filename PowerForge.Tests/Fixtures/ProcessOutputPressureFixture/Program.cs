using System.Diagnostics;
using System.Text.Json;
using System.Reflection;
using PowerForge;

var fixture = typeof(Program).Assembly.Location;
if (args[0] == "hold") { Thread.Sleep(10000); return 0; }
if (args[0] == "write")
{
    Thread.Sleep(200); // Make the first pipe read pending before producing output.
    if (args[1] == "inherited")
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        start.ArgumentList.Add(fixture);
        start.ArgumentList.Add("hold");
        using var descendant = Process.Start(start)!;
        Console.WriteLine(descendant.Id);
    }
    Console.Write("parent-output😀");
    Console.Error.Write("parent-error漢字");
    return 0;
}

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
    var run = typeof(DotNetPublishPipelineRunner).GetMethod("RunBuildInputEvaluationProcess", BindingFlags.NonPublic | BindingFlags.Static)!;
    var result = ((int ExitCode, string StdOut, string StdErr, bool TimedOut))run.Invoke(null,
        new object?[] { "dotnet", Environment.CurrentDirectory, new[] { fixture, "write", args[0] }, null, TimeSpan.FromSeconds(5), null, null })!;
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
