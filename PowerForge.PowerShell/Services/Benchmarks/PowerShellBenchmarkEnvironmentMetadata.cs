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
    /// <summary>
    /// Builds a metadata map for a benchmark suite run.
    /// </summary>
    /// <param name="suite">Benchmark suite.</param>
    /// <returns>Metadata values.</returns>
    public static Dictionary<string, string> Build(PowerShellBenchmarkSuite suite)
    {
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
        AddMetadata(metadata, "gitSha", ReadGitValue("rev-parse HEAD"));
        AddMetadata(metadata, "gitBranch", ReadGitValue("branch --show-current"));
        return metadata;
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

    private static string? ReadGitValue(string arguments)
        => ReadProcessValue("git", arguments, timeoutMilliseconds: 3000);

    private static string? ReadProcessValue(
        string fileName,
        string arguments,
        int timeoutMilliseconds)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // The process may have exited between the timeout and cleanup.
                }
                return null;
            }
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
