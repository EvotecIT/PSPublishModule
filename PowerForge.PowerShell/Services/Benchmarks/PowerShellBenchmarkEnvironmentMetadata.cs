using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Captures environment metadata for PowerShell benchmark reports.
/// </summary>
internal static class PowerShellBenchmarkEnvironmentMetadata
{
    internal sealed class SourceProvenance
    {
        internal string? GitSha { get; set; }
        internal string? GitBranch { get; set; }
        internal string? GitStatus { get; set; }
    }

    /// <summary>
    /// Builds a metadata map for a benchmark suite run.
    /// </summary>
    /// <param name="suite">Benchmark suite.</param>
    /// <returns>Metadata values.</returns>
    public static Dictionary<string, string> Build(PowerShellBenchmarkSuite suite)
        => Build(suite, CaptureSourceProvenance(suite));

    internal static Dictionary<string, string> Build(
        PowerShellBenchmarkSuite suite,
        SourceProvenance startedProvenance)
    {
        if (startedProvenance is null)
            throw new ArgumentNullException(nameof(startedProvenance));
        SourceProvenance finishedProvenance = CaptureSourceProvenance(suite);
        if (!string.Equals(startedProvenance.GitSha, finishedProvenance.GitSha, StringComparison.Ordinal) ||
            !string.Equals(startedProvenance.GitBranch, finishedProvenance.GitBranch, StringComparison.Ordinal) ||
            !string.Equals(startedProvenance.GitStatus, finishedProvenance.GitStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Benchmark source provenance changed while measurements were running. " +
                "Discard this run and measure again from an unchanged worktree.");
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["suite"] = suite.Name,
            ["pwsh"] = PSVersionInfo(),
            ["psEdition"] = Convert.ToString(PSVersionInfoValue("PSEdition"), CultureInfo.InvariantCulture) ?? string.Empty,
            ["machine"] = Environment.MachineName,
            ["user"] = Environment.UserName,
            ["os"] = Environment.OSVersion.ToString(),
            ["osLabel"] = GetOperatingSystemLabel(),
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["processorCount"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["workingSetBytes"] = Environment.WorkingSet.ToString(CultureInfo.InvariantCulture),
            ["profile"] = suite.Profile.ToString(),
            ["cleanup"] = suite.Cleanup.ToString(),
            ["warmupCount"] = suite.WarmupCount.ToString(CultureInfo.InvariantCulture),
            ["iterationCount"] = suite.IterationCount.ToString(CultureInfo.InvariantCulture),
            ["runOrder"] = suite.RunOrder.ToString(),
            ["memoryCleanup"] = suite.MemoryCleanup.ToString(),
            ["cooldownMilliseconds"] = suite.CooldownMilliseconds.ToString(CultureInfo.InvariantCulture),
            ["outlierMode"] = suite.OutlierMode.ToString(),
            ["runMode"] = suite.RunMode
        };
        foreach (var item in suite.Metadata)
            metadata["benchmark." + item.Key] = item.Value;
        AddMetadata(metadata, "gitSha", startedProvenance.GitSha);
        AddMetadata(metadata, "gitBranch", startedProvenance.GitBranch);
        if (!string.IsNullOrWhiteSpace(startedProvenance.GitSha) &&
            startedProvenance.GitStatus is not null)
        {
            metadata["gitWorktreeClean"] =
                string.IsNullOrWhiteSpace(startedProvenance.GitStatus) ? "true" : "false";
        }
        return metadata;
    }

    internal static SourceProvenance CaptureSourceProvenance(PowerShellBenchmarkSuite suite)
    {
        if (suite is null)
            throw new ArgumentNullException(nameof(suite));
        string? gitSha = ReadGitValue(suite.SourceRoot, "rev-parse HEAD");
        return new SourceProvenance
        {
            GitSha = gitSha,
            GitBranch = ReadGitValue(suite.SourceRoot, "branch --show-current"),
            GitStatus = string.IsNullOrWhiteSpace(gitSha)
                ? null
                : ReadGitValue(suite.SourceRoot, "status --porcelain --untracked-files=normal")
        };
    }

    /// <summary>
    /// Captures the typed environment identity used by cross-platform evidence catalogs.
    /// </summary>
    /// <returns>Normalized environment identity.</returns>
    public static BenchmarkEnvironmentInfo BuildEnvironment()
        => new()
        {
            OsFamily = GetOperatingSystemLabel(),
            OsDescription = RuntimeInformation.OSDescription,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorName = GetProcessorName(),
            LogicalCoreCount = Environment.ProcessorCount,
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            Runner = $"PowerShell {PSVersionInfo()}",
            MachineName = Environment.MachineName
        };

    private static void AddMetadata(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            metadata[key] = value!.Trim();
    }

    private static string PSVersionInfo()
        => Convert.ToString(PSVersionInfoValue("PSVersion"), CultureInfo.InvariantCulture) ?? string.Empty;

    private static object? PSVersionInfoValue(string name)
    {
        using var ps = PowerShell.Create();
        return ps.AddScript($"$PSVersionTable.{name}").Invoke().FirstOrDefault()?.BaseObject;
    }

    private static string GetOperatingSystemLabel()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS";
        return Environment.OSVersion.Platform.ToString();
    }

    private static string GetProcessorName()
    {
        string? environmentName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(environmentName))
            return environmentName.Trim();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                foreach (string line in File.ReadLines("/proc/cpuinfo"))
                {
                    int separator = line.IndexOf(':');
                    if (separator <= 0)
                        continue;
                    string key = line.Substring(0, separator).Trim();
                    if (!key.Equals("model name", StringComparison.OrdinalIgnoreCase) &&
                        !key.Equals("hardware", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string value = line.Substring(separator + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
            catch (IOException)
            {
                // Fall through to the architecture identity.
            }
            catch (UnauthorizedAccessException)
            {
                // Fall through to the architecture identity.
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string? sysctlName = ReadProcessValue(
                "/usr/sbin/sysctl",
                "-n machdep.cpu.brand_string",
                timeoutMilliseconds: 3000);
            if (string.IsNullOrWhiteSpace(sysctlName))
                sysctlName = ReadProcessValue("/usr/sbin/sysctl", "-n hw.model", timeoutMilliseconds: 3000);
            if (!string.IsNullOrWhiteSpace(sysctlName))
                return sysctlName!;
        }

        return $"{RuntimeInformation.OSArchitecture} processor";
    }

    private static string? ReadGitValue(string? workingDirectory, string arguments)
        => ReadProcessValue(
            "git",
            arguments,
            timeoutMilliseconds: 3000,
            workingDirectory: workingDirectory);

    internal static string? ReadProcessValue(
        string fileName,
        string arguments,
        int timeoutMilliseconds,
        string? workingDirectory = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (!string.IsNullOrWhiteSpace(workingDirectory) &&
                Directory.Exists(workingDirectory))
            {
                startInfo.WorkingDirectory = Path.GetFullPath(workingDirectory!);
            }
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            var output = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                    output.AppendLine(eventArgs.Data);
            };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
                catch
                {
                    // The process may have exited between the timeout and cleanup.
                }
                return null;
            }

            // Ensure asynchronous output handlers have received the final buffered lines.
            process.WaitForExit();
            return process.ExitCode == 0 ? output.ToString().Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
