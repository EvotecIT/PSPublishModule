using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PowerForge;

/// <summary>
/// Supported PowerShell invocation modes.
/// </summary>
public enum PowerShellInvocationMode
{
    /// <summary>
    /// Executes a script path via <c>-File</c>.
    /// </summary>
    File = 0,

    /// <summary>
    /// Executes inline PowerShell text via <c>-Command</c>.
    /// </summary>
    Command = 1
}

/// <summary>
/// Request to execute a PowerShell script out-of-process.
/// </summary>
public sealed class PowerShellRunRequest
{
    /// <summary>Path to the script to execute with <c>-File</c>.</summary>
    public string? ScriptPath { get; }
    /// <summary>Inline PowerShell text to execute with <c>-Command</c>.</summary>
    public string? CommandText { get; }
    /// <summary>Arguments passed to the script (after <c>-File</c>).</summary>
    public IReadOnlyList<string> Arguments { get; }
    /// <summary>Maximum allowed execution time before killing the process.</summary>
    public TimeSpan Timeout { get; }
    /// <summary>When true, prefer <c>pwsh</c>; otherwise use Windows PowerShell first on Windows.</summary>
    public bool PreferPwsh { get; }
    /// <summary>Optional working directory for the PowerShell process.</summary>
    public string? WorkingDirectory { get; }
    /// <summary>Optional environment variable overrides for the PowerShell process.</summary>
    public IReadOnlyDictionary<string, string?>? EnvironmentVariables { get; }
    /// <summary>Optional explicit executable name or path.</summary>
    public string? ExecutableOverride { get; }
    /// <summary>Gets the invocation mode for the request.</summary>
    public PowerShellInvocationMode InvocationMode { get; }
    /// <summary>
    /// Creates a new file-based request.
    /// </summary>
    public PowerShellRunRequest(
        string scriptPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        bool preferPwsh = true,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? executableOverride = null)
    {
        ScriptPath = scriptPath;
        CommandText = null;
        Arguments = arguments;
        Timeout = timeout;
        PreferPwsh = preferPwsh;
        WorkingDirectory = workingDirectory;
        EnvironmentVariables = environmentVariables;
        ExecutableOverride = executableOverride;
        InvocationMode = PowerShellInvocationMode.File;
    }

    /// <summary>
    /// Creates a new command-based request.
    /// </summary>
    /// <param name="commandText">Inline PowerShell text to execute with <c>-Command</c>.</param>
    /// <param name="timeout">Maximum allowed execution time before killing the process.</param>
    /// <param name="preferPwsh">When true, prefer <c>pwsh</c>; otherwise use Windows PowerShell first on Windows.</param>
    /// <param name="workingDirectory">Optional working directory for the PowerShell process.</param>
    /// <param name="environmentVariables">Optional environment variable overrides.</param>
    /// <param name="executableOverride">Optional explicit executable name or path.</param>
    /// <returns>PowerShell command request.</returns>
    public static PowerShellRunRequest ForCommand(
        string commandText,
        TimeSpan timeout,
        bool preferPwsh = true,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? executableOverride = null)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            throw new ArgumentException("Command text is required.", nameof(commandText));

        return new PowerShellRunRequest(
            scriptPath: null,
            commandText: commandText,
            arguments: Array.Empty<string>(),
            timeout: timeout,
            preferPwsh: preferPwsh,
            workingDirectory: workingDirectory,
            environmentVariables: environmentVariables,
            executableOverride: executableOverride,
            invocationMode: PowerShellInvocationMode.Command);
    }

    private PowerShellRunRequest(
        string? scriptPath,
        string? commandText,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        bool preferPwsh,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        string? executableOverride,
        PowerShellInvocationMode invocationMode)
    {
        ScriptPath = scriptPath;
        CommandText = commandText;
        Arguments = arguments;
        Timeout = timeout;
        PreferPwsh = preferPwsh;
        WorkingDirectory = workingDirectory;
        EnvironmentVariables = environmentVariables;
        ExecutableOverride = executableOverride;
        InvocationMode = invocationMode;
    }
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
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerShellRunner"/> class.
    /// </summary>
    /// <param name="processRunner">Optional external process runner implementation.</param>
    public PowerShellRunner(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
    }

    /// <inheritdoc />
    public PowerShellRunResult Run(PowerShellRunRequest request)
    {
        var exe = ResolveExecutable(request.PreferPwsh, request.ExecutableOverride);
        if (exe is null)
        {
            return new PowerShellRunResult(127, string.Empty, "No PowerShell executable found (pwsh or powershell.exe).", string.Empty);
        }

        var arguments = BuildArguments(request);
        var processResult = _processRunner.RunAsync(
            new ProcessRunRequest(
                exe,
                request.WorkingDirectory ?? Environment.CurrentDirectory,
                arguments,
                request.Timeout,
                request.EnvironmentVariables)).GetAwaiter().GetResult();

        return new PowerShellRunResult(processResult.ExitCode, processResult.StdOut, processResult.StdErr, exe);
    }

    /// <summary>
    /// Resolves pwsh or Windows PowerShell on PATH depending on <paramref name="preferPwsh"/>.
    /// </summary>
    private static string? ResolveExecutable(bool preferPwsh, string? executableOverride)
    {
        if (!string.IsNullOrWhiteSpace(executableOverride))
        {
            var overridePath = ResolveOnPath(executableOverride!);
            if (overridePath is not null)
                return overridePath;
            if (File.Exists(executableOverride))
                return executableOverride;
        }

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

    private static IReadOnlyList<string> BuildArguments(PowerShellRunRequest request)
    {
        var arguments = new List<string> {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass"
        };

        if (request.InvocationMode == PowerShellInvocationMode.Command)
        {
            arguments.Add("-Command");
            arguments.Add(request.CommandText ?? string.Empty);
            return arguments;
        }

        arguments.Add("-File");
        arguments.Add(request.ScriptPath ?? string.Empty);
        foreach (var arg in request.Arguments)
            arguments.Add(arg);

        return arguments;
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
