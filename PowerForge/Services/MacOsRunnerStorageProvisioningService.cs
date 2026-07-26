using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

#if !NET472
namespace PowerForge;

/// <summary>
/// Plans and applies a durable external-storage layout for a self-hosted macOS GitHub Actions runner.
/// </summary>
public sealed partial class MacOsRunnerStorageProvisioningService
{
    private readonly ILogger _logger;
    private readonly IProcessRunner _processRunner;
    private readonly Func<bool> _isMacOs;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<string> _homePath;
    private readonly Func<string?, string, string> _requireExternalPath;
    private readonly Action<string> _validateExternalStorage;
    private readonly Func<string, string> _resolveExternalVolumeRoot;
    private readonly Func<string, string> _resolveExternalVolumeUuid;

    /// <summary>
    /// Creates a provisioning service.
    /// </summary>
    public MacOsRunnerStorageProvisioningService(ILogger logger, IProcessRunner? processRunner = null)
        : this(
            logger,
            processRunner ?? new ProcessRunner(),
            OperatingSystem.IsMacOS,
            () => DateTimeOffset.UtcNow,
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            RequireExternalVolumePath,
            validateExternalStorage: null,
            resolveExternalVolumeRoot: null,
            resolveExternalVolumeUuid: null)
    {
    }

    internal MacOsRunnerStorageProvisioningService(
        ILogger logger,
        IProcessRunner processRunner,
        Func<bool> isMacOs,
        Func<DateTimeOffset> utcNow,
        Func<string> homePath,
        Func<string?, string, string> requireExternalPath,
        Action<string>? validateExternalStorage = null,
        Func<string, string>? resolveExternalVolumeRoot = null,
        Func<string, string>? resolveExternalVolumeUuid = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _isMacOs = isMacOs ?? throw new ArgumentNullException(nameof(isMacOs));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _homePath = homePath ?? throw new ArgumentNullException(nameof(homePath));
        _requireExternalPath = requireExternalPath ?? throw new ArgumentNullException(nameof(requireExternalPath));
        _validateExternalStorage = validateExternalStorage ?? ValidateExternalApfsVolume;
        _resolveExternalVolumeRoot = resolveExternalVolumeRoot ?? GetExternalVolumeRoot;
        _resolveExternalVolumeUuid = resolveExternalVolumeUuid ?? ResolveExternalVolumeUuid;
    }

    /// <summary>
    /// Plans or applies external runner work, cache, and CoreSimulator storage.
    /// </summary>
    public MacOsRunnerStorageProvisioningResult Provision(MacOsRunnerStorageProvisioningSpec spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (!_isMacOs())
            throw new PlatformNotSupportedException("macOS runner storage provisioning is available only on macOS.");

        var paths = Normalize(spec);
        var backupRoot = Path.Combine(paths.StateRoot, "backups", "runner-storage-original");
        var desired = BuildDesiredState(paths, spec);
        var steps = Plan(paths, desired, backupRoot);
        var changed = steps.Any(step => step.Changed);

        if (!spec.DryRun && changed)
        {
            EnsureRunnerStopped(paths.RunnerRoot);
            using var operationLock = AcquireOperationLock(paths.RunnerRoot);
            Apply(paths, desired, backupRoot, steps);
        }

        return new MacOsRunnerStorageProvisioningResult
        {
            RunnerRootPath = paths.RunnerRoot,
            StateRootPath = paths.StateRoot,
            WorkRootPath = paths.WorkRoot,
            ExternalVolumeRootPath = paths.ExternalVolumeRoot,
            ExternalVolumeUuid = paths.ExternalVolumeUuid,
            CoreSimulatorImagePath = paths.CoreSimulatorImage,
            CoreSimulatorMountPath = paths.CoreSimulatorMount,
            RunnerWrapperPath = paths.WrapperPath,
            BackupRootPath = backupRoot,
            DryRun = spec.DryRun,
            AlreadyConfigured = !changed,
            Steps = steps.ToArray()
        };
    }

    private List<MacOsRunnerStorageProvisioningStep> Plan(
        ProvisioningPaths paths,
        DesiredState desired,
        string backupRoot)
    {
        var steps = new List<MacOsRunnerStorageProvisioningStep>();
        var imageExists = Directory.Exists(paths.CoreSimulatorImage);
        var imageMounted = imageExists && IsImageMountedAt(paths.CoreSimulatorImage, paths.CoreSimulatorMount);
        var mountOccupied = IsMounted(paths.CoreSimulatorMount);

        steps.Add(Step(
            "directories",
            "Create runner state, work, and cache directories.",
            !Directory.Exists(paths.StateRoot)
            || !Directory.Exists(paths.WorkRoot)
            || desired.CacheLinks.Any(link => !Directory.Exists(link.Target)),
            paths.StateRoot,
            paths.WorkRoot,
            paths.CacheRoot));

        steps.Add(Step(
            "core-simulator-image",
            $"Create a {desired.ImageSizeGb} GiB APFS sparse bundle for CoreSimulator.",
            !imageExists,
            paths.CoreSimulatorImage));

        if (mountOccupied && !imageMounted)
        {
            steps.Add(new MacOsRunnerStorageProvisioningStep
            {
                Id = "core-simulator-mount",
                Description = "CoreSimulator is mounted from a different image; apply will refuse to replace it.",
                Changed = true,
                Paths = new[] { paths.CoreSimulatorMount, paths.CoreSimulatorImage }
            });
        }
        else
        {
            steps.Add(Step(
                "core-simulator-mount",
                "Migrate CoreSimulator into the sparse bundle and mount it at Apple's standard path.",
                !imageMounted,
                paths.CoreSimulatorMount,
                paths.CoreSimulatorImage,
                backupRoot));
        }

        foreach (var link in desired.CacheLinks)
        {
            var currentTarget = GetSymbolicLinkTarget(link.Source);
            steps.Add(Step(
                "cache-" + link.Id,
                $"Move {link.Description} to external storage and retain the standard local path as a symlink.",
                !PathsEqual(currentTarget, link.Target),
                link.Source,
                link.Target,
                backupRoot));
        }

        steps.Add(Step(
            "runner-config",
            "Set the runner work folder to the external work directory.",
            !StringEqualsNormalized(ReadRunnerWorkFolder(paths.RunnerConfigPath), desired.RelativeWorkRoot),
            paths.RunnerConfigPath,
            paths.WorkRoot));

        steps.Add(Step(
            "runner-environment",
            "Set external NuGet and Playwright cache paths in the runner environment.",
            !File.Exists(paths.EnvironmentPath)
            || !StringEqualsOrdinal(File.ReadAllText(paths.EnvironmentPath), desired.EnvironmentContent),
            paths.EnvironmentPath));

        steps.Add(Step(
            "runner-wrapper",
            "Install the storage-aware runner service wrapper.",
            !File.Exists(paths.WrapperPath)
            || !StringEqualsOrdinal(File.ReadAllText(paths.WrapperPath), desired.WrapperContent),
            paths.WrapperPath));

        steps.Add(Step(
            "launch-agent",
            "Point the runner LaunchAgent at the storage-aware wrapper.",
            !LaunchAgentUsesWrapper(paths.LaunchAgentPath, paths.WrapperPath),
            paths.LaunchAgentPath,
            paths.WrapperPath));

        return steps;
    }

    private void Apply(
        ProvisioningPaths paths,
        DesiredState desired,
        string backupRoot,
        List<MacOsRunnerStorageProvisioningStep> steps)
    {
        if (IsMounted(paths.CoreSimulatorMount)
            && !IsImageMountedAt(paths.CoreSimulatorImage, paths.CoreSimulatorMount))
        {
            throw new InvalidOperationException(
                $"CoreSimulator is already mounted from a different image at '{paths.CoreSimulatorMount}'. Detach it before applying this plan.");
        }

        Directory.CreateDirectory(paths.StateRoot);
        Directory.CreateDirectory(paths.WorkRoot);
        Directory.CreateDirectory(paths.CacheRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(backupRoot)!);
        foreach (var link in desired.CacheLinks)
            Directory.CreateDirectory(link.Target);
        BackupManagedFiles(paths, backupRoot);

        if (!Directory.Exists(paths.CoreSimulatorImage))
        {
            RunRequired(
                "/usr/bin/hdiutil",
                paths.StateRoot,
                "create",
                "-size",
                desired.ImageSizeGb.ToString(System.Globalization.CultureInfo.InvariantCulture) + "g",
                "-type",
                "SPARSEBUNDLE",
                "-fs",
                "APFS",
                "-volname",
                "GitHubRunnerCoreSimulator",
                paths.CoreSimulatorImage);
        }

        if (!IsImageMountedAt(paths.CoreSimulatorImage, paths.CoreSimulatorMount))
            MigrateCoreSimulator(paths, backupRoot);

        foreach (var link in desired.CacheLinks)
            MigrateDirectoryToLink(link, backupRoot);

        WriteRunnerConfig(paths.RunnerConfigPath, desired.RelativeWorkRoot);
        WriteTextIfChanged(paths.EnvironmentPath, desired.EnvironmentContent);
        WriteTextIfChanged(paths.WrapperPath, desired.WrapperContent);
        RunRequired("/bin/chmod", paths.RunnerRoot, "+x", paths.WrapperPath);
        if (!LaunchAgentUsesWrapper(paths.LaunchAgentPath, paths.WrapperPath))
            UpdateLaunchAgent(paths.LaunchAgentPath, paths.WrapperPath);

        foreach (var step in steps.Where(step => step.Changed))
            step.Skipped = false;

        _logger.Info($"macOS runner storage provisioning completed. Recoverable backups, when needed, are under: {backupRoot}");
    }

    private void MigrateCoreSimulator(ProvisioningPaths paths, string backupRoot)
    {
        var temporaryMount = Path.Combine(paths.StateRoot, ".mount-CoreSimulator");
        if (IsMounted(temporaryMount))
            throw new InvalidOperationException($"Temporary CoreSimulator mount is already occupied: {temporaryMount}");

        Directory.CreateDirectory(temporaryMount);
        RunRequired(
            "/usr/bin/hdiutil",
            paths.StateRoot,
            "attach",
            paths.CoreSimulatorImage,
            "-mountpoint",
            temporaryMount,
            "-nobrowse",
            "-owners",
            "on");

        try
        {
            if (Directory.Exists(paths.CoreSimulatorMount) && Directory.EnumerateFileSystemEntries(paths.CoreSimulatorMount).Any())
            {
                RunRequired("/usr/bin/ditto", paths.StateRoot, paths.CoreSimulatorMount, temporaryMount);
                Directory.CreateDirectory(backupRoot);
                var backup = Path.Combine(backupRoot, "CoreSimulator-local");
                BackupDirectory(paths.CoreSimulatorMount, backup, paths.StateRoot);
                Directory.Delete(paths.CoreSimulatorMount, recursive: true);
            }
            else if (Directory.Exists(paths.CoreSimulatorMount))
            {
                Directory.Delete(paths.CoreSimulatorMount);
            }
        }
        finally
        {
            RunRequired("/usr/bin/hdiutil", paths.StateRoot, "detach", temporaryMount);
            if (Directory.Exists(temporaryMount))
                Directory.Delete(temporaryMount, recursive: false);
        }

        Directory.CreateDirectory(paths.CoreSimulatorMount);
        RunRequired(
            "/usr/bin/hdiutil",
            paths.StateRoot,
            "attach",
            paths.CoreSimulatorImage,
            "-mountpoint",
            paths.CoreSimulatorMount,
            "-nobrowse",
            "-owners",
            "on");
    }

    private void MigrateDirectoryToLink(CacheLink link, string backupRoot)
    {
        var currentTarget = GetSymbolicLinkTarget(link.Source);
        if (PathsEqual(currentTarget, link.Target))
            return;
        if (!string.IsNullOrWhiteSpace(currentTarget))
        {
            throw new InvalidOperationException(
                $"Refusing to replace an existing symlink at '{link.Source}' that points to '{currentTarget}'.");
        }

        Directory.CreateDirectory(link.Target);
        if (Directory.Exists(link.Source))
        {
            if (Directory.EnumerateFileSystemEntries(link.Source).Any())
                RunRequired("/usr/bin/ditto", link.Target, link.Source, link.Target);

            Directory.CreateDirectory(backupRoot);
            var backup = Path.Combine(backupRoot, link.Id + "-local");
            BackupDirectory(link.Source, backup, link.Target);
            Directory.Delete(link.Source, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(link.Source)!);
        Directory.CreateSymbolicLink(link.Source, link.Target);
    }

    private void EnsureRunnerStopped(string runnerRoot)
    {
        var result = Run(
            "/usr/bin/pgrep",
            runnerRoot,
            "-fal",
            "Runner.Worker|Runner.Listener|runsvc.sh");
        if (result.ExitCode is not (0 or 1))
            throw ProcessFailure("Unable to inspect GitHub Actions runner processes.", result);

        var active = result.StdOut
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains(runnerRoot, StringComparison.Ordinal))
            .Where(line => !line.Contains("/usr/bin/pgrep", StringComparison.Ordinal))
            .ToArray();
        if (active.Length > 0)
        {
            throw new InvalidOperationException(
                "The GitHub Actions runner is active. Stop its service before applying storage provisioning. Active process: "
                + active[0]);
        }
    }

    private bool IsMounted(string mountPath)
    {
        var result = Run("/sbin/mount", "/", Array.Empty<string>());
        if (!result.Succeeded)
            throw ProcessFailure("Unable to inspect mounted filesystems.", result);
        return result.StdOut
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Contains(" on " + mountPath + " (", StringComparison.Ordinal));
    }

    private bool IsImageMountedAt(string imagePath, string mountPath)
    {
        var result = Run("/usr/bin/hdiutil", "/", "info");
        if (!result.Succeeded)
            throw ProcessFailure("Unable to inspect mounted disk images.", result);

        var currentImageMatches = false;
        foreach (var rawLine in result.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("image-path", StringComparison.Ordinal))
            {
                var separator = line.IndexOf(':');
                currentImageMatches = separator >= 0
                    && PathsEqual(line[(separator + 1)..].Trim(), imagePath);
                continue;
            }

            if (!currentImageMatches || !line.StartsWith("mount-point", StringComparison.Ordinal))
            {
                if (currentImageMatches
                    && line.Contains('\t')
                    && PathsEqual(line.Split('\t', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(), mountPath))
                {
                    return true;
                }
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex >= 0 && PathsEqual(line[(separatorIndex + 1)..].Trim(), mountPath))
                return true;
        }

        return false;
    }

    private bool LaunchAgentUsesWrapper(string launchAgentPath, string wrapperPath)
    {
        if (!File.Exists(launchAgentPath))
            return false;

        var result = Run(
            "/usr/bin/plutil",
            Path.GetDirectoryName(launchAgentPath)!,
            "-extract",
            "ProgramArguments.0",
            "raw",
            "-o",
            "-",
            launchAgentPath);
        return result.Succeeded && PathsEqual(result.StdOut.Trim(), wrapperPath);
    }

    private void ValidateExternalApfsVolume(string path)
    {
        var volumeRoot = GetExternalVolumeRoot(path);
        var result = Run("/usr/sbin/diskutil", "/", "info", "-plist", volumeRoot);
        if (!result.Succeeded)
            throw ProcessFailure($"Unable to inspect external volume '{volumeRoot}'.", result);

        var values = ParsePlistDictionary(result.StdOut);
        if (!values.TryGetValue("MountPoint", out var mountPoint)
            || !PathsEqual(mountPoint, volumeRoot))
        {
            throw new InvalidOperationException($"External volume is not mounted at the expected path: {volumeRoot}");
        }
        if (!values.TryGetValue("FilesystemType", out var fileSystem)
            || !fileSystem.Equals("apfs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"External runner storage must use APFS. '{volumeRoot}' reports '{fileSystem ?? "unknown"}'.");
        }
        if (values.TryGetValue("Internal", out var internalValue)
            && internalValue.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Runner storage must be on external media: {volumeRoot}");
        }
    }

    private string ResolveExternalVolumeUuid(string volumeRoot)
    {
        var result = Run("/usr/sbin/diskutil", "/", "info", "-plist", volumeRoot);
        if (!result.Succeeded)
            throw ProcessFailure($"Unable to inspect external volume '{volumeRoot}'.", result);
        var values = ParsePlistDictionary(result.StdOut);
        if (!values.TryGetValue("VolumeUUID", out var uuid) || string.IsNullOrWhiteSpace(uuid))
            throw new InvalidOperationException($"External APFS volume has no stable VolumeUUID: {volumeRoot}");
        return uuid.Trim();
    }

    private static Dictionary<string, string> ParsePlistDictionary(string plist)
    {
        var document = XDocument.Parse(plist);
        var dictionary = document.Descendants("dict").FirstOrDefault()
                         ?? throw new InvalidOperationException("diskutil returned an invalid property list.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodes = dictionary.Elements().ToArray();
        for (var index = 0; index + 1 < nodes.Length; index++)
        {
            if (nodes[index].Name.LocalName != "key")
                continue;
            var key = nodes[index].Value;
            var valueNode = nodes[index + 1];
            values[key] = valueNode.Name.LocalName switch
            {
                "true" => "true",
                "false" => "false",
                _ => valueNode.Value
            };
            index++;
        }
        return values;
    }

    private void UpdateLaunchAgent(string launchAgentPath, string wrapperPath)
    {
        var json = JsonSerializer.Serialize(new[] { wrapperPath });
        var temporaryPath = launchAgentPath + ".powerforge-" + Guid.NewGuid().ToString("N") + ".tmp";
        File.Copy(launchAgentPath, temporaryPath, overwrite: false);
        try
        {
            RunRequired(
                "/usr/bin/plutil",
                Path.GetDirectoryName(launchAgentPath)!,
                "-replace",
                "ProgramArguments",
                "-json",
                json,
                temporaryPath);
            File.Move(temporaryPath, launchAgentPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private void ValidateLaunchAgentOwnership(string launchAgentPath, string runnerRoot)
    {
        var result = Run(
            "/usr/bin/plutil",
            Path.GetDirectoryName(launchAgentPath)!,
            "-extract",
            "WorkingDirectory",
            "raw",
            "-o",
            "-",
            launchAgentPath);
        if (!result.Succeeded || !PathsEqual(result.StdOut.Trim(), runnerRoot))
        {
            throw new InvalidOperationException(
                $"LaunchAgent '{launchAgentPath}' does not belong to runner root '{runnerRoot}'.");
        }
    }

    private static void BackupManagedFiles(ProvisioningPaths paths, string backupRoot)
    {
        var configRoot = Path.Combine(backupRoot, "configuration");
        Directory.CreateDirectory(configRoot);
        BackupFile(paths.RunnerConfigPath, Path.Combine(configRoot, "runner.json"));
        BackupFile(paths.EnvironmentPath, Path.Combine(configRoot, "runner.env"));
        BackupFile(paths.WrapperPath, Path.Combine(configRoot, Path.GetFileName(paths.WrapperPath)));
        BackupFile(paths.LaunchAgentPath, Path.Combine(configRoot, Path.GetFileName(paths.LaunchAgentPath)));
    }

    private static void BackupFile(string source, string destination)
    {
        if (!File.Exists(source))
            return;
        if (File.Exists(destination))
            return;

        var temporaryPath = destination + ".partial-" + Guid.NewGuid().ToString("N");
        File.Copy(source, temporaryPath, overwrite: false);
        try
        {
            File.Move(temporaryPath, destination);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private void BackupDirectory(string source, string destination, string workingDirectory)
    {
        if (Directory.Exists(destination))
            return;
        if (File.Exists(destination))
            throw new InvalidOperationException($"Backup target is not a directory: {destination}");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = destination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            RunRequired("/usr/bin/ditto", workingDirectory, source, temporaryPath);
            Directory.Move(temporaryPath, destination);
        }
        finally
        {
            if (Directory.Exists(temporaryPath))
                Directory.Delete(temporaryPath, recursive: true);
        }
    }

    private static FileStream AcquireOperationLock(string runnerRoot)
    {
        var path = Path.Combine(runnerRoot, ".powerforge-runner-storage.lock");
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Another runner storage operation is active. Lock path: {path}",
                ex);
        }
    }

    private ProcessRunResult Run(string fileName, string workingDirectory, params string[] arguments)
        => Run(fileName, workingDirectory, (IReadOnlyList<string>)arguments);

    private ProcessRunResult Run(string fileName, string workingDirectory, IReadOnlyList<string> arguments)
        => _processRunner.RunAsync(new ProcessRunRequest(
                fileName,
                workingDirectory,
                arguments,
                TimeSpan.FromMinutes(15)))
            .GetAwaiter()
            .GetResult();

    private void RunRequired(string fileName, string workingDirectory, params string[] arguments)
    {
        var result = Run(fileName, workingDirectory, arguments);
        if (!result.Succeeded)
            throw ProcessFailure($"{fileName} failed.", result);
    }

    private static InvalidOperationException ProcessFailure(string message, ProcessRunResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return new InvalidOperationException($"{message} Exit code {result.ExitCode}: {detail.Trim()}");
    }

}
#endif
