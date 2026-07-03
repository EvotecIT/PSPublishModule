using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;

namespace PowerForge;

internal static class PowerShellBenchmarkHostRuntime
{
    internal static string GetCurrentHostLabel()
    {
        var edition = Convert.ToString(PSVersionInfoValue("PSEdition"), CultureInfo.InvariantCulture);
        var version = Convert.ToString(PSVersionInfoValue("PSVersion"), CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(edition))
            edition = "PowerShell";
        return string.IsNullOrWhiteSpace(version) ? edition! : string.Concat(edition, "-", version);
    }

    internal static bool IsCurrentHost(string? host, string currentHostLabel)
        => string.IsNullOrWhiteSpace(host)
           || string.Equals(host, "Current", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "CurrentHost", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, currentHostLabel, StringComparison.OrdinalIgnoreCase)
           || IsEditionAlias(host, currentHostLabel);

    internal static string NormalizeCurrentHost(string? host, string currentHostLabel)
        => string.IsNullOrWhiteSpace(host)
           || string.Equals(host, "Current", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "CurrentHost", StringComparison.OrdinalIgnoreCase)
           || IsEditionAlias(host, currentHostLabel)
            ? currentHostLabel
            : host!;

    internal static string[] GetRequestedHosts(PowerShellBenchmarkSuite suite)
        => suite.Axes.FirstOrDefault(axis => string.Equals(axis.Name, "Host", StringComparison.OrdinalIgnoreCase))
               ?.Values
               .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture))
               .Where(value => !string.IsNullOrWhiteSpace(value))
               .Select(value => value!)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .ToArray()
           ?? Array.Empty<string>();

    internal static bool RequiresOutOfProcessHost(PowerShellBenchmarkSuite suite)
    {
        var hosts = GetRequestedHosts(suite);
        if (hosts.Length == 0)
            return false;
        var current = GetCurrentHostLabel();
        return hosts.Any(host => !IsCurrentHost(host, current));
    }

    internal static string ResolveExecutable(string host)
    {
        var current = TryGetCurrentExecutable();
        if (IsCurrentHost(host, GetCurrentHostLabel()) && IsPowerShellExecutable(current) && !IsWindowsAppsPath(current))
            return current!;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesPwsh = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        var windowsPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (IsDesktopHost(host))
        {
            if (File.Exists(windowsPowerShell))
                return windowsPowerShell;
            throw new InvalidOperationException($"Benchmark host '{host}' requires Windows PowerShell, but '{windowsPowerShell}' was not found.");
        }

        if (IsCoreHost(host) || IsCurrentHost(host, GetCurrentHostLabel()))
        {
            if (File.Exists(programFilesPwsh))
                return programFilesPwsh;
            if (IsPwshExecutable(current) && !IsWindowsAppsPath(current))
                return current!;
            throw new InvalidOperationException($"Benchmark host '{host}' requires PowerShell 7, but '{programFilesPwsh}' was not found.");
        }

        if (File.Exists(host))
            return host;

        throw new InvalidOperationException($"Benchmark host '{host}' is not a known PowerShell host. Use Core, Desktop, Current, or a full pwsh.exe/powershell.exe path.");
    }

    internal static bool IsDesktopExecutable(string executable)
        => string.Equals(Path.GetFileName(executable), "powershell.exe", StringComparison.OrdinalIgnoreCase);

    internal static string ResolveAssemblyForHost(string currentAssemblyPath, string executable)
    {
        var target = IsDesktopExecutable(executable) ? "net472" : "net8.0";
        foreach (var candidate in GetAssemblyCandidates(currentAssemblyPath, target, IsDesktopExecutable(executable)))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return currentAssemblyPath;
    }

    private static IEnumerable<string> GetAssemblyCandidates(string currentAssemblyPath, string targetFramework, bool desktop)
    {
        foreach (var candidate in ReplaceSegments(currentAssemblyPath, desktop ? "Core" : "Default", desktop ? "Default" : "Core"))
            yield return candidate;
        foreach (var candidate in ReplaceSegments(currentAssemblyPath, "net10.0", targetFramework))
            yield return candidate;
        foreach (var candidate in ReplaceSegments(currentAssemblyPath, "net8.0", targetFramework))
            yield return candidate;
        foreach (var candidate in ReplaceSegments(currentAssemblyPath, "net472", targetFramework))
            yield return candidate;
        yield return currentAssemblyPath;
    }

    private static IEnumerable<string> ReplaceSegments(string path, string oldSegment, string newSegment)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var changed = false;
        for (var i = 0; i < parts.Length; i++)
        {
            if (string.Equals(parts[i], oldSegment, StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = newSegment;
                changed = true;
            }
        }

        if (changed)
            yield return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
    }

    private static object? PSVersionInfoValue(string name)
    {
        using var ps = PowerShell.Create();
        return ps.AddScript($"$PSVersionTable.{name}").Invoke().FirstOrDefault()?.BaseObject;
    }

    private static string? TryGetCurrentExecutable()
    {
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsEditionAlias(string? host, string currentHostLabel)
        => IsCoreHost(host) && currentHostLabel.StartsWith("Core-", StringComparison.OrdinalIgnoreCase)
           || IsDesktopHost(host) && currentHostLabel.StartsWith("Desktop-", StringComparison.OrdinalIgnoreCase);

    private static bool IsCoreHost(string? host)
        => string.Equals(host, "Core", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "PS7", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "PowerShell7", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "pwsh", StringComparison.OrdinalIgnoreCase);

    private static bool IsDesktopHost(string? host)
        => string.Equals(host, "Desktop", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "PS5", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "PS5.1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "WindowsPowerShell", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "powershell", StringComparison.OrdinalIgnoreCase);

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
}
