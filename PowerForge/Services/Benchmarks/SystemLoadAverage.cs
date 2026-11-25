using System.Globalization;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Reads the operating system's 15-minute load average on Linux and macOS.
/// </summary>
internal static class SystemLoadAverage
{
    /// <summary>True on platforms that report a load average (Linux and macOS).</summary>
    internal static bool IsSupported
        => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Returns the 15-minute load average, or null when the platform has none (Windows) or this reading failed.
    /// </summary>
    internal static async Task<double?> ReadFifteenMinuteAsync(IProcessRunner processRunner, CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            const string path = "/proc/loadavg";
            try
            {
                return ParseLinux(File.ReadAllText(path));
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var result = await processRunner.RunAsync(
                new ProcessRunRequest("/usr/sbin/sysctl", "/", new[] { "-n", "vm.loadavg" }, TimeSpan.FromSeconds(30)),
                cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? ParseSysctl(result.StdOut) : null;
        }

        return null;
    }

    /// <summary>Parses <c>/proc/loadavg</c>: <c>0.52 0.58 0.59 1/389 12345</c>.</summary>
    internal static double? ParseLinux(string text)
        => Field(text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries), 2);

    /// <summary>Parses <c>sysctl -n vm.loadavg</c>: <c>{ 1.92 2.10 2.31 }</c>.</summary>
    internal static double? ParseSysctl(string text)
        => Field(text.Split(new[] { ' ', '\t', '\n', '{', '}' }, StringSplitOptions.RemoveEmptyEntries), 2);

    private static double? Field(string[] fields, int index)
        => fields.Length > index && double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
}
