using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PowerForge;

public sealed partial class RunnerHousekeepingService
{
    internal static string ResolveActiveSdkProbePath(string? githubWorkspace, string currentDirectory)
    {
        if (!string.IsNullOrWhiteSpace(githubWorkspace))
        {
            var resolvedWorkspace = Path.GetFullPath(githubWorkspace!);
            if (Directory.Exists(resolvedWorkspace))
                return resolvedWorkspace;
        }

        return Path.GetFullPath(currentDirectory);
    }

    private RunnerHousekeepingStepResult PruneDotNetSdks(
        string? dotNetRootPath,
        string activeSdkProbePath,
        int versionsToKeepPerMajorMinor,
        bool dryRun,
        bool allowSudo)
    {
        const string id = "dotnet-sdk-prune";
        const string title = "Prune superseded dotnet SDKs";

        var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (!isLinux && !isWindows)
            return SkippedStep(id, title, "SDK pruning is supported only on Linux and Windows runners.");

        if (string.IsNullOrWhiteSpace(dotNetRootPath))
            return SkippedStep(id, title, "DOTNET_ROOT is not configured.");

        var sdkRoot = Path.Combine(dotNetRootPath!, "sdk");
        if (!Directory.Exists(sdkRoot))
            return SkippedStep(id, title, $"SDK root not found: {sdkRoot}");

        if (!CommandExists("dotnet"))
            return SkippedStep(id, title, "dotnet is not available on PATH; active SDK cannot be protected.");

        var versionProbe = RunProcess("dotnet", new[] { "--version" }, activeSdkProbePath);
        var activeVersion = versionProbe.ExitCode == 0 ? versionProbe.StdOut.Trim() : string.Empty;
        if (!Version.TryParse(activeVersion, out _))
            return SkippedStep(id, title, "Unable to resolve a stable active dotnet SDK version; no SDKs were pruned.");

        var installedDirectories = Directory.EnumerateDirectories(sdkRoot, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, GetPathStringComparer())
            .ToArray();
        var protectedDirectories = new HashSet<string>(GetPathStringComparer());

        if (isLinux)
        {
            if (!CommandExists("dpkg-query"))
                return SkippedStep(id, title, "SDK pruning currently supports Debian-family Linux runners; dpkg-query is unavailable.");

            foreach (var directory in installedDirectories)
            {
                var markerPath = Path.Combine(directory, "dotnet.dll");
                if (!File.Exists(markerPath))
                {
                    protectedDirectories.Add(directory);
                    continue;
                }

                var ownershipProbe = RunProcess("dpkg-query", new[] { "-S", markerPath }, sdkRoot);
                if (ownershipProbe.ExitCode == 0)
                    protectedDirectories.Add(directory);
                else if (IsPackageOwnershipProbeFailureFatal(ownershipProbe.ExitCode))
                    return SkippedStep(id, title, $"dpkg-query failed with exit code {ownershipProbe.ExitCode}; no SDKs were pruned.");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                 && !TryGetWindowsPackageOwnedSdkDirectories(installedDirectories, out protectedDirectories))
        {
            return SkippedStep(id, title, "Unable to inspect registered Windows SDK packages; no SDKs were pruned.");
        }

        var targets = SelectDotNetSdkDirectoriesToPrune(
            installedDirectories,
            activeVersion,
            protectedDirectories,
            versionsToKeepPerMajorMinor);

        return DeleteTargets(
            id,
            title,
            targets,
            dryRun,
            allowSudo,
            isDirectory: true,
            allowedRootPath: sdkRoot);
    }

    /// <summary>
    /// Selects superseded stable SDK directories while preserving the active SDK, protected package-owned
    /// directories, the newest configured count in each major/minor line, and unknown or prerelease layouts.
    /// </summary>
    internal static string[] SelectDotNetSdkDirectoriesToPrune(
        IEnumerable<string> installedDirectories,
        string activeVersion,
        IEnumerable<string> protectedDirectories,
        int versionsToKeepPerMajorMinor)
    {
        if (installedDirectories is null) throw new ArgumentNullException(nameof(installedDirectories));
        if (protectedDirectories is null) throw new ArgumentNullException(nameof(protectedDirectories));

        var comparer = GetPathStringComparer();
        var protectedSet = new HashSet<string>(protectedDirectories.Select(Path.GetFullPath), comparer);
        var stable = installedDirectories
            .Select(path => new
            {
                Path = Path.GetFullPath(path),
                Name = Path.GetFileName(path),
                Parsed = TryParseStableSdkVersion(Path.GetFileName(path), out var version) ? version : null
            })
            .Where(item => item.Parsed is not null)
            .ToArray();

        var keepCount = Math.Max(1, versionsToKeepPerMajorMinor);
        foreach (var group in stable.GroupBy(item => new { item.Parsed!.Major, item.Parsed.Minor }))
        {
            foreach (var retained in group.OrderByDescending(item => item.Parsed).Take(keepCount))
                protectedSet.Add(retained.Path);
        }

        foreach (var active in stable.Where(item => string.Equals(item.Name, activeVersion, StringComparison.OrdinalIgnoreCase)))
            protectedSet.Add(active.Path);

        return stable
            .Where(item => !protectedSet.Contains(item.Path))
            .OrderBy(item => item.Parsed)
            .ThenBy(item => item.Path, comparer)
            .Select(item => item.Path)
            .ToArray();
    }

    internal static bool IsPackageOwnershipProbeFailureFatal(int exitCode) => exitCode is not (0 or 1);

#if NET8_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    private static bool TryGetWindowsPackageOwnedSdkDirectories(
        IEnumerable<string> installedDirectories,
        out HashSet<string> protectedDirectories)
    {
        protectedDirectories = new HashSet<string>(GetPathStringComparer());
        try
        {
            var displayNames = new List<string>();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                    continue;

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var subKey = uninstall.OpenSubKey(subKeyName);
                    if (subKey?.GetValue("DisplayName") is string displayName)
                        displayNames.Add(displayName);
                }
            }

            protectedDirectories = SelectWindowsPackageOwnedSdkDirectories(installedDirectories, displayNames);
            return true;
        }
        catch
        {
            protectedDirectories.Clear();
            return false;
        }
    }

    /// <summary>
    /// Maps registered Windows SDK package display names to their corresponding SDK directories.
    /// Unregistered directories installed by dotnet-install remain eligible for normal retention selection.
    /// </summary>
    internal static HashSet<string> SelectWindowsPackageOwnedSdkDirectories(
        IEnumerable<string> installedDirectories,
        IEnumerable<string> registeredDisplayNames)
    {
        if (installedDirectories is null) throw new ArgumentNullException(nameof(installedDirectories));
        if (registeredDisplayNames is null) throw new ArgumentNullException(nameof(registeredDisplayNames));

        var registeredVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var displayName in registeredDisplayNames)
        {
            if (TryParseWindowsRegisteredSdkVersion(displayName, out var version))
                registeredVersions.Add(version!);
        }

        return new HashSet<string>(
            installedDirectories
                .Where(path => registeredVersions.Contains(Path.GetFileName(path)))
                .Select(Path.GetFullPath),
            GetPathStringComparer());
    }

    /// <summary>
    /// Extracts a canonical SDK version from a Windows package display name.
    /// </summary>
    internal static bool TryParseWindowsRegisteredSdkVersion(string? displayName, out string? version)
    {
        var prefixes = new[]
        {
            "Microsoft .NET SDK ",
            "Microsoft .NET Core SDK "
        };
        version = null;
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        var normalizedDisplayName = displayName!;
        var prefix = prefixes.FirstOrDefault(candidate => normalizedDisplayName.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
        if (prefix is null)
            return false;

        var remainder = normalizedDisplayName.Substring(prefix.Length);
        var separator = remainder.IndexOf(' ');
        var candidate = separator < 0 ? remainder : remainder.Substring(0, separator);
        if (!TryParseStableSdkVersion(candidate, out _))
            return false;

        version = candidate;
        return true;
    }

    internal static bool TryParseStableSdkVersion(string? value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value!.Split('.');
        if (parts.Length != 3 || parts.Any(part => part.Length == 0 || part.Any(character => !char.IsDigit(character))))
            return false;

        if (!Version.TryParse(value, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0)
            return false;

        if (!string.Equals(parsed.ToString(3), value, StringComparison.Ordinal))
            return false;

        version = parsed;
        return true;
    }
}
