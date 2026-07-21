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
    /// <summary>
    /// Minimum .NET runtime major version required from a PowerShell Core host.
    /// A value of zero allows the normal preferred-host fallback behavior.
    /// </summary>
    public int RequiredRuntimeMajor { get; }
    /// <summary>Optional working directory for the PowerShell process.</summary>
    public string? WorkingDirectory { get; }
    /// <summary>Optional environment variable overrides for the PowerShell process.</summary>
    public IReadOnlyDictionary<string, string?>? EnvironmentVariables { get; }
    /// <summary>Optional explicit executable name or path.</summary>
    public string? ExecutableOverride { get; }
    /// <summary>When true, capture standard output.</summary>
    public bool CaptureOutput { get; }
    /// <summary>When true, capture standard error.</summary>
    public bool CaptureError { get; }
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
        string? executableOverride = null,
        bool captureOutput = true,
        bool captureError = true)
    {
        ScriptPath = scriptPath;
        CommandText = null;
        Arguments = arguments;
        Timeout = timeout;
        PreferPwsh = preferPwsh;
        WorkingDirectory = workingDirectory;
        EnvironmentVariables = environmentVariables;
        ExecutableOverride = executableOverride;
        CaptureOutput = captureOutput;
        CaptureError = captureError;
        InvocationMode = PowerShellInvocationMode.File;
        RequiredRuntimeMajor = 0;
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
    /// <param name="captureOutput">When true, capture standard output.</param>
    /// <param name="captureError">When true, capture standard error.</param>
    /// <returns>PowerShell command request.</returns>
    public static PowerShellRunRequest ForCommand(
        string commandText,
        TimeSpan timeout,
        bool preferPwsh = true,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? executableOverride = null,
        bool captureOutput = true,
        bool captureError = true)
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
            captureOutput: captureOutput,
            captureError: captureError,
            requiredRuntimeMajor: 0,
            invocationMode: PowerShellInvocationMode.Command);
    }

    /// <summary>
    /// Creates a command request that requires a compatible PowerShell Core host.
    /// </summary>
    /// <param name="commandText">Inline PowerShell text to execute with <c>-Command</c>.</param>
    /// <param name="timeout">Maximum allowed execution time before killing the process.</param>
    /// <param name="requiredRuntimeMajor">Minimum .NET runtime major version required from <c>pwsh</c>.</param>
    /// <param name="workingDirectory">Optional working directory for the PowerShell process.</param>
    /// <param name="environmentVariables">Optional environment variable overrides.</param>
    /// <param name="executableOverride">Optional explicit executable name or path.</param>
    /// <param name="captureOutput">When true, capture standard output.</param>
    /// <param name="captureError">When true, capture standard error.</param>
    /// <returns>PowerShell command request.</returns>
    public static PowerShellRunRequest ForCompatibleCommand(
        string commandText,
        TimeSpan timeout,
        int requiredRuntimeMajor,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? executableOverride = null,
        bool captureOutput = true,
        bool captureError = true)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            throw new ArgumentException("Command text is required.", nameof(commandText));
        if (requiredRuntimeMajor <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredRuntimeMajor), "Required runtime major must be greater than zero.");

        return new PowerShellRunRequest(
            scriptPath: null,
            commandText: commandText,
            arguments: Array.Empty<string>(),
            timeout: timeout,
            preferPwsh: true,
            workingDirectory: workingDirectory,
            environmentVariables: environmentVariables,
            executableOverride: executableOverride,
            captureOutput: captureOutput,
            captureError: captureError,
            requiredRuntimeMajor: requiredRuntimeMajor,
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
        bool captureOutput,
        bool captureError,
        int requiredRuntimeMajor,
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
        CaptureOutput = captureOutput;
        CaptureError = captureError;
        RequiredRuntimeMajor = requiredRuntimeMajor;
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
        string? resolutionError = null;
        var exe = request.RequiredRuntimeMajor > 0
            ? ResolveCompatiblePwsh(request, out resolutionError)
            : ResolveExecutable(request.PreferPwsh, request.ExecutableOverride);
        if (exe is null)
        {
            var error = resolutionError ?? "No PowerShell executable found (pwsh or powershell.exe).";
            return new PowerShellRunResult(127, string.Empty, error, string.Empty);
        }

        var arguments = BuildArguments(request);
        var processResult = _processRunner.RunAsync(
            new ProcessRunRequest(
                exe,
                request.WorkingDirectory ?? Environment.CurrentDirectory,
                arguments,
                request.Timeout,
                request.EnvironmentVariables,
                request.CaptureOutput,
                request.CaptureError)).GetAwaiter().GetResult();

        return new PowerShellRunResult(processResult.ExitCode, processResult.StdOut, processResult.StdErr, exe);
    }

    private string? ResolveCompatiblePwsh(PowerShellRunRequest request, out string? error)
    {
        var requiredRuntimeMajor = request.RequiredRuntimeMajor;
        IReadOnlyList<string> candidates;
        if (!string.IsNullOrWhiteSpace(request.ExecutableOverride))
        {
            candidates = ResolveExecutableCandidates(request.ExecutableOverride!);
            if (candidates.Count == 0)
            {
                error = $"PowerShell executable override '{request.ExecutableOverride}' was not found. "
                        + $"The requested command requires pwsh running on .NET {requiredRuntimeMajor} or later.";
                return null;
            }
        }
        else
        {
            var pwshName = Path.DirectorySeparatorChar == '\\' ? "pwsh.exe" : "pwsh";
            candidates = ResolveExecutableCandidates(pwshName);
        }

        foreach (var candidate in candidates)
        {
            if (IsCompatiblePwsh(candidate, request, requiredRuntimeMajor))
            {
                error = null;
                return candidate;
            }
        }

        var overrideDescription = string.IsNullOrWhiteSpace(request.ExecutableOverride)
            ? "No compatible pwsh executable was found"
            : $"PowerShell executable override '{request.ExecutableOverride}' is not compatible";
        error = $"{overrideDescription}. The requested command requires PowerShell Core running on .NET {requiredRuntimeMajor} or later.";
        return null;
    }

    private bool IsCompatiblePwsh(string executable, PowerShellRunRequest request, int requiredRuntimeMajor)
    {
        var probeArguments = new[] {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            "'{0}|{1}' -f $PSVersionTable.PSEdition, [Environment]::Version.Major"
        };
        var probeResult = _processRunner.RunAsync(
            new ProcessRunRequest(
                executable,
                request.WorkingDirectory ?? Environment.CurrentDirectory,
                probeArguments,
                TimeSpan.FromSeconds(15),
                request.EnvironmentVariables,
                captureOutput: true,
                captureError: true)).GetAwaiter().GetResult();

        if (!probeResult.Succeeded)
            return false;

        var lines = probeResult.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Trim().Split('|');
            if (parts.Length == 2
                && string.Equals(parts[0], "Core", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[1], out var runtimeMajor)
                && runtimeMajor >= requiredRuntimeMajor)
            {
                return true;
            }
        }

        return false;
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
        var candidates = ResolveExecutableCandidates(fileName);
        return candidates.Count > 0 ? candidates[0] : null;
    }

    private static IReadOnlyList<string> ResolveExecutableCandidates(string fileName)
    {
        var candidates = new List<string>();
        if (Path.IsPathRooted(fileName) || fileName.IndexOf(Path.DirectorySeparatorChar) >= 0 || fileName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
        {
            if (File.Exists(fileName))
                candidates.Add(fileName);
            return candidates;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate) && !candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(candidate);
            }
            catch { /* ignore */ }
        }
        if (candidates.Count == 0 && File.Exists(fileName))
            candidates.Add(fileName);
        return candidates;
    }

}
