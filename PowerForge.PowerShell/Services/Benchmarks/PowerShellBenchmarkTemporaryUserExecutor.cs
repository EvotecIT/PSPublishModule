using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
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

        WindowsTemporaryIdentityLease? identity = null;
        var scratchRoot = string.Empty;
        var resultPath = string.Empty;
        var stdoutPath = string.Empty;
        var stderrPath = string.Empty;
        var wrapperPath = string.Empty;
        var readmePathFile = string.Empty;
        var childRequestPath = string.Empty;
        var failed = true;
        BenchmarkRunResult? result = null;

        try
        {
            identity = WindowsTemporaryIdentityLease.Create(new WindowsTemporaryIdentityOptions
            {
                UserNamePrefix = request.UserNamePrefix,
                ScratchRootPrefix = "pf-benchmark-user-",
                Description = "Temporary PowerForge benchmark user",
                CapabilityName = "Benchmark profile 'TemporaryLocalUser'"
            });

            scratchRoot = identity.ScratchRoot;
            resultPath = Path.Combine(scratchRoot, "result.json");
            stdoutPath = Path.Combine(scratchRoot, "stdout.txt");
            stderrPath = Path.Combine(scratchRoot, "stderr.txt");
            wrapperPath = Path.Combine(scratchRoot, "run-benchmark.ps1");
            readmePathFile = Path.Combine(scratchRoot, "readme-paths.txt");
            childRequestPath = Path.Combine(scratchRoot, "child-request.json");

            identity.GrantDirectoryAccess(scratchRoot, "(OI)(CI)F");
            identity.GrantDirectoryAccess(request.OutputRoot, "(OI)(CI)F");
            identity.GrantDirectoryAccess(request.WorkingDirectory, "(OI)(CI)RX");
            identity.GrantDirectoryAccess(Path.GetDirectoryName(request.SpecPath) ?? request.WorkingDirectory, "(OI)(CI)RX");
            identity.GrantFileAccess(request.SpecPath, "R");
            identity.GrantFileAccess(GetPowerForgeAssemblyPath(), "R");
            identity.GrantFileAccess(GetPowerForgePowerShellAssemblyPath(), "R");
            var modulePaths = GetImportableCallerModulePaths();
            foreach (var modulePath in modulePaths)
            {
                var moduleDirectory = Path.GetDirectoryName(modulePath);
                if (!string.IsNullOrWhiteSpace(moduleDirectory))
                    identity.GrantDirectoryAccess(moduleDirectory!, "(OI)(CI)RX");
                identity.GrantFileAccess(modulePath, "R");
            }
            foreach (var readmePath in GetReadmeGrantPaths(request))
                identity.GrantFileAccess(readmePath, "M");

            File.WriteAllText(wrapperPath, ChildRunnerScript, new UTF8Encoding(false));
            File.WriteAllLines(readmePathFile, GetReadmePathsForChild(request), new UTF8Encoding(false));
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
                SuiteName = request.SuiteName ?? string.Empty,
                ModulePaths = modulePaths
            });
            identity.GrantFileAccess(wrapperPath, "R");
            identity.GrantFileAccess(readmePathFile, "R");
            identity.GrantFileAccess(childRequestPath, "R");

            var processResult = identity.RunProcess(
                GetPowerShellExecutable(),
                Path.GetDirectoryName(wrapperPath) ?? request.WorkingDirectory,
                stdoutPath,
                stderrPath,
                "-NoLogo",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                wrapperPath,
                "-RequestPath",
                childRequestPath);
            if (processResult.ExitCode != 0)
                throw new InvalidOperationException($"Temporary benchmark user process failed with exit code {processResult.ExitCode}. STDOUT: {processResult.Stdout} STDERR: {processResult.Stderr} Scratch: {scratchRoot}");

            if (!File.Exists(resultPath))
                throw new InvalidOperationException($"Temporary benchmark user process did not write a result file. STDOUT: {processResult.Stdout} STDERR: {processResult.Stderr} Scratch: {scratchRoot}");

            result = BenchmarkJson.Read<BenchmarkRunResult>(resultPath);
            failed = result.Samples.Any(sample => sample.Status == BenchmarkSampleStatus.Failed);
            EnrichResult(result, request, identity.UserName, scratchRoot, ShouldKeep(request.Cleanup, failed));
            RewriteEnrichedArtifacts(result);
            return result;
        }
        finally
        {
            if (identity is not null)
            {
                if (ShouldKeep(request.Cleanup, failed))
                    identity.RetainProfileAndScratch();
                identity.Dispose();
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

    private static string GetPowerShellExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesPwsh = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        try
        {
            var current = Process.GetCurrentProcess().MainModule?.FileName;
            if (IsPowerShellExecutable(current) && !IsWindowsAppsPath(current))
                return current!;
            if (IsPwshExecutable(current) && File.Exists(programFilesPwsh))
                return programFilesPwsh;
            if (IsPowerShellExecutable(current))
                return current!;
        }
        catch
        {
            // Fall back to the installed PowerShell locations below.
        }

        if (File.Exists(programFilesPwsh))
            return programFilesPwsh;

        return "pwsh.exe";
    }

    private static bool IsPowerShellExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;
        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, "pwsh.exe", StringComparison.OrdinalIgnoreCase)
               || string.Equals(fileName, "powershell.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPwshExecutable(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && string.Equals(Path.GetFileName(path), "pwsh.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowsAppsPath(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    private static string GetPowerForgeAssemblyPath()
        => typeof(BenchmarkRunResult).Assembly.Location;

    private static string GetPowerForgePowerShellAssemblyPath()
        => typeof(PowerShellBenchmarkRunner).Assembly.Location;

    internal static string[] GetImportableCallerModulePaths()
    {
        try
        {
            using var powerShell = PowerShell.Create();
            if (Runspace.DefaultRunspace is not null)
                powerShell.Runspace = Runspace.DefaultRunspace;
            powerShell.AddCommand("Get-Module");
            return powerShell.Invoke<PSModuleInfo>()
                .Select(module => module.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    internal static string[] GetReadmePathsForChild(PowerShellBenchmarkTemporaryUserRequest request)
        => request.ReadmePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

    private static string[] GetReadmeGrantPaths(PowerShellBenchmarkTemporaryUserRequest request)
        => GetReadmePathsForChild(request)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

    internal static void RewriteEnrichedArtifacts(BenchmarkRunResult result)
    {
        if (result.Artifacts.TryGetValue("run-report.json", out var reportPath) && File.Exists(reportPath))
            BenchmarkJson.Write(reportPath, result);
        if (result.Artifacts.TryGetValue("metadata.json", out var metadataPath) && File.Exists(metadataPath))
            BenchmarkJson.Write(metadataPath, result.Metadata);
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
        public string[] ModulePaths { get; set; } = Array.Empty<string>();
    }

    private static string ChildRunnerScript => EmbeddedScripts.Load("Scripts/Benchmarks/TemporaryUserChildRunner.ps1");
}
