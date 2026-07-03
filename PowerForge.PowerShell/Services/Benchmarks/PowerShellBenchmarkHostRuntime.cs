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
            var pathPwsh = ResolveExecutableFromPath("pwsh");
            if (pathPwsh is not null)
                return pathPwsh;
            if (IsPwshExecutable(current) && !IsWindowsAppsPath(current))
                return current!;
            throw new InvalidOperationException($"Benchmark host '{host}' requires PowerShell 7, but neither '{programFilesPwsh}' nor pwsh on PATH was found.");
        }

        if (File.Exists(host))
            return host;

        throw new InvalidOperationException($"Benchmark host '{host}' is not a known PowerShell host. Use Core, Desktop, Current, or a full pwsh/pwsh.exe/powershell/powershell.exe path.");
    }

    internal static bool IsDesktopExecutable(string executable)
        => IsExecutableName(executable, "powershell", "powershell.exe");

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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var baseCandidate in GetHostFolderCandidates(currentAssemblyPath, desktop).Append(currentAssemblyPath))
        {
            foreach (var candidate in GetFrameworkCandidates(baseCandidate, targetFramework).Append(baseCandidate))
            {
                if (seen.Add(candidate))
                    yield return candidate;
            }
        }

        if (seen.Add(currentAssemblyPath))
            yield return currentAssemblyPath;
    }

    private static IEnumerable<string> GetHostFolderCandidates(string currentAssemblyPath, bool desktop)
    {
        if (desktop)
        {
            foreach (var candidate in ReplaceSegments(currentAssemblyPath, "Core", "Default"))
                yield return candidate;
            foreach (var candidate in ReplaceSegments(currentAssemblyPath, "Standard", "Default"))
                yield return candidate;
            yield break;
        }

        foreach (var candidate in ReplaceSegments(currentAssemblyPath, "Default", "Standard"))
            yield return candidate;
        foreach (var candidate in ReplaceSegments(currentAssemblyPath, "Default", "Core"))
            yield return candidate;
    }

    private static IEnumerable<string> GetFrameworkCandidates(string currentAssemblyPath, string targetFramework)
    {
        foreach (var candidate in ReplaceSegments(currentAssemblyPath, "net10.0", targetFramework))
            yield return candidate;
        foreach (var candidate in ReplaceSegments(currentAssemblyPath, "net8.0", targetFramework))
            yield return candidate;
        foreach (var candidate in ReplaceSegments(currentAssemblyPath, "net472", targetFramework))
            yield return candidate;
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
        return IsExecutableName(path, "pwsh", "pwsh.exe", "powershell", "powershell.exe");
    }

    internal static bool IsPwshExecutable(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && IsExecutableName(path, "pwsh", "pwsh.exe");

    internal static string? ResolveExecutableFromPath(params string[] fileNames)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var fileName in ExpandExecutableNames(fileNames))
            {
                try
                {
                    var candidate = Path.Combine(directory, fileName);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> ExpandExecutableNames(IEnumerable<string> fileNames)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileName in fileNames)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                continue;
            if (seen.Add(fileName))
                yield return fileName;
            if (Path.GetExtension(fileName).Length > 0)
                continue;
            foreach (var extension in GetPathExtensions())
            {
                var expanded = fileName + extension;
                if (seen.Add(expanded))
                    yield return expanded;
            }
        }
    }

    private static string[] GetPathExtensions()
    {
        if (Path.DirectorySeparatorChar != '\\')
            return Array.Empty<string>();
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        return string.IsNullOrWhiteSpace(pathExt)
            ? new[] { ".EXE", ".CMD", ".BAT", ".COM" }
            : pathExt.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsExecutableName(string? path, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var fileName = Path.GetFileName(path);
        return names.Any(name => string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWindowsAppsPath(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
}
