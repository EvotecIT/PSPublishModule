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
            ["cooldownMilliseconds"] = suite.CooldownMilliseconds.ToString(CultureInfo.InvariantCulture),
            ["outlierMode"] = suite.OutlierMode.ToString(),
            ["runMode"] = suite.RunMode
        };
        AddMetadata(metadata, "gitSha", ReadGitValue("rev-parse HEAD"));
        AddMetadata(metadata, "gitBranch", ReadGitValue("branch --show-current"));
        return metadata;
    }

    private static void AddMetadata(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            metadata[key] = value!.Trim();
    }

    private static string PSVersionInfo()
        => Convert.ToString(PSObject.AsPSObject(typeof(PSObject).Assembly.GetName().Version).BaseObject, CultureInfo.InvariantCulture) ?? string.Empty;

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

    private static string? ReadGitValue(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
