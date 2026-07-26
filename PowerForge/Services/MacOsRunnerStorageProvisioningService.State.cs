using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

#if !NET472
namespace PowerForge;

public sealed partial class MacOsRunnerStorageProvisioningService
{
    private ProvisioningPaths Normalize(MacOsRunnerStorageProvisioningSpec spec)
    {
        var runnerRoot = RequireAbsoluteExistingDirectory(spec.RunnerRootPath, nameof(spec.RunnerRootPath));
        var runnerConfig = Path.Combine(runnerRoot, ".runner");
        if (!File.Exists(runnerConfig))
            throw new InvalidOperationException($"GitHub Actions runner configuration was not found: {runnerConfig}");
        if (!File.Exists(Path.Combine(runnerRoot, "runsvc.sh")))
            throw new InvalidOperationException($"GitHub Actions runner service entrypoint was not found under: {runnerRoot}");

        var stateRoot = _requireExternalPath(spec.StateRootPath, nameof(spec.StateRootPath));
        var workRoot = _requireExternalPath(spec.WorkRootPath, nameof(spec.WorkRootPath));
        foreach (var externalRoot in new[] { stateRoot, workRoot }.Distinct(StringComparer.Ordinal))
            _validateExternalStorage(externalRoot);
        var stateVolumeRoot = _resolveExternalVolumeRoot(stateRoot);
        var workVolumeRoot = _resolveExternalVolumeRoot(workRoot);
        if (!PathsEqual(stateVolumeRoot, workVolumeRoot))
        {
            throw new InvalidOperationException(
                "Runner state and work roots must use the same external APFS volume so the service can verify one stable volume identity.");
        }
        EnsureNoSymbolicLinkComponents(stateVolumeRoot, stateRoot);
        EnsureNoSymbolicLinkComponents(workVolumeRoot, workRoot);
        var externalVolumeUuid = _resolveExternalVolumeUuid(stateVolumeRoot);
        var home = Path.GetFullPath(_homePath());
        var coreSimulator = string.IsNullOrWhiteSpace(spec.CoreSimulatorPath)
            ? Path.Combine(home, "Library", "Developer", "CoreSimulator")
            : Path.GetFullPath(spec.CoreSimulatorPath);
        var launchAgent = ResolveLaunchAgentPath(spec.LaunchAgentPath, runnerRoot);
        if (!File.Exists(launchAgent))
            throw new InvalidOperationException($"Runner LaunchAgent was not found: {launchAgent}");
        ValidateLaunchAgentOwnership(launchAgent, runnerRoot);
        if (spec.CoreSimulatorImageSizeGb < 20)
            throw new ArgumentOutOfRangeException(nameof(spec.CoreSimulatorImageSizeGb), "CoreSimulator image size must be at least 20 GiB.");
        if (spec.ExternalStorageWaitSeconds is < 0 or > 900)
            throw new ArgumentOutOfRangeException(nameof(spec.ExternalStorageWaitSeconds), "External storage wait must be between 0 and 900 seconds.");

        return new ProvisioningPaths(
            runnerRoot,
            stateRoot,
            workRoot,
            coreSimulator,
            Path.Combine(stateRoot, "CoreSimulator.sparsebundle"),
            Path.Combine(stateRoot, "caches"),
            Path.Combine(runnerRoot, ".env"),
            runnerConfig,
            Path.Combine(runnerRoot, "run-with-external-state.sh"),
            launchAgent,
            home,
            stateVolumeRoot,
            externalVolumeUuid);
    }

    private static DesiredState BuildDesiredState(ProvisioningPaths paths, MacOsRunnerStorageProvisioningSpec spec)
    {
        var cacheLinks = new[]
        {
            new CacheLink(
                "nuget-packages",
                "NuGet packages",
                Path.Combine(paths.Home, ".nuget", "packages"),
                Path.Combine(paths.CacheRoot, "nuget-packages")),
            new CacheLink(
                "playwright",
                "Playwright browsers",
                Path.Combine(paths.Home, "Library", "Caches", "ms-playwright"),
                Path.Combine(paths.CacheRoot, "ms-playwright")),
            new CacheLink(
                "swiftpm",
                "SwiftPM cache",
                Path.Combine(paths.Home, "Library", "Caches", "org.swift.swiftpm"),
                Path.Combine(paths.CacheRoot, "org.swift.swiftpm"))
        };

        var environmentContent = BuildEnvironmentContent(
            paths.EnvironmentPath,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NUGET_PACKAGES"] = cacheLinks[0].Target,
                ["PLAYWRIGHT_BROWSERS_PATH"] = cacheLinks[1].Target
            });

        var relativeWork = Path.GetRelativePath(paths.RunnerRoot, paths.WorkRoot)
            .Replace(Path.DirectorySeparatorChar, '/');
        return new DesiredState(
            spec.CoreSimulatorImageSizeGb,
            relativeWork,
            environmentContent,
            BuildWrapper(paths, spec.ExternalStorageWaitSeconds),
            cacheLinks);
    }

    private static string BuildWrapper(ProvisioningPaths paths, int waitSeconds)
    {
        var attempts = waitSeconds == 0 ? 1 : Math.Max(1, (int)Math.Ceiling(waitSeconds / 2d));
        var builder = new StringBuilder();
        builder.AppendLine("#!/bin/bash");
        builder.AppendLine();
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine();
        builder.AppendLine("runner_root=" + BashQuote(paths.RunnerRoot));
        builder.AppendLine("state_root=" + BashQuote(paths.StateRoot));
        builder.AppendLine("work_root=" + BashQuote(paths.WorkRoot));
        builder.AppendLine("external_volume_root=" + BashQuote(paths.ExternalVolumeRoot));
        builder.AppendLine("external_volume_uuid=" + BashQuote(paths.ExternalVolumeUuid));
        builder.AppendLine("core_simulator_image=" + BashQuote(paths.CoreSimulatorImage));
        builder.AppendLine("core_simulator_mount=" + BashQuote(paths.CoreSimulatorMount));
        builder.AppendLine();
        builder.AppendLine("external_storage_ready() {");
        builder.AppendLine("    [[ -d \"${state_root}\" && -d \"${work_root}\" ]] || return 1");
        builder.AppendLine("    actual_uuid=$(/usr/sbin/diskutil info -plist \"${external_volume_root}\" | \\");
        builder.AppendLine("        /usr/bin/plutil -extract VolumeUUID raw -o - - 2>/dev/null) || return 1");
        builder.AppendLine("    [[ \"${actual_uuid}\" == \"${external_volume_uuid}\" ]]");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("is_core_simulator_mounted() {");
        builder.AppendLine("    /usr/bin/hdiutil info | /usr/bin/awk -v image=\"${core_simulator_image}\" -v mount=\"${core_simulator_mount}\" '");
        builder.AppendLine("        /^image-path[[:space:]]*:/ {");
        builder.AppendLine("            value = $0");
        builder.AppendLine("            sub(/^[^:]*:[[:space:]]*/, \"\", value)");
        builder.AppendLine("            matches_image = (value == image)");
        builder.AppendLine("            next");
        builder.AppendLine("        }");
        builder.AppendLine("        matches_image && index($0, \"\\t\" mount) > 0 { found = 1; exit }");
        builder.AppendLine("        END { exit(found ? 0 : 1) }");
        builder.AppendLine("    '");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine($"for _ in $(/usr/bin/seq 1 {attempts}); do");
        builder.AppendLine("    external_storage_ready && break");
        builder.AppendLine("    /bin/sleep 2");
        builder.AppendLine("done");
        builder.AppendLine();
        builder.AppendLine("if ! external_storage_ready; then");
        builder.AppendLine("    echo \"Expected external runner storage is unavailable or has the wrong volume identity; refusing to start.\" >&2");
        builder.AppendLine("    exit 1");
        builder.AppendLine("fi");
        builder.AppendLine();
        builder.AppendLine("if ! is_core_simulator_mounted; then");
        builder.AppendLine("    /bin/mkdir -p \"${core_simulator_mount}\"");
        builder.AppendLine("    if [[ -n \"$(/usr/bin/find \"${core_simulator_mount}\" -mindepth 1 -maxdepth 1 -print -quit)\" ]]; then");
        builder.AppendLine("        echo \"CoreSimulator mount point is not empty; refusing to hide local data.\" >&2");
        builder.AppendLine("        exit 1");
        builder.AppendLine("    fi");
        builder.AppendLine();
        builder.AppendLine("    /usr/bin/hdiutil attach \"${core_simulator_image}\" \\");
        builder.AppendLine("        -mountpoint \"${core_simulator_mount}\" \\");
        builder.AppendLine("        -nobrowse \\");
        builder.AppendLine("        -owners on");
        builder.AppendLine("fi");
        builder.AppendLine();
        builder.AppendLine("exec \"${runner_root}/runsvc.sh\"");
        return builder.ToString();
    }

    private static string BuildEnvironmentContent(
        string path,
        IReadOnlyDictionary<string, string> requiredValues)
    {
        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();
        var remaining = new HashSet<string>(requiredValues.Keys, StringComparer.Ordinal);
        for (var index = 0; index < lines.Count; index++)
        {
            var separator = lines[index].IndexOf('=');
            if (separator <= 0)
                continue;
            var key = lines[index][..separator];
            if (!requiredValues.TryGetValue(key, out var value))
                continue;
            lines[index] = key + "=" + value;
            remaining.Remove(key);
        }
        foreach (var key in remaining.OrderBy(value => value, StringComparer.Ordinal))
            lines.Add(key + "=" + requiredValues[key]);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string ReadRunnerWorkFolder(string runnerConfigPath)
    {
        if (!File.Exists(runnerConfigPath))
            return string.Empty;
        var node = JsonNode.Parse(File.ReadAllText(runnerConfigPath)) as JsonObject;
        return node?["workFolder"]?.GetValue<string>() ?? string.Empty;
    }

    private static void WriteRunnerConfig(string runnerConfigPath, string relativeWorkRoot)
    {
        var node = JsonNode.Parse(File.ReadAllText(runnerConfigPath)) as JsonObject
                   ?? throw new InvalidOperationException($"Runner configuration is not a JSON object: {runnerConfigPath}");
        node["workFolder"] = relativeWorkRoot;
        WriteTextIfChanged(
            runnerConfigPath,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static void WriteTextIfChanged(string path, string content)
    {
        if (File.Exists(path) && StringEqualsOrdinal(File.ReadAllText(path), content))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".powerforge-" + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string ResolveLaunchAgentPath(string? explicitPath, string runnerRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        var serviceFile = Path.Combine(runnerRoot, ".service");
        if (!File.Exists(serviceFile))
            throw new InvalidOperationException(
                "Runner service metadata was not found. Provide --launch-agent explicitly.");
        var value = File.ReadLines(serviceFile).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Runner service metadata is empty: {serviceFile}");
        return Path.GetFullPath(value);
    }

    private static string RequireAbsoluteExistingDirectory(string? value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A path is required.", parameter);
        if (!Path.IsPathFullyQualified(value))
            throw new ArgumentException("An absolute path is required.", parameter);
        var path = Path.GetFullPath(value);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        return path;
    }

    private static string RequireExternalVolumePath(string? value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A path is required.", parameter);
        if (!Path.IsPathFullyQualified(value))
            throw new ArgumentException("An absolute path is required.", parameter);
        var path = Path.GetFullPath(value);
        var parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("Volumes", StringComparison.Ordinal))
            throw new ArgumentException("The path must be inside a mounted volume under /Volumes.", parameter);
        var volumeRoot = Path.Combine(Path.DirectorySeparatorChar.ToString(), parts[0], parts[1]);
        if (!Directory.Exists(volumeRoot))
            throw new DirectoryNotFoundException($"External volume is not mounted: {volumeRoot}");
        if (PathsEqual(path, volumeRoot))
            throw new ArgumentException("Choose a runner-specific directory inside the external volume.", parameter);
        return path;
    }

    private static string GetExternalVolumeRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var parts = fullPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("Volumes", StringComparison.Ordinal))
            throw new InvalidOperationException($"Path is not on an external macOS volume: {fullPath}");
        return Path.Combine(Path.DirectorySeparatorChar.ToString(), parts[0], parts[1]);
    }

    private static void EnsureNoSymbolicLinkComponents(string volumeRoot, string path)
    {
        var root = Path.GetFullPath(volumeRoot);
        var target = Path.GetFullPath(path);
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"External path is outside the validated volume: {target}");

        var current = root;
        EnsureDirectoryIsNotLink(current);
        var relative = Path.GetRelativePath(root, target);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current))
                EnsureDirectoryIsNotLink(current);
        }
    }

    private static void EnsureDirectoryIsNotLink(string path)
    {
        var info = new DirectoryInfo(path);
        if (info.LinkTarget is not null)
            throw new InvalidOperationException($"External runner storage path contains a symbolic link: {path}");
    }

    private static MacOsRunnerStorageProvisioningStep Step(
        string id,
        string description,
        bool changed,
        params string[] paths)
        => new()
        {
            Id = id,
            Description = description,
            Changed = changed,
            Skipped = !changed,
            Paths = paths
        };

    private static string? GetSymbolicLinkTarget(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.LinkTarget is null ? null : Path.GetFullPath(Path.Combine(info.Parent!.FullName, info.LinkTarget));
        }
        catch
        {
            return null;
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.Ordinal);
    }

    private static bool StringEqualsNormalized(string? left, string? right)
        => string.Equals(
            (left ?? string.Empty).Replace('\\', '/').TrimEnd('/'),
            (right ?? string.Empty).Replace('\\', '/').TrimEnd('/'),
            StringComparison.Ordinal);

    private static bool StringEqualsOrdinal(string? left, string? right)
        => string.Equals(left, right, StringComparison.Ordinal);

    private static string BashQuote(string value)
        => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private sealed record ProvisioningPaths(
        string RunnerRoot,
        string StateRoot,
        string WorkRoot,
        string CoreSimulatorMount,
        string CoreSimulatorImage,
        string CacheRoot,
        string EnvironmentPath,
        string RunnerConfigPath,
        string WrapperPath,
        string LaunchAgentPath,
        string Home,
        string ExternalVolumeRoot,
        string ExternalVolumeUuid);

    private sealed record DesiredState(
        int ImageSizeGb,
        string RelativeWorkRoot,
        string EnvironmentContent,
        string WrapperContent,
        CacheLink[] CacheLinks);

    private sealed record CacheLink(string Id, string Description, string Source, string Target);
}
#endif
