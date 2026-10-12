using System.Runtime.InteropServices;
using NuGet.Common;

namespace PowerForge;

/// <summary>Owns the explicit child environment for provenance-bound SDK discovery and trimmed publication.</summary>
internal static class PowerShellCompilationSdkProcessEnvironment
{
    private static readonly string[] ForwardedVariables =
    {
        "SystemRoot", "WINDIR", "PATHEXT", "PROCESSOR_ARCHITECTURE", "PROCESSOR_ARCHITEW6432",
        "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "LANG", "LC_ALL", "LC_CTYPE",
        "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy"
    };

    /// <summary>Uses the host belonging to the fingerprinted SDK, with no inherited MSBuild or runtime injection inputs.</summary>
    internal static ProcessRunRequest CreateRequest(PowerShellCompilationSdkSelection sdk, string workingDirectory,
        IReadOnlyList<string> arguments, TimeSpan timeout, string? packageRoot = null)
    {
        var dotnetRoot = Directory.GetParent(Directory.GetParent(sdk.RootPath)!.FullName)!.FullName;
        var windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var host = Path.Combine(dotnetRoot, windows ? "dotnet.exe" : "dotnet");
        if (!File.Exists(host))
            throw new InvalidOperationException("The selected SDK has no matching dotnet host: " + host);
        var environment = CreateEnvironment(Path.Combine(workingDirectory, "producer-environment"), packageRoot);
        environment["PATH"] = windows
            ? string.Join(Path.PathSeparator.ToString(), dotnetRoot, Environment.SystemDirectory,
                Directory.GetParent(Environment.SystemDirectory)!.FullName)
            : dotnetRoot + Path.PathSeparator + "/usr/bin:/bin:/usr/sbin:/sbin";
        environment["DOTNET_ROOT"] = dotnetRoot;
        environment["DOTNET_HOST_PATH"] = host;
        // Automatic response files can import caller targets from above the owned
        // project directory even when the process environment is explicit.
        if (arguments.Count > 0 && (arguments[0] == "restore" || arguments[0] == "publish"))
            arguments = arguments.Concat(new[] { "-noAutoResponse" }).ToArray();
        return Create(host, workingDirectory, arguments, timeout, environment);
    }

    /// <summary>Preserves PATH-based SDK selection while removing ambient resolver, MSBuild, and CLR startup overrides.</summary>
    internal static ProcessRunRequest CreateDiscoveryRequest(string workingDirectory, string environmentDirectory,
        IReadOnlyList<string> arguments)
    {
        var environment = CreateEnvironment(environmentDirectory, packageRoot: null);
        environment["PATH"] = Environment.GetEnvironmentVariable("PATH");
        return Create("dotnet", workingDirectory, arguments, TimeSpan.FromSeconds(30), environment);
    }

    private static ProcessRunRequest Create(string executable, string workingDirectory, IReadOnlyList<string> arguments,
        TimeSpan timeout, IReadOnlyDictionary<string, string?> environment)
        => new(executable, workingDirectory, arguments, timeout, environment,
            captureOutput: true, captureError: true, inheritEnvironment: false);

    private static Dictionary<string, string?> CreateEnvironment(string root, string? packageRoot)
    {
        // The fresh home/config roots also prevent MSBuild's normal per-user wildcard
        // imports and NuGet plugin discovery from finding unrecorded build code.
        var home = Directory.CreateDirectory(Path.Combine(root, "home")).FullName;
        var config = Directory.CreateDirectory(Path.Combine(root, "config")).FullName;
        var cache = Directory.CreateDirectory(Path.Combine(root, "cache")).FullName;
        var temporary = Directory.CreateDirectory(Path.Combine(root, "temp")).FullName;
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in ForwardedVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value)) environment[name] = value;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            environment["ComSpec"] = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        environment["HOME"] = home;
        environment["USERPROFILE"] = home;
        environment["DOTNET_CLI_HOME"] = home;
        environment["APPDATA"] = config;
        environment["LOCALAPPDATA"] = cache;
        environment["XDG_CONFIG_HOME"] = config;
        environment["XDG_CACHE_HOME"] = cache;
        environment["XDG_DATA_HOME"] = cache;
        environment["TEMP"] = temporary;
        environment["TMP"] = temporary;
        environment["TMPDIR"] = temporary;
        environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false";
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        environment["MSBUILDDISABLENODEREUSE"] = "1";
        environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        environment["UseSharedCompilation"] = "false";
        environment["MSBuildEnableWorkloadResolver"] = "false";
        environment["MSBuildUserExtensionsPath"] = Path.Combine(config, "MSBuild");
        environment["ImportUserLocationsByWildcardBeforeMicrosoftCommonProps"] = "false";
        environment["ImportUserLocationsByWildcardAfterMicrosoftCommonProps"] = "false";
        environment["ImportUserLocationsByWildcardBeforeMicrosoftCommonTargets"] = "false";
        environment["ImportUserLocationsByWildcardAfterMicrosoftCommonTargets"] = "false";
        var packages = packageRoot ?? Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packages))
            packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        environment["NUGET_PACKAGES"] = Path.GetFullPath(packages!.Trim().Trim('"'));
        // Resolve defaults in the parent before overriding TEMP/home: a shared
        // package cache must retain the same ordinary NuGet locking directory.
        environment["NUGET_SCRATCH"] = Environment.GetEnvironmentVariable("NUGET_SCRATCH") ??
            NuGetEnvironment.GetFolderPath(NuGetFolderPath.Temp);
        environment["NUGET_HTTP_CACHE_PATH"] = Environment.GetEnvironmentVariable("NUGET_HTTP_CACHE_PATH") ??
            NuGetEnvironment.GetFolderPath(NuGetFolderPath.HttpCacheDirectory);
        return environment;
    }
}
