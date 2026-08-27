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
                     (!IsUsableDotNetInstallation(fullPath) ||
                      !IsIndependentlyTrustedDotNetExecutable(fullPath))))
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

    internal static Dictionary<string, string?> CreateTrustedGitEnvironment(
        IReadOnlyDictionary<string, string?>? requestedEnvironment = null,
        IReadOnlyList<KeyValuePair<string, string>>? controlledConfiguration = null,
        string? controlledIndexFile = null)
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
        foreach (object key in Environment.GetEnvironmentVariables().Keys)
        {
            if (key is string name && IsNativeLoaderInjectionEnvironmentVariable(name))
                environment[name] = null;
        }
        foreach (string name in environment.Keys
                     .Where(IsNativeLoaderInjectionEnvironmentVariable)
                     .ToArray())
        {
            environment[name] = null;
        }

        string nullDevice = IsWindows() ? "NUL" : "/dev/null";
        environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        environment["GIT_ATTR_NOSYSTEM"] = "1";
        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        environment["GIT_CONFIG_GLOBAL"] = nullDevice;
        var configuration = new List<KeyValuePair<string, string>>
        {
            new("core.attributesFile", nullDevice),
            new("core.autocrlf", "false"),
            new("core.eol", "lf"),
            new("core.hooksPath", nullDevice),
            new("core.fsmonitor", "false"),
            new("core.safecrlf", "false")
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
        if (!string.IsNullOrWhiteSpace(controlledIndexFile))
            environment["GIT_INDEX_FILE"] = Path.GetFullPath(controlledIndexFile!);
        environment["GIT_TERMINAL_PROMPT"] = "0";
        environment["GCM_INTERACTIVE"] = "Never";
        environment["GIT_OPTIONAL_LOCKS"] = "0";
        return environment;
    }

    private static bool IsNativeLoaderInjectionEnvironmentVariable(string name)
        => name.StartsWith("LD_", StringComparison.Ordinal) ||
           name.StartsWith("DYLD_", StringComparison.Ordinal) ||
           name.Equals("LIBPATH", StringComparison.Ordinal) ||
           name.Equals("SHLIB_PATH", StringComparison.Ordinal);

    private static IEnumerable<string> EnumerateDotNetCandidates()
    {
        string executableName = IsWindows() ? "dotnet.exe" : "dotnet";
        string? configuredPath = Environment.GetEnvironmentVariable("POWERFORGE_DOTNET_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && Path.IsPathRooted(configuredPath))
            yield return configuredPath!;

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
            string hostFxrRoot = Path.Combine(root, "host", "fxr");
            string runtimeRoot = Path.Combine(root, "shared", "Microsoft.NETCore.App");
            string sdkRoot = Path.Combine(root, "sdk");
            string hostFxrName = IsWindows()
                ? "hostfxr.dll"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "libhostfxr.dylib"
                    : "libhostfxr.so";
            string coreClrName = IsWindows()
                ? "coreclr.dll"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "libcoreclr.dylib"
                    : "libcoreclr.so";
            return Directory.Exists(hostFxrRoot) &&
                   Directory.Exists(runtimeRoot) &&
                   Directory.Exists(sdkRoot) &&
                   Directory.EnumerateFiles(hostFxrRoot, hostFxrName, SearchOption.AllDirectories).Any() &&
                   Directory.EnumerateFiles(runtimeRoot, coreClrName, SearchOption.AllDirectories).Any() &&
                   Directory.EnumerateFiles(sdkRoot, "MSBuild.dll", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsIndependentlyTrustedDotNetExecutable(string executablePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(executablePath);
            if (IsWindows())
            {
                DotNetPublishReleaseArtifactVerifier.AuthenticodeResult signature =
                    DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode(fullPath);
                return signature.IsValid &&
                       signature.Subject.IndexOf("CN=.NET", StringComparison.OrdinalIgnoreCase) >= 0 &&
                       signature.Subject.IndexOf("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return EnumeratePlatformDotNetCandidates()
                .Select(Path.GetFullPath)
                .Contains(fullPath, StringComparer.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumeratePlatformDotNetCandidates()
    {
        string executableName = IsWindows() ? "dotnet.exe" : "dotnet";
        string? processPath = null;
        try
        {
            processPath = Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            // Fixed installation roots remain available.
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
            // Fixed installation roots remain available.
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

    private static IEnumerable<string> EnumerateGitCandidates()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("POWERFORGE_GIT_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && Path.IsPathRooted(configuredPath))
            yield return configuredPath!;

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
