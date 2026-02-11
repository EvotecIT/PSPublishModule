using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PowerForge;

/// <summary>
/// Request to execute a PowerShell script out-of-process.
/// </summary>
public sealed class PowerShellRunRequest
{
    /// <summary>Path to the script to execute with <c>-File</c>.</summary>
    public string ScriptPath { get; }
    /// <summary>Arguments passed to the script (after <c>-File</c>).</summary>
    public IReadOnlyList<string> Arguments { get; }
    /// <summary>Maximum allowed execution time before killing the process.</summary>
    public TimeSpan Timeout { get; }
    /// <summary>When true, prefer <c>pwsh</c>; otherwise use Windows PowerShell first on Windows.</summary>
    public bool PreferPwsh { get; }
    /// <summary>
    /// Creates a new request.
    /// </summary>
    public PowerShellRunRequest(string scriptPath, IReadOnlyList<string> arguments, TimeSpan timeout, bool preferPwsh = true)
    { ScriptPath = scriptPath; Arguments = arguments; Timeout = timeout; PreferPwsh = preferPwsh; }
}
/// <summary>
/// Result of a PowerShell process execution.
/// </summary>
public sealed class PowerShellRunResult
{
    /// <summary>Exit code returned by the process.</summary>
    public int ExitCode { get; }
    /// <summary>Captured standard output.</summary>
    public string StdOut { get; }
    /// <summary>Captured standard error.</summary>
    public string StdErr { get; }
    /// <summary>Path to the executable used (pwsh or powershell).</summary>
    public string Executable { get; }
    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public PowerShellRunResult(int exitCode, string stdOut, string stdErr, string executable)
    { ExitCode = exitCode; StdOut = stdOut; StdErr = stdErr; Executable = executable; }
}
/// <summary>
/// Executes PowerShell scripts in a child process to avoid host conflicts.
/// </summary>
public interface IPowerShellRunner
{
    /// <summary>
    /// Runs the provided <paramref name="request"/> and returns the result.
    /// </summary>
    PowerShellRunResult Run(PowerShellRunRequest request);
}
/// <summary>
/// Default implementation that locates <c>pwsh</c> or <c>powershell.exe</c> on PATH and executes a script with <c>-File</c>.
/// </summary>
public sealed class PowerShellRunner : IPowerShellRunner
{
    /// <inheritdoc />
    public PowerShellRunResult Run(PowerShellRunRequest request)
    {
        var exe = ResolveExecutable(request.PreferPwsh);
        if (exe is null)
        {
            return new PowerShellRunResult(127, string.Empty, "No PowerShell executable found (pwsh or powershell.exe).", string.Empty);
        }

        var psi = new ProcessStartInfo();
        psi.FileName = exe;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        // Build classic argument string for net472
        var sb = new System.Text.StringBuilder();
        void AddArg(string s)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (string.IsNullOrEmpty(s))
            {
                sb.Append("\"\"");
                return;
            }
            if (s.IndexOf(' ') >= 0 || s.IndexOf('"') >= 0)
            {
                sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
            }
            else sb.Append(s);
        }
        AddArg("-NoProfile");
        AddArg("-NonInteractive");
        AddArg("-ExecutionPolicy");
        AddArg("Bypass");
        AddArg("-File");
        AddArg(request.ScriptPath);
        foreach (var arg in request.Arguments)
        {
            AddArg(arg);
        }
        psi.Arguments = sb.ToString();
#else
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(request.ScriptPath);
        foreach (var arg in request.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }
#endif

        using var p = new Process { StartInfo = psi };

        try { p.Start(); }
        catch (Exception ex)
        {
            return new PowerShellRunResult(127, string.Empty, ex.Message, exe);
        }

        // Read output asynchronously to avoid deadlocks when the child process writes a lot of data.
        // (Waiting for exit before draining stdout/stderr can cause the child process to block on full buffers.)
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();

        var timeoutMs = (int)Math.Max(1, Math.Min(int.MaxValue, request.Timeout.TotalMilliseconds));
        if (!p.WaitForExit(timeoutMs))
        {
            try
            {
#if NET472
                p.Kill();
#else
                p.Kill(entireProcessTree: true);
#endif
            }
            catch { /* ignore */ }

            try { p.WaitForExit(5000); } catch { /* ignore */ }

            var stdout = TryGetCompletedTaskResult(stdoutTask);
            var stderr = TryGetCompletedTaskResult(stderrTask);
            if (string.IsNullOrWhiteSpace(stderr)) stderr = "Timeout";

            return new PowerShellRunResult(124, stdout, stderr, exe);
        }

        // Ensure the async readers have completed after process exit.
        try { Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 5000); } catch { /* ignore */ }

        var finalStdout = TryGetCompletedTaskResult(stdoutTask);
        var finalStderr = TryGetCompletedTaskResult(stderrTask);
        return new PowerShellRunResult(p.ExitCode, finalStdout, finalStderr, exe);
    }

    private static string TryGetCompletedTaskResult(Task<string> task)
    {
        if (task is null) return string.Empty;

        try
        {
            if (task.Status == TaskStatus.RanToCompletion) return task.Result ?? string.Empty;
            if (task.Wait(10_000)) return task.Result ?? string.Empty;
        }
        catch { /* ignore */ }

        return string.Empty;
    }

    /// <summary>
    /// Resolves pwsh or Windows PowerShell on PATH depending on <paramref name="preferPwsh"/>.
    /// </summary>
    private static string? ResolveExecutable(bool preferPwsh)
    {
        var isWindows = Path.DirectorySeparatorChar == '\\';
        string[] candidates = preferPwsh
            ? (isWindows ? new[] { "pwsh.exe", "powershell.exe" } : new[] { "pwsh" })
            : (isWindows ? new[] { "powershell.exe", "pwsh.exe" } : new[] { "pwsh" });

        foreach (var name in candidates)
        {
            var full = ResolveOnPath(name);
            if (full is not null) return full;
        }
        return null;
    }

    /// <summary>
    /// Resolves <paramref name="fileName"/> using the PATH environment variable.
    /// </summary>
    private static string? ResolveOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignore */ }
        }
        return null;
    }
}
