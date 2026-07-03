using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerForge;

/// <summary>
/// Request used to run one file-backed benchmark suite across PowerShell host processes.
/// </summary>
public sealed class PowerShellBenchmarkHostRunRequest
{
    /// <summary>Path to the benchmark spec file that should be re-evaluated in child processes.</summary>
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

    /// <summary>Run order after caller-side overrides.</summary>
    public PowerShellBenchmarkRunOrder RunOrder { get; set; } = PowerShellBenchmarkRunOrder.Rotated;

    /// <summary>Delay between measured samples, in milliseconds.</summary>
    public int CooldownMilliseconds { get; set; }

    /// <summary>Outlier mode after caller-side overrides.</summary>
    public PowerShellBenchmarkOutlierMode OutlierMode { get; set; } = PowerShellBenchmarkOutlierMode.None;

    /// <summary>Suite name after caller-side overrides.</summary>
    public string SuiteName { get; set; } = string.Empty;

    /// <summary>Variables exposed to benchmark specs as <c>$BenchmarkVariables</c>.</summary>
    public Dictionary<string, string?> BenchmarkVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Selection applied to the suite after child-process evaluation.</summary>
    public PowerShellBenchmarkSelection Selection { get; set; } = new();

    /// <summary>Host labels that should be executed.</summary>
    public string[] Hosts { get; set; } = Array.Empty<string>();

    /// <summary>Maximum time allowed for each external PowerShell host process.</summary>
    public int ExternalHostTimeoutSeconds { get; set; } = 1800;
}

/// <summary>
/// Runs file-backed benchmark specs in selected PowerShell host processes and merges their results.
/// </summary>
public sealed class PowerShellBenchmarkHostExecutor
{
    /// <summary>
    /// Returns true when a file-backed suite needs child host processes for its host matrix.
    /// </summary>
    /// <param name="suite">Benchmark suite.</param>
    /// <returns>True when at least one requested host is not the current PowerShell host.</returns>
    public bool RequiresHostProcesses(PowerShellBenchmarkSuite suite)
        => PowerShellBenchmarkHostRuntime.RequiresOutOfProcessHost(suite);

    /// <summary>
    /// Runs one suite from a benchmark spec in selected PowerShell hosts.
    /// </summary>
    /// <param name="suite">Caller-side suite after overrides and path resolution.</param>
    /// <param name="request">Host benchmark request.</param>
    /// <returns>Merged benchmark run result.</returns>
    public BenchmarkRunResult Run(PowerShellBenchmarkSuite suite, PowerShellBenchmarkHostRunRequest request)
    {
        if (suite is null) throw new ArgumentNullException(nameof(suite));
        if (request is null) throw new ArgumentNullException(nameof(request));
        ValidateRequest(request);
        PowerShellBenchmarkArtifactWriter.ValidateReadmeBlocks(suite);

        var hosts = request.Hosts.Length == 0
            ? PowerShellBenchmarkHostRuntime.GetRequestedHosts(suite)
            : request.Hosts;
        if (hosts.Length == 0)
            hosts = new[] { "Current" };

        var started = DateTimeOffset.UtcNow;
        var results = new List<BenchmarkRunResult>();
        foreach (var host in hosts)
        {
            results.Add(RunHost(suite, request, host, started));
        }

        var merged = PowerShellBenchmarkResultMerger.Merge(suite, results, started);
        PowerShellBenchmarkArtifactWriter.WriteArtifacts(suite, merged);
        PowerShellBenchmarkArtifactWriter.UpdateReadmeBlocks(suite, merged);
        return merged;
    }

    private static void ValidateRequest(PowerShellBenchmarkHostRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SpecPath))
            throw new InvalidOperationException("PowerShell host benchmark execution requires a file-backed benchmark spec path.");
        if (!File.Exists(request.SpecPath))
            throw new FileNotFoundException("Benchmark spec file was not found.", request.SpecPath);
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
            request.WorkingDirectory = Directory.GetCurrentDirectory();
        if (string.IsNullOrWhiteSpace(request.OutputRoot))
            throw new InvalidOperationException("PowerShell host benchmark execution requires an output root.");
        if (request.ExternalHostTimeoutSeconds < 1)
            request.ExternalHostTimeoutSeconds = 1;
        Directory.CreateDirectory(request.OutputRoot);
    }

    private static BenchmarkRunResult RunHost(PowerShellBenchmarkSuite suite, PowerShellBenchmarkHostRunRequest request, string host, DateTimeOffset started)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "pf-benchmark-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);
        var deleteScratch = false;
        var resultPath = Path.Combine(scratchRoot, "result.json");
        var stdoutPath = Path.Combine(scratchRoot, "stdout.txt");
        var stderrPath = Path.Combine(scratchRoot, "stderr.txt");
        var wrapperPath = Path.Combine(scratchRoot, "run-benchmark.ps1");
        var readmePathFile = Path.Combine(scratchRoot, "readme-paths.txt");
        var childRequestPath = Path.Combine(scratchRoot, "child-request.json");

        try
        {
            var executable = PowerShellBenchmarkHostRuntime.ResolveExecutable(host);
            File.WriteAllText(wrapperPath, ChildRunnerScript, new UTF8Encoding(false));
            File.WriteAllLines(readmePathFile, Array.Empty<string>(), new UTF8Encoding(false));
            BenchmarkJson.Write(childRequestPath, CreateChildRequest(request, host, executable, resultPath, readmePathFile, started));

            var processResult = RunProcess(executable, scratchRoot, stdoutPath, stderrPath, wrapperPath, childRequestPath, request.ExternalHostTimeoutSeconds);
            if (!File.Exists(resultPath))
                throw new InvalidOperationException($"Benchmark host '{host}' did not write a result file. STDOUT: {processResult.Stdout} STDERR: {processResult.Stderr} Scratch: {scratchRoot}");

            var result = BenchmarkJson.Read<BenchmarkRunResult>(resultPath);
            if (processResult.ExitCode != 0)
                throw new InvalidOperationException($"Benchmark host '{host}' failed with exit code {processResult.ExitCode}. STDOUT: {processResult.Stdout} STDERR: {processResult.Stderr} Scratch: {scratchRoot}");
            deleteScratch = true;
            return result;
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            return CreateHostFailureResult(suite, host, started, scratchRoot, ex);
        }
        finally
        {
            if (deleteScratch)
                TryDeleteDirectory(scratchRoot);
        }
    }

    private static PowerShellBenchmarkChildRunnerRequest CreateChildRequest(
        PowerShellBenchmarkHostRunRequest request,
        string host,
        string executable,
        string resultPath,
        string readmePathFile,
        DateTimeOffset started)
    {
        var selection = new PowerShellBenchmarkSelection
        {
            Cases = request.Selection.Cases,
            Engines = request.Selection.Engines,
            Operations = request.Selection.Operations,
            Hosts = new[] { NormalizeChildHostSelection(host, executable) }
        };
        return new PowerShellBenchmarkChildRunnerRequest
        {
            SpecPath = request.SpecPath,
            SuiteIndex = request.SuiteIndex,
            ResultPath = resultPath,
            PowerForgeAssemblyPath = PowerShellBenchmarkHostRuntime.ResolveAssemblyForHost(typeof(BenchmarkRunResult).Assembly.Location, executable),
            PowerForgePowerShellAssemblyPath = PowerShellBenchmarkHostRuntime.ResolveAssemblyForHost(typeof(PowerShellBenchmarkRunner).Assembly.Location, executable),
            ReadmePathFile = readmePathFile,
            WorkingDirectory = request.WorkingDirectory,
            OutputRoot = request.OutputRoot,
            WarmupCount = request.WarmupCount,
            IterationCount = request.IterationCount,
            RunMode = request.RunMode ?? string.Empty,
            RunOrder = request.RunOrder.ToString(),
            CooldownMilliseconds = request.CooldownMilliseconds,
            OutlierMode = request.OutlierMode.ToString(),
            SuiteName = request.SuiteName ?? string.Empty,
            PlanningProfile = PowerShellBenchmarkProfileKind.Current.ToString(),
            BenchmarkVariables = request.BenchmarkVariables,
            Selection = selection,
            ModulePaths = PowerShellBenchmarkTemporaryUserExecutor.GetImportableCallerModulePaths(),
            RunStartedUtc = started.ToString("O"),
            UpdateReadmeBlocks = false
        };
    }

    private static ProcessResult RunProcess(string executable, string workingDirectory, string stdoutPath, string stderrPath, string wrapperPath, string childRequestPath, int timeoutSeconds)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = string.Join(" ", new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                QuoteArgument(wrapperPath),
                "-RequestPath",
                QuoteArgument(childRequestPath)
            })
        };
        ConfigureModulePath(start, executable);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Failed to start benchmark host '{executable}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var timeoutMilliseconds = timeoutSeconds >= int.MaxValue / 1000 ? int.MaxValue : timeoutSeconds * 1000;
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // A timeout already tells the caller the host did not finish cleanly.
            }

            process.WaitForExit();
            var timedOutStdout = stdoutTask.GetAwaiter().GetResult();
            var timedOutStderr = stderrTask.GetAwaiter().GetResult();
            File.WriteAllText(stdoutPath, timedOutStdout, new UTF8Encoding(false));
            File.WriteAllText(stderrPath, timedOutStderr, new UTF8Encoding(false));
            throw new TimeoutException($"Benchmark host '{executable}' exceeded the external host timeout of {timeoutSeconds} second(s). STDOUT: {timedOutStdout} STDERR: {timedOutStderr}");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        File.WriteAllText(stdoutPath, stdout, new UTF8Encoding(false));
        File.WriteAllText(stderrPath, stderr, new UTF8Encoding(false));
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static BenchmarkRunResult CreateHostFailureResult(PowerShellBenchmarkSuite suite, string host, DateTimeOffset started, string scratchRoot, Exception exception)
    {
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var reason = $"External host '{host}' failed before completing benchmark execution: {exception.Message} Scratch: {scratchRoot}";
        var samples = new PowerShellBenchmarkRunner()
            .Plan(suite)
            .Where(item => IsHostMatch(item.Host, host))
            .Select(item => CreateHostFailureSample(runId, suite, item, BenchmarkSampleStatus.Failed, reason))
            .ToArray();
        if (samples.Length == 0)
        {
            samples = new[]
            {
                new BenchmarkSample
                {
                    RunId = runId,
                    Suite = suite.Name,
                    Scenario = "Host",
                    Operation = "Host",
                    Engine = "Host",
                    Host = host,
                    Os = RuntimeInformation.OSDescription,
                    RunMode = suite.RunMode,
                    Iteration = 0,
                    Status = BenchmarkSampleStatus.Failed,
                    Reason = reason
                }
            };
        }

        return new BenchmarkRunResult
        {
            RunId = runId,
            Suite = suite.Name,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Samples = samples,
            Metadata = PowerShellBenchmarkEnvironmentMetadata.Build(suite)
        };
    }

    private static BenchmarkSample CreateHostFailureSample(string runId, PowerShellBenchmarkSuite suite, PowerShellBenchmarkWorkItem item, BenchmarkSampleStatus status, string reason)
        => new()
        {
            RunId = runId,
            Suite = suite.Name,
            Scenario = item.Scenario,
            Operation = item.Operation,
            Engine = item.Engine,
            Host = item.Host,
            Os = RuntimeInformation.OSDescription,
            RunMode = suite.RunMode,
            Iteration = 0,
            Status = status,
            Reason = reason,
            Variables = ToVariables(item.Values)
        };

    private static bool IsHostMatch(string itemHost, string requestedHost)
        => string.Equals(itemHost, requestedHost, StringComparison.OrdinalIgnoreCase)
           || PowerShellBenchmarkHostRuntime.IsCurrentHost(requestedHost, itemHost);

    private static bool IsFatal(Exception exception)
        => exception is OutOfMemoryException or StackOverflowException or AccessViolationException;

    private static Dictionary<string, string?> ToVariables(IReadOnlyDictionary<string, object?> values)
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
            variables[value.Key] = Convert.ToString(value.Value, CultureInfo.InvariantCulture);
        return variables;
    }

    private static string QuoteArgument(string value)
        => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static void ConfigureModulePath(ProcessStartInfo start, string executable)
    {
        if (!PowerShellBenchmarkHostRuntime.IsDesktopExecutable(executable))
            return;

        var modulePaths = GetWindowsPowerShellModulePaths();
        if (modulePaths.Length > 0)
            start.Environment["PSModulePath"] = string.Join(Path.PathSeparator.ToString(), modulePaths);
    }

    private static string[] GetWindowsPowerShellModulePaths()
    {
        var paths = new List<string>();
        AddIfDirectory(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WindowsPowerShell", "Modules"));
        AddIfDirectory(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsPowerShell", "Modules"));
        AddIfDirectory(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "Modules"));

        var inherited = Environment.GetEnvironmentVariable("PSModulePath");
        if (!string.IsNullOrWhiteSpace(inherited))
        {
            foreach (var path in inherited.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                if (path.Contains(@"\PowerShell\7\", StringComparison.OrdinalIgnoreCase)
                    || path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
                    continue;
                AddIfDirectory(paths, path);
            }
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddIfDirectory(List<string> paths, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            paths.Add(path);
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
            // Host scratch cleanup is best effort after the child result has been imported.
        }
    }

    private static string ChildRunnerScript => EmbeddedScripts.Load("Scripts/Benchmarks/TemporaryUserChildRunner.ps1");

    internal static string NormalizeChildHostSelection(string host, string executable)
    {
        if (File.Exists(host)
            && string.Equals(Path.GetFullPath(host), Path.GetFullPath(executable), PathComparison))
            return "Current";

        return host;
    }

    private static StringComparison PathComparison
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed class ProcessResult
    {
        internal ProcessResult(int exitCode, string stdout, string stderr)
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
