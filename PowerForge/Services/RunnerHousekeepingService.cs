using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Reclaims disk space on GitHub-hosted or self-hosted runners using conservative defaults.
/// </summary>
public sealed class RunnerHousekeepingService
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a runner housekeeping service with a logger.
    /// </summary>
    /// <param name="logger">Logger used for progress and diagnostics.</param>
    public RunnerHousekeepingService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a housekeeping run against the current runner filesystem.
    /// </summary>
    /// <param name="spec">Cleanup specification.</param>
    /// <returns>Run summary with step details and free-space counters.</returns>
    public RunnerHousekeepingResult Clean(RunnerHousekeepingSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var normalized = NormalizeSpec(spec);
        var steps = new List<RunnerHousekeepingStepResult>();
        var freeBefore = GetFreeBytes(normalized.FreeSpaceProbePath);
        var aggressiveApplied = normalized.Aggressive || (normalized.AggressiveThresholdBytes.HasValue && freeBefore < normalized.AggressiveThresholdBytes.Value);

        _logger.Info($"Runner root detected as: {normalized.RunnerRootPath}");
        _logger.Info($"Free disk before cleanup: {FormatGiB(freeBefore)} GiB");

        if (normalized.RequiredFreeBytes.HasValue)
            _logger.Info($"Required free disk after cleanup: {FormatGiB(normalized.RequiredFreeBytes.Value)} GiB");

        if (normalized.AggressiveThresholdBytes.HasValue)
            _logger.Info($"Aggressive cleanup threshold: < {FormatGiB(normalized.AggressiveThresholdBytes.Value)} GiB");

        if (normalized.CleanDiagnostics)
        {
            steps.Add(DeleteFilesOlderThan(
                id: "diag",
                title: "Cleanup runner diagnostics",
                rootPath: normalized.DiagnosticsRootPath,
                retentionDays: normalized.DiagnosticsRetentionDays,
                dryRun: normalized.DryRun));
        }

        if (normalized.CleanRunnerTemp)
        {
            steps.Add(DeleteDirectoryContents(
                id: "runner-temp",
                title: "Cleanup runner temp",
                rootPath: normalized.RunnerTempPath,
                dryRun: normalized.DryRun));
        }

        if (aggressiveApplied)
        {
            if (normalized.CleanActionsCache)
            {
                steps.Add(DeleteDirectoriesOlderThan(
                    id: "actions-cache",
                    title: "Cleanup action working sets",
                    rootPath: normalized.ActionsRootPath,
                    retentionDays: normalized.ActionsRetentionDays,
                    dryRun: normalized.DryRun,
                    allowSudo: normalized.AllowSudo));
            }

            if (normalized.CleanToolCache)
            {
                steps.Add(DeleteDirectoriesOlderThan(
                    id: "tool-cache",
                    title: "Cleanup runner tool cache",
                    rootPath: normalized.ToolCachePath,
                    retentionDays: normalized.ToolCacheRetentionDays,
                    dryRun: normalized.DryRun,
                    allowSudo: normalized.AllowSudo));
            }

            if (normalized.ClearDotNetCaches)
            {
                steps.Add(RunCommand(
                    id: "dotnet-cache",
                    title: "Clear dotnet caches",
                    fileName: "dotnet",
                    arguments: new[] { "nuget", "locals", "all", "--clear" },
                    workingDirectory: normalized.WorkRootPath,
                    dryRun: normalized.DryRun));
            }

            if (normalized.PruneDocker)
            {
                var args = normalized.IncludeDockerVolumes
                    ? new[] { "system", "prune", "-af", "--volumes" }
                    : new[] { "system", "prune", "-af" };

                steps.Add(RunCommand(
                    id: "docker-prune",
                    title: "Prune Docker data",
                    fileName: "docker",
                    arguments: args,
                    workingDirectory: normalized.WorkRootPath,
                    dryRun: normalized.DryRun));
            }
        }
        else
        {
            _logger.Info("Skipping aggressive cleanup; free disk is healthy.");
        }

        var freeAfter = normalized.DryRun ? freeBefore : GetFreeBytes(normalized.FreeSpaceProbePath);
        var result = new RunnerHousekeepingResult
        {
            RunnerRootPath = normalized.RunnerRootPath,
            WorkRootPath = normalized.WorkRootPath,
            RunnerTempPath = normalized.RunnerTempPath,
            DiagnosticsRootPath = normalized.DiagnosticsRootPath,
            ToolCachePath = normalized.ToolCachePath,
            FreeBytesBefore = freeBefore,
            FreeBytesAfter = freeAfter,
            RequiredFreeBytes = normalized.RequiredFreeBytes,
            AggressiveThresholdBytes = normalized.AggressiveThresholdBytes,
            AggressiveApplied = aggressiveApplied,
            DryRun = normalized.DryRun,
            Steps = steps.ToArray(),
            Success = steps.All(s => s.Success || s.Skipped)
        };

        if (!normalized.DryRun)
            _logger.Info($"Free disk after cleanup: {FormatGiB(freeAfter)} GiB");

        if (normalized.RequiredFreeBytes.HasValue && freeAfter < normalized.RequiredFreeBytes.Value)
        {
            result.Success = false;
            result.Message = $"Free disk after cleanup is {FormatGiB(freeAfter)} GiB (required: {FormatGiB(normalized.RequiredFreeBytes.Value)} GiB).";
        }

        return result;
    }

    private sealed class NormalizedSpec
    {
        public string RunnerRootPath { get; set; } = string.Empty;
        public string WorkRootPath { get; set; } = string.Empty;
        public string RunnerTempPath { get; set; } = string.Empty;
        public string? DiagnosticsRootPath { get; set; }
        public string? ActionsRootPath { get; set; }
        public string? ToolCachePath { get; set; }
        public string FreeSpaceProbePath { get; set; } = string.Empty;
        public long? RequiredFreeBytes { get; set; }
        public long? AggressiveThresholdBytes { get; set; }
        public int DiagnosticsRetentionDays { get; set; }
        public int ActionsRetentionDays { get; set; }
        public int ToolCacheRetentionDays { get; set; }
        public bool DryRun { get; set; }
        public bool Aggressive { get; set; }
        public bool CleanDiagnostics { get; set; }
        public bool CleanRunnerTemp { get; set; }
        public bool CleanActionsCache { get; set; }
        public bool CleanToolCache { get; set; }
        public bool ClearDotNetCaches { get; set; }
        public bool PruneDocker { get; set; }
        public bool IncludeDockerVolumes { get; set; }
        public bool AllowSudo { get; set; }
    }

    private NormalizedSpec NormalizeSpec(RunnerHousekeepingSpec spec)
    {
        var runnerTemp = ResolveRunnerTempPath(spec);
        if (string.IsNullOrWhiteSpace(runnerTemp) || !Directory.Exists(runnerTemp))
            throw new InvalidOperationException("RUNNER_TEMP is missing or invalid. Provide --runner-temp when running outside GitHub Actions.");

        var workRoot = ResolveWorkRootPath(spec, runnerTemp);
        var runnerRoot = ResolveRunnerRootPath(spec, workRoot);
        var diagnosticsRoot = ResolvePathOrNull(spec.DiagnosticsRootPath) ?? Path.Combine(runnerRoot, "_diag");
        var actionsRoot = Path.Combine(workRoot, "_actions");
        var toolCache = ResolvePathOrNull(spec.ToolCachePath)
                        ?? ResolvePathOrNull(Environment.GetEnvironmentVariable("RUNNER_TOOL_CACHE"))
                        ?? ResolvePathOrNull(Environment.GetEnvironmentVariable("AGENT_TOOLSDIRECTORY"));

        var requiredFreeBytes = spec.MinFreeGb is > 0 ? (long?)ToGiBBytes(spec.MinFreeGb.Value) : null;
        var aggressiveThresholdGb = spec.AggressiveThresholdGb
                                    ?? (spec.MinFreeGb is > 0 ? spec.MinFreeGb.Value + 5 : (int?)null);
        var aggressiveThresholdBytes = aggressiveThresholdGb is > 0 ? (long?)ToGiBBytes(aggressiveThresholdGb.Value) : null;

        return new NormalizedSpec
        {
            RunnerRootPath = runnerRoot,
            WorkRootPath = workRoot,
            RunnerTempPath = runnerTemp,
            DiagnosticsRootPath = diagnosticsRoot,
            ActionsRootPath = actionsRoot,
            ToolCachePath = toolCache,
            FreeSpaceProbePath = Directory.Exists(runnerRoot) ? runnerRoot : workRoot,
            RequiredFreeBytes = requiredFreeBytes,
            AggressiveThresholdBytes = aggressiveThresholdBytes,
            DiagnosticsRetentionDays = Math.Max(0, spec.DiagnosticsRetentionDays),
            ActionsRetentionDays = Math.Max(0, spec.ActionsRetentionDays),
            ToolCacheRetentionDays = Math.Max(0, spec.ToolCacheRetentionDays),
            DryRun = spec.DryRun,
            Aggressive = spec.Aggressive,
            CleanDiagnostics = spec.CleanDiagnostics,
            CleanRunnerTemp = spec.CleanRunnerTemp,
            CleanActionsCache = spec.CleanActionsCache,
            CleanToolCache = spec.CleanToolCache,
            ClearDotNetCaches = spec.ClearDotNetCaches,
            PruneDocker = spec.PruneDocker,
            IncludeDockerVolumes = spec.IncludeDockerVolumes,
            AllowSudo = spec.AllowSudo
        };
    }

    private static string ResolveRunnerTempPath(RunnerHousekeepingSpec spec)
    {
        var explicitPath = ResolvePathOrNull(spec.RunnerTempPath);
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath!;

        return ResolvePathOrNull(Environment.GetEnvironmentVariable("RUNNER_TEMP")) ?? string.Empty;
    }

    private static string ResolveWorkRootPath(RunnerHousekeepingSpec spec, string runnerTempPath)
    {
        var explicitPath = ResolvePathOrNull(spec.WorkRootPath);
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath!;

        var tempParent = Directory.GetParent(runnerTempPath)?.FullName;
        if (!string.IsNullOrWhiteSpace(tempParent) && Directory.Exists(tempParent))
            return tempParent!;

        var workspace = ResolvePathOrNull(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE"));
        if (!string.IsNullOrWhiteSpace(workspace))
        {
            var repoDir = Directory.GetParent(workspace);
            var workRoot = repoDir?.Parent?.FullName;
            if (!string.IsNullOrWhiteSpace(workRoot) && Directory.Exists(workRoot))
                return workRoot!;
        }

        throw new InvalidOperationException("Unable to determine runner work root. Provide --work-root explicitly.");
    }

    private static string ResolveRunnerRootPath(RunnerHousekeepingSpec spec, string workRootPath)
    {
        var explicitPath = ResolvePathOrNull(spec.RunnerRootPath);
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath!;

        return Directory.GetParent(workRootPath)?.FullName ?? workRootPath;
    }

    private RunnerHousekeepingStepResult DeleteFilesOlderThan(string id, string title, string? rootPath, int retentionDays, bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return SkippedStep(id, title, $"Path not found: {rootPath ?? "(null)"}");

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var targets = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(path => File.GetLastWriteTimeUtc(path) <= cutoff)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return DeleteTargets(id, title, targets, dryRun, allowSudo: false, isDirectory: false, allowedRootPath: rootPath);
    }

    private RunnerHousekeepingStepResult DeleteDirectoryContents(string id, string title, string? rootPath, bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return SkippedStep(id, title, $"Path not found: {rootPath ?? "(null)"}");

        var targets = Directory.EnumerateFileSystemEntries(rootPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return DeleteTargets(id, title, targets, dryRun, allowSudo: false, isDirectory: null, allowedRootPath: rootPath);
    }

    private RunnerHousekeepingStepResult DeleteDirectoriesOlderThan(string id, string title, string? rootPath, int retentionDays, bool dryRun, bool allowSudo)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return SkippedStep(id, title, $"Path not found: {rootPath ?? "(null)"}");

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var targets = Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Directory.GetLastWriteTimeUtc(path) <= cutoff)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return DeleteTargets(id, title, targets, dryRun, allowSudo, isDirectory: true, allowedRootPath: rootPath);
    }

    private RunnerHousekeepingStepResult DeleteTargets(string id, string title, string[] targets, bool dryRun, bool allowSudo, bool? isDirectory, string? allowedRootPath)
    {
        if (targets.Length == 0)
            return SkippedStep(id, title, "Nothing to clean.");

        if (dryRun)
        {
            _logger.Info($"{title}: planned {targets.Length} item(s).");
            return new RunnerHousekeepingStepResult
            {
                Id = id,
                Title = title,
                DryRun = true,
                EntriesAffected = targets.Length,
                Message = $"Planned {targets.Length} item(s).",
                Targets = targets
            };
        }

        var deleted = new List<string>(targets.Length);
        var failures = new List<string>();

        foreach (var target in targets)
        {
            try
            {
                DeleteTarget(target, allowSudo, isDirectory, allowedRootPath);
                deleted.Add(target);
            }
            catch (Exception ex)
            {
                failures.Add($"{target}: {ex.Message}");
            }
        }

        if (failures.Count == 0)
            _logger.Info($"{title}: deleted {deleted.Count} item(s).");
        else
            _logger.Warn($"{title}: deleted {deleted.Count} item(s), failed {failures.Count} item(s).");

        return new RunnerHousekeepingStepResult
        {
            Id = id,
            Title = title,
            Success = failures.Count == 0,
            EntriesAffected = deleted.Count,
            Message = failures.Count == 0
                ? $"Deleted {deleted.Count} item(s)."
                : $"Deleted {deleted.Count} item(s); failed {failures.Count} item(s): {string.Join(" | ", failures)}",
            Targets = targets
        };
    }

    private void DeleteTarget(string target, bool allowSudo, bool? isDirectory, string? allowedRootPath)
    {
        try
        {
            if (isDirectory == true || (isDirectory is null && Directory.Exists(target)))
            {
                Directory.Delete(target, recursive: true);
                return;
            }

            if (File.Exists(target))
                File.Delete(target);
        }
        catch when (allowSudo && CanUseSudo() && (isDirectory == true || Directory.Exists(target)))
        {
            RunSudoDelete(target, allowedRootPath);
        }
    }

    private RunnerHousekeepingStepResult RunCommand(string id, string title, string fileName, IReadOnlyList<string> arguments, string workingDirectory, bool dryRun)
    {
        if (!CommandExists(fileName))
            return SkippedStep(id, title, $"Command not available: {fileName}");

        var renderedArgs = string.Join(" ", arguments.Select(QuoteForDisplay));
        var commandText = $"{fileName} {renderedArgs}".Trim();

        if (dryRun)
        {
            _logger.Info($"{title}: would run '{commandText}'.");
            return new RunnerHousekeepingStepResult
            {
                Id = id,
                Title = title,
                DryRun = true,
                Command = commandText,
                Message = $"Would run '{commandText}'."
            };
        }

        var result = RunProcess(fileName, arguments, workingDirectory);
        if (result.ExitCode == 0)
        {
            _logger.Info($"{title}: completed.");
            return new RunnerHousekeepingStepResult
            {
                Id = id,
                Title = title,
                Command = commandText,
                ExitCode = result.ExitCode,
                Message = "Completed successfully."
            };
        }

        _logger.Warn($"{title}: exit code {result.ExitCode}. {result.StdErr}".Trim());
        return new RunnerHousekeepingStepResult
        {
            Id = id,
            Title = title,
            Success = false,
            Command = commandText,
            ExitCode = result.ExitCode,
            Message = string.IsNullOrWhiteSpace(result.StdErr) ? $"Exit code {result.ExitCode}." : result.StdErr.Trim()
        };
    }

    private static RunnerHousekeepingStepResult SkippedStep(string id, string title, string message)
    {
        return new RunnerHousekeepingStepResult
        {
            Id = id,
            Title = title,
            Skipped = true,
            Message = message
        };
    }

    private static long GetFreeBytes(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException($"Unable to determine filesystem root for path: {path}");

        var drive = new DriveInfo(root);
        return drive.AvailableFreeSpace;
    }

    private static long ToGiBBytes(int gibibytes)
        => gibibytes <= 0 ? 0 : (long)gibibytes * 1024L * 1024L * 1024L;

    private static string FormatGiB(long bytes)
        => (bytes <= 0 ? 0d : bytes / 1024d / 1024d / 1024d).ToString("N1", CultureInfo.InvariantCulture);

    private static string? ResolvePathOrNull(string? value)
    {
        var trimmed = value == null ? string.Empty : value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return Path.GetFullPath(trimmed);
    }

    private static bool CommandExists(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return false;

        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            : new[] { string.Empty };

        foreach (var rawSegment in pathValue.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Trim();
            if (string.IsNullOrWhiteSpace(segment) || !Directory.Exists(segment))
                continue;

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(segment, fileName + extension);
                if (File.Exists(candidate))
                    return true;
            }
        }

        return false;
    }

    private static bool CanUseSudo()
        => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && CommandExists("sudo");

    private void RunSudoDelete(string target, string? allowedRootPath)
    {
        EnsureDeleteTargetWithinRoot(target, allowedRootPath);
        var result = RunProcess("sudo", new[] { "rm", "-rf", target }, workingDirectory: Path.GetDirectoryName(target) ?? Environment.CurrentDirectory);
        if (result.ExitCode != 0)
            throw new IOException(string.IsNullOrWhiteSpace(result.StdErr) ? $"sudo rm -rf failed for '{target}'." : result.StdErr.Trim());
    }

    private static void EnsureDeleteTargetWithinRoot(string target, string? allowedRootPath)
    {
        var root = ResolvePathOrNull(allowedRootPath);
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException($"Refusing sudo delete for '{target}' because no safe root was supplied.");

        var fullRoot = AppendDirectorySeparator(root!);
        var fullTarget = Path.GetFullPath(target);
        if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing sudo delete for '{target}' because it is outside '{root}'.");
    }

    private static string AppendDirectorySeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
            return Path.DirectorySeparatorChar.ToString();

        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        psi.Arguments = BuildWindowsArgumentString(arguments);
#else
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);
#endif

        using var process = Process.Start(psi);
        if (process is null)
            return (-1, string.Empty, $"Failed to start process: {fileName}");

        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }

    private static string QuoteForDisplay(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return "\"\"";

        return argument.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
            ? $"\"{argument.Replace("\"", "\\\"")}\""
            : argument;
    }

#if NET472
    private static string BuildWindowsArgumentString(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(EscapeWindowsArgument));

    private static string EscapeWindowsArgument(string arg)
    {
        if (arg is null) return "\"\"";
        if (arg.Length == 0) return "\"\"";

        var needsQuotes = arg.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!needsQuotes) return arg;

        var sb = new System.Text.StringBuilder();
        sb.Append('"');

        var backslashCount = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                sb.Append('\\', backslashCount * 2 + 1);
                sb.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                sb.Append('\\', backslashCount);
                backslashCount = 0;
            }

            sb.Append(ch);
        }

        if (backslashCount > 0)
            sb.Append('\\', backslashCount * 2);

        sb.Append('"');
        return sb.ToString();
    }
#endif
}
