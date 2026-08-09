using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

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

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return SkippedStep(id, title, "SDK pruning is supported only on Linux runners.");

        if (string.IsNullOrWhiteSpace(dotNetRootPath))
            return SkippedStep(id, title, "DOTNET_ROOT is not configured.");

        var sdkRoot = Path.Combine(dotNetRootPath!, "sdk");
        if (!Directory.Exists(sdkRoot))
            return SkippedStep(id, title, $"SDK root not found: {sdkRoot}");

        if (!CommandExists("dotnet"))
            return SkippedStep(id, title, "dotnet is not available on PATH; active SDK cannot be protected.");

        if (!CommandExists("dpkg-query"))
            return SkippedStep(id, title, "SDK pruning currently supports Debian-family Linux runners; dpkg-query is unavailable.");

        var versionProbe = RunProcess("dotnet", new[] { "--version" }, activeSdkProbePath);
        var activeVersion = versionProbe.ExitCode == 0 ? versionProbe.StdOut.Trim() : string.Empty;
        if (!Version.TryParse(activeVersion, out _))
            return SkippedStep(id, title, "Unable to resolve a stable active dotnet SDK version; no SDKs were pruned.");

        var installedDirectories = Directory.EnumerateDirectories(sdkRoot, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, GetPathStringComparer())
            .ToArray();
        var protectedDirectories = new HashSet<string>(GetPathStringComparer());

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
