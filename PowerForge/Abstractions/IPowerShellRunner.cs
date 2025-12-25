using System.Diagnostics;

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

        using var p = new Process { StartInfo = psi };
        p.Start();
        if (!p.WaitForExit((int)request.Timeout.TotalMilliseconds))
        {
            try { p.Kill(); } catch { /* ignore */ }
            return new PowerShellRunResult(124, p.StandardOutput.ReadToEnd(), "Timeout", exe);
        }
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        return new PowerShellRunResult(p.ExitCode, stdout, stderr, exe);
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
