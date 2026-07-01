using System.Diagnostics;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace PowerForge;

/// <summary>
/// Request used to run one file-backed benchmark suite in a temporary Windows local user profile.
/// </summary>
public sealed class PowerShellBenchmarkTemporaryUserRequest
{
    /// <summary>Path to the benchmark spec file that should be re-evaluated in the child process.</summary>
    public string SpecPath { get; set; } = string.Empty;

    /// <summary>Zero-based suite index inside the evaluated spec.</summary>
    public int SuiteIndex { get; set; }

    /// <summary>Working directory inherited from the caller's PowerShell location.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Output root after caller-side path resolution.</summary>
    public string OutputRoot { get; set; } = string.Empty;

    /// <summary>Warmup count after caller-side overrides.</summary>
    public int WarmupCount { get; set; }

    /// <summary>Measured iteration count after caller-side overrides.</summary>
    public int IterationCount { get; set; }

    /// <summary>Run mode after caller-side overrides.</summary>
    public string RunMode { get; set; } = "standard";

    /// <summary>Suite name after caller-side overrides.</summary>
    public string SuiteName { get; set; } = string.Empty;

    /// <summary>Cleanup mode requested by the suite.</summary>
    public PowerShellBenchmarkCleanupMode Cleanup { get; set; } = PowerShellBenchmarkCleanupMode.Always;

    /// <summary>Resolved README/document block paths that the child runner may update.</summary>
    public string[] ReadmePaths { get; set; } = Array.Empty<string>();

    /// <summary>Prefix used for generated local account names.</summary>
    public string UserNamePrefix { get; set; } = "PFBench";
}

/// <summary>
/// Runs file-backed benchmark specs inside a temporary Windows local user profile.
/// </summary>
public sealed class PowerShellBenchmarkTemporaryUserExecutor
{
    /// <summary>
    /// Runs one suite from a benchmark spec in a temporary local Windows account.
    /// </summary>
    /// <param name="request">Temporary-user benchmark request.</param>
    /// <returns>Benchmark run result emitted by the child process.</returns>
    public BenchmarkRunResult Run(PowerShellBenchmarkTemporaryUserRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        ValidateRequest(request);
        ValidateWindowsAdministrator();

        var userName = CreateUserName(request.UserNamePrefix);
        var password = CreatePassword();
        using var securePassword = ToSecureString(password);
        var accountName = string.Concat(Environment.MachineName, "\\", userName);
        var scratchRoot = Path.Combine(Path.GetTempPath(), "pf-benchmark-user-" + userName);
        var resultPath = Path.Combine(scratchRoot, "result.json");
        var stdoutPath = Path.Combine(scratchRoot, "stdout.txt");
        var stderrPath = Path.Combine(scratchRoot, "stderr.txt");
        var wrapperPath = Path.Combine(scratchRoot, "run-benchmark.ps1");
        var readmePathFile = Path.Combine(scratchRoot, "readme-paths.txt");
        var childRequestPath = Path.Combine(scratchRoot, "child-request.json");
        var grantedAccessPaths = new List<string>();
        var failed = true;
        BenchmarkRunResult? result = null;

        Directory.CreateDirectory(scratchRoot);
        try
        {
            CreateLocalUser(userName, securePassword);
            GrantDirectoryAccess(scratchRoot, accountName, "(OI)(CI)F", grantedAccessPaths);
            GrantDirectoryAccess(request.OutputRoot, accountName, "(OI)(CI)F", grantedAccessPaths);
            GrantDirectoryAccess(request.WorkingDirectory, accountName, "(OI)(CI)RX", grantedAccessPaths);
            GrantDirectoryAccess(Path.GetDirectoryName(request.SpecPath) ?? request.WorkingDirectory, accountName, "(OI)(CI)RX", grantedAccessPaths);
            GrantFileAccess(request.SpecPath, accountName, "R", grantedAccessPaths);
            GrantFileAccess(GetPowerForgeAssemblyPath(), accountName, "R", grantedAccessPaths);
            GrantFileAccess(GetPowerForgePowerShellAssemblyPath(), accountName, "R", grantedAccessPaths);
            foreach (var readmePath in request.ReadmePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                GrantFileAccess(readmePath, accountName, "M", grantedAccessPaths);

            File.WriteAllText(wrapperPath, ChildRunnerScript, new UTF8Encoding(false));
            File.WriteAllLines(readmePathFile, request.ReadmePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase), new UTF8Encoding(false));
            BenchmarkJson.Write(childRequestPath, new ChildRunnerRequest
            {
                SpecPath = request.SpecPath,
                SuiteIndex = request.SuiteIndex,
                ResultPath = resultPath,
                PowerForgeAssemblyPath = GetPowerForgeAssemblyPath(),
                PowerForgePowerShellAssemblyPath = GetPowerForgePowerShellAssemblyPath(),
                ReadmePathFile = readmePathFile,
                WorkingDirectory = request.WorkingDirectory,
                OutputRoot = request.OutputRoot,
                WarmupCount = request.WarmupCount,
                IterationCount = request.IterationCount,
                RunMode = request.RunMode ?? string.Empty,
                SuiteName = request.SuiteName ?? string.Empty
            });
            GrantFileAccess(wrapperPath, accountName, "R", grantedAccessPaths);
            GrantFileAccess(readmePathFile, accountName, "R", grantedAccessPaths);
            GrantFileAccess(childRequestPath, accountName, "R", grantedAccessPaths);

            var processResult = RunChildProcess(request, userName, securePassword, wrapperPath, childRequestPath, stdoutPath, stderrPath);
            if (processResult.ExitCode != 0)
                throw new InvalidOperationException($"Temporary benchmark user process failed with exit code {processResult.ExitCode}. STDOUT: {processResult.Stdout} STDERR: {processResult.Stderr} Scratch: {scratchRoot}");

            if (!File.Exists(resultPath))
                throw new InvalidOperationException($"Temporary benchmark user process did not write a result file. STDOUT: {processResult.Stdout} STDERR: {processResult.Stderr} Scratch: {scratchRoot}");

            result = BenchmarkJson.Read<BenchmarkRunResult>(resultPath);
            failed = result.Samples.Any(sample => sample.Status == BenchmarkSampleStatus.Failed);
            EnrichResult(result, request, userName, scratchRoot, ShouldKeep(request.Cleanup, failed));
            RewriteRunReport(result);
            return result;
        }
        finally
        {
            RevokeGrantedAccess(grantedAccessPaths, accountName);
            RemoveLocalUser(userName);
            if (!ShouldKeep(request.Cleanup, failed))
            {
                RemoveUserProfile(userName);
                TryDeleteDirectory(scratchRoot);
            }
        }
    }

    private static void ValidateRequest(PowerShellBenchmarkTemporaryUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SpecPath))
            throw new InvalidOperationException("TemporaryLocalUser benchmark profile requires a file-backed benchmark spec path.");
        if (!File.Exists(request.SpecPath))
            throw new FileNotFoundException("Benchmark spec file was not found.", request.SpecPath);
        if (request.SuiteIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(request.SuiteIndex), "Suite index must be non-negative.");
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
            request.WorkingDirectory = Directory.GetCurrentDirectory();
        if (string.IsNullOrWhiteSpace(request.OutputRoot))
            throw new InvalidOperationException("TemporaryLocalUser benchmark profile requires an output root.");
        Directory.CreateDirectory(request.OutputRoot);
    }

    private static void ValidateWindowsAdministrator()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Benchmark profile 'TemporaryLocalUser' is supported only on Windows.");

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Benchmark profile 'TemporaryLocalUser' requires an elevated administrator PowerShell session so a temporary local user can be created and removed.");
    }

    private static string CreateUserName(string? prefix)
    {
        var safePrefix = new string((prefix ?? "PFBench").Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(safePrefix))
            safePrefix = "PFBench";
        if (safePrefix.Length > 10)
            safePrefix = safePrefix.Substring(0, 10);
        return safePrefix + Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    private static string CreatePassword()
        => "PFb!" + Guid.NewGuid().ToString("N") + "9a";

    private static SecureString ToSecureString(string value)
    {
        var secure = new SecureString();
        foreach (var ch in value)
            secure.AppendChar(ch);
        secure.MakeReadOnly();
        return secure;
    }

    private static void CreateLocalUser(string userName, SecureString password)
    {
        using var ps = PowerShell.Create();
        ps.AddCommand("New-LocalUser")
            .AddParameter("Name", userName)
            .AddParameter("Password", password)
            .AddParameter("Description", "Temporary PowerForge benchmark user")
            .AddParameter("AccountNeverExpires")
            .AddParameter("PasswordNeverExpires");
        InvokePowerShell(ps, $"create temporary benchmark user '{userName}'");
    }

    private static void RemoveLocalUser(string userName)
    {
        using var ps = PowerShell.Create();
        ps.AddCommand("Remove-LocalUser")
            .AddParameter("Name", userName)
            .AddParameter("ErrorAction", ActionPreference.SilentlyContinue);
        _ = ps.Invoke();
    }

    private static void RemoveUserProfile(string userName)
    {
        using var ps = PowerShell.Create();
        ps.AddScript("""
param([string] $UserName)
Get-CimInstance Win32_UserProfile |
    Where-Object { $_.LocalPath -like "*\$UserName" } |
    Remove-CimInstance -ErrorAction SilentlyContinue
""").AddArgument(userName);
        _ = ps.Invoke();
    }

    private static void InvokePowerShell(PowerShell ps, string action)
    {
        _ = ps.Invoke();
        if (!ps.HadErrors)
            return;

        var errors = string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString()));
        throw new InvalidOperationException($"Failed to {action}. {errors}");
    }

    private static ChildProcessResult RunChildProcess(
        PowerShellBenchmarkTemporaryUserRequest request,
        string userName,
        SecureString password,
        string wrapperPath,
        string childRequestPath,
        string stdoutPath,
        string stderrPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetPowerShellExecutable(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(wrapperPath) ?? request.WorkingDirectory,
            CreateNoWindow = true
        };
#pragma warning disable CA1416
        startInfo.Domain = Environment.MachineName;
        startInfo.UserName = userName;
        startInfo.Password = password;
        startInfo.LoadUserProfile = true;
#pragma warning restore CA1416
        ProcessStartInfoEncoding.TryApplyUtf8(startInfo);
        AddArguments(
            startInfo,
            "-NoLogo",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            wrapperPath,
            "-RequestPath",
            childRequestPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start temporary benchmark user process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        File.WriteAllText(stdoutPath, stdout, new UTF8Encoding(false));
        File.WriteAllText(stderrPath, stderr, new UTF8Encoding(false));
        return new ChildProcessResult(process.ExitCode, stdout, stderr);
    }

    private static void GrantDirectoryAccess(string path, string accountName, string rights, ICollection<string> grantedAccessPaths)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        Directory.CreateDirectory(path);
        RunIcacls(path, accountName, rights);
        grantedAccessPaths.Add(path);
    }

    private static void GrantFileAccess(string path, string accountName, string rights, ICollection<string> grantedAccessPaths)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        RunIcacls(path, accountName, rights);
        grantedAccessPaths.Add(path);
    }

    private static void RunIcacls(string path, string accountName, string rights)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "icacls.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(startInfo);
        AddArguments(startInfo, path, "/grant", string.Concat(accountName, ":", rights));

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start icacls.exe.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Failed to grant benchmark temporary user access to '{path}' (icacls exit {process.ExitCode}). STDOUT: {stdout} STDERR: {stderr}");
    }

    private static void RevokeGrantedAccess(IEnumerable<string> paths, string accountName)
    {
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(path) && !File.Exists(path))
                continue;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "icacls.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                ProcessStartInfoEncoding.TryApplyUtf8(startInfo);
                AddArguments(startInfo, path, "/remove:g", accountName);
                using var process = Process.Start(startInfo);
                process?.WaitForExit();
            }
            catch
            {
                // Best-effort ACL cleanup; the user account cleanup still runs below.
            }
        }
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
#if NET472
        startInfo.Arguments = string.Join(" ", arguments.Select(EscapeWindowsArgument));
#else
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
#endif
    }

#if NET472
    private static string EscapeWindowsArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return "\"\"";

        var needsQuotes = argument.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!needsQuotes)
            return argument;

        var builder = new StringBuilder();
        builder.Append('"');

        var backslashCount = 0;
        foreach (var ch in argument)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount);
                backslashCount = 0;
            }

            builder.Append(ch);
        }

        if (backslashCount > 0)
            builder.Append('\\', backslashCount * 2);

        builder.Append('"');
        return builder.ToString();
    }
#endif

    private static string GetPowerShellExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesPwsh = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        if (File.Exists(programFilesPwsh))
            return programFilesPwsh;

        try
        {
            var current = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
                return current!;
        }
        catch
        {
            // Fall back to PATH lookup below.
        }

        return "pwsh.exe";
    }

    private static string GetPowerForgeAssemblyPath()
        => typeof(BenchmarkRunResult).Assembly.Location;

    private static string GetPowerForgePowerShellAssemblyPath()
        => typeof(PowerShellBenchmarkRunner).Assembly.Location;

    private static bool ShouldKeep(PowerShellBenchmarkCleanupMode cleanup, bool failed)
        => cleanup == PowerShellBenchmarkCleanupMode.KeepAlways
           || (cleanup == PowerShellBenchmarkCleanupMode.KeepOnFailure && failed);

    private static void EnrichResult(BenchmarkRunResult result, PowerShellBenchmarkTemporaryUserRequest request, string userName, string scratchRoot, bool retained)
    {
        result.Metadata["profile"] = PowerShellBenchmarkProfileKind.TemporaryLocalUser.ToString();
        result.Metadata["cleanup"] = request.Cleanup.ToString();
        result.Metadata["temporaryUserName"] = userName;
        result.Metadata["temporaryUserScratchRoot"] = scratchRoot;
        result.Metadata["temporaryUserProfileRetained"] = retained ? "true" : "false";
    }

    private static void RewriteRunReport(BenchmarkRunResult result)
    {
        if (result.Artifacts.TryGetValue("run-report.json", out var reportPath) && File.Exists(reportPath))
            BenchmarkJson.Write(reportPath, result);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Cleanup is best effort; the account has already been removed.
        }
    }

    private sealed class ChildRunnerRequest
    {
        public string SpecPath { get; set; } = string.Empty;
        public int SuiteIndex { get; set; }
        public string ResultPath { get; set; } = string.Empty;
        public string PowerForgeAssemblyPath { get; set; } = string.Empty;
        public string PowerForgePowerShellAssemblyPath { get; set; } = string.Empty;
        public string ReadmePathFile { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public string OutputRoot { get; set; } = string.Empty;
        public int WarmupCount { get; set; }
        public int IterationCount { get; set; }
        public string RunMode { get; set; } = string.Empty;
        public string SuiteName { get; set; } = string.Empty;
    }

    private const string ChildRunnerScript = """
param(
    [Parameter(Mandatory = $true)] [string] $RequestPath
)
$ErrorActionPreference = 'Stop'
$request = [System.IO.File]::ReadAllText($RequestPath) | ConvertFrom-Json
Set-Location -LiteralPath $request.WorkingDirectory
[System.Environment]::CurrentDirectory = (Get-Location).ProviderPath
Add-Type -Path $request.PowerForgeAssemblyPath
Add-Type -Path $request.PowerForgePowerShellAssemblyPath
$scriptRoot = [System.IO.Path]::GetDirectoryName($request.SpecPath)
$block = [scriptblock]::Create([System.IO.File]::ReadAllText($request.SpecPath))
$suites = [PowerForge.PowerShellBenchmarkDslRuntime]::Evaluate($block, $scriptRoot)
if ($request.SuiteIndex -ge $suites.Length) {
    throw "Benchmark spec '$($request.SpecPath)' did not produce suite index $($request.SuiteIndex)."
}
$suite = $suites[$request.SuiteIndex]
$suite.Profile = [PowerForge.PowerShellBenchmarkProfileKind]::Current
$suite.OutputRoot = $request.OutputRoot
$suite.WarmupCount = [Math]::Max(0, [int]$request.WarmupCount)
$suite.IterationCount = [Math]::Max(1, [int]$request.IterationCount)
if (-not [string]::IsNullOrWhiteSpace($request.RunMode)) {
    $suite.RunMode = $request.RunMode
}
if (-not [string]::IsNullOrWhiteSpace($request.SuiteName)) {
    $suite.Name = $request.SuiteName
}
$readmePaths = @()
if ([System.IO.File]::Exists($request.ReadmePathFile)) {
    $readmePaths = @([System.IO.File]::ReadAllLines($request.ReadmePathFile))
}
for ($index = 0; $index -lt $suite.ReadmeBlocks.Count -and $index -lt $readmePaths.Count; $index++) {
    if (-not [string]::IsNullOrWhiteSpace($readmePaths[$index])) {
        $suite.ReadmeBlocks[$index].Path = $readmePaths[$index]
    }
}
$result = [PowerForge.PowerShellBenchmarkRunner]::new().Run($suite)
[PowerForge.BenchmarkJson]::Write($request.ResultPath, $result)
""";

    private sealed class ChildProcessResult
    {
        internal ChildProcessResult(int exitCode, string stdout, string stderr)
        {
            ExitCode = exitCode;
            Stdout = stdout;
            Stderr = stderr;
        }

        internal int ExitCode { get; }

        internal string Stdout { get; }

        internal string Stderr { get; }
    }
}
