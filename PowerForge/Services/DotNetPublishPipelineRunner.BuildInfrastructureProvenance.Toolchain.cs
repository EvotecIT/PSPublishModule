using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static bool TryResolveTrustedBuildTool(string toolName, out string path)
    {
        IEnumerable<string> candidates = toolName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? EnumerateDotNetCandidates()
            : toolName.Equals("git", StringComparison.OrdinalIgnoreCase)
                ? EnumerateGitCandidates()
                : Array.Empty<string>();
        var seen = new HashSet<string>(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string candidate in candidates)
        {
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                if (!seen.Add(fullPath) ||
                    !File.Exists(fullPath) ||
                    (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0 ||
                    !HasSinglePhysicalLink(fullPath) ||
                    (toolName.Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
                     !IsUsableDotNetInstallation(fullPath)))
                {
                    continue;
                }
                path = fullPath;
                return true;
            }
            catch
            {
                // An unprovable tool path cannot be used for provenance work.
            }
        }

        path = string.Empty;
        return false;
    }

    private static Dictionary<string, string?> CreateTrustedGitEnvironment(
        IReadOnlyDictionary<string, string?>? requestedEnvironment = null,
        IReadOnlyList<KeyValuePair<string, string>>? controlledConfiguration = null)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (requestedEnvironment is not null)
        {
            foreach (KeyValuePair<string, string?> variable in requestedEnvironment)
                environment[variable.Key] = variable.Value;
        }

        foreach (object key in Environment.GetEnvironmentVariables().Keys)
        {
            if (key is string name && name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
                environment[name] = null;
        }
        foreach (string name in environment.Keys
                     .Where(name => name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            environment[name] = null;
        }

        string nullDevice = IsWindows() ? "NUL" : "/dev/null";
        environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        environment["GIT_CONFIG_GLOBAL"] = nullDevice;
        var configuration = new List<KeyValuePair<string, string>>
        {
            new("core.hooksPath", nullDevice),
            new("core.fsmonitor", "false")
        };
        if (controlledConfiguration is not null)
            configuration.AddRange(controlledConfiguration);
        environment["GIT_CONFIG_COUNT"] = configuration.Count.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        for (int index = 0; index < configuration.Count; index++)
        {
            environment["GIT_CONFIG_KEY_" + index] = configuration[index].Key;
            environment["GIT_CONFIG_VALUE_" + index] = configuration[index].Value;
        }
        environment["GIT_TERMINAL_PROMPT"] = "0";
        environment["GCM_INTERACTIVE"] = "Never";
        environment["GIT_OPTIONAL_LOCKS"] = "0";
        return environment;
    }

    private static IEnumerable<string> EnumerateDotNetCandidates()
    {
        string executableName = IsWindows() ? "dotnet.exe" : "dotnet";
        string? processPath = null;
        try
        {
            processPath = Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            // The active runtime directory and configured installation roots remain available.
        }
        if (!string.IsNullOrWhiteSpace(processPath) &&
            Path.GetFileName(processPath!).Equals(executableName, StringComparison.OrdinalIgnoreCase))
        {
            yield return processPath!;
        }

        string? runtimeDirectory = null;
        try
        {
            runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        }
        catch
        {
            // A non-dotnet runtime has no active runtime directory to contribute.
        }
        string? runtimeRoot = string.IsNullOrWhiteSpace(runtimeDirectory)
            ? null
            : TryGetDotNetRootFromRuntimeDirectory(runtimeDirectory!);
        if (!string.IsNullOrWhiteSpace(runtimeRoot))
            yield return Path.Combine(runtimeRoot!, executableName);

        if (IsWindows())
        {
            string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
                yield return Path.Combine(programFiles!, "dotnet", "dotnet.exe");
            string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
                yield return Path.Combine(programFilesX86!, "dotnet", "dotnet.exe");
            yield break;
        }

        yield return "/usr/lib/dotnet/dotnet";
        yield return "/usr/share/dotnet/dotnet";
        yield return "/usr/local/share/dotnet/dotnet";
        yield return "/usr/local/share/dotnet/x64/dotnet";
        yield return "/opt/homebrew/share/dotnet/dotnet";
    }

    internal static string? TryGetDotNetRootFromRuntimeDirectory(string runtimeDirectory)
    {
        try
        {
            DirectoryInfo? directory = new(runtimeDirectory);
            while (directory is not null)
            {
                if (directory.Name.Equals("shared", StringComparison.OrdinalIgnoreCase))
                    return directory.Parent?.FullName;
                directory = directory.Parent;
            }
        }
        catch
        {
            // A non-dotnet runtime has no active dotnet installation to contribute.
        }
        return null;
    }

    private static bool IsUsableDotNetInstallation(string executablePath)
    {
        try
        {
            string root = Path.GetDirectoryName(executablePath)!;
            return Directory.Exists(Path.Combine(root, "host", "fxr")) &&
                   Directory.Exists(Path.Combine(root, "shared", "Microsoft.NETCore.App")) &&
                   Directory.Exists(Path.Combine(root, "sdk"));
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateGitCandidates()
    {
        if (IsWindows())
        {
            string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles!, "Git", "mingw64", "bin", "git.exe");
                yield return Path.Combine(programFiles!, "Git", "bin", "git.exe");
                yield return Path.Combine(programFiles!, "Git", "cmd", "git.exe");
            }
            string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
                yield return Path.Combine(programFilesX86!, "Git", "cmd", "git.exe");
            yield break;
        }

        yield return "/usr/bin/git";
        yield return "/usr/local/bin/git";
        yield return "/opt/homebrew/bin/git";
    }

}
