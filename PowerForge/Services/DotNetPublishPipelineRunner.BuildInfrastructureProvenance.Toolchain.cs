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
        foreach (string candidate in candidates)
        {
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                if (!File.Exists(fullPath) ||
                    (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0 ||
                    !HasSinglePhysicalLink(fullPath))
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
        IReadOnlyDictionary<string, string?>? requestedEnvironment = null)
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
        environment["GIT_CONFIG_COUNT"] = "2";
        environment["GIT_CONFIG_KEY_0"] = "core.hooksPath";
        environment["GIT_CONFIG_VALUE_0"] = nullDevice;
        environment["GIT_CONFIG_KEY_1"] = "core.fsmonitor";
        environment["GIT_CONFIG_VALUE_1"] = "false";
        environment["GIT_TERMINAL_PROMPT"] = "0";
        environment["GCM_INTERACTIVE"] = "Never";
        environment["GIT_OPTIONAL_LOCKS"] = "0";
        return environment;
    }

    private static IEnumerable<string> EnumerateDotNetCandidates()
    {
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
