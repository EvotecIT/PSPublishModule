using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace PowerForge;

public sealed partial class RunnerHousekeepingService
{
    private static void MaintenanceRecover(MaintenanceRun run)
    {
        var directory = Path.Combine(run.Spec.StateRootPath, "journal");
        if (!Directory.Exists(directory)) return;
        var roots = run.Spec.Runners.SelectMany(r => new[] { r.WorkRootPath, r.TempPath, Path.Combine(r.RootPath, "_diag") })
            .Concat(run.Spec.LegacyTemporaryRoots.Select(r => r.RootPath))
            .Concat(new[] { run.Spec.NuGetPackagesPath, run.Spec.NuGetHttpCachePath }).Where(p => !string.IsNullOrEmpty(p)).ToArray();
        foreach (var record in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (run.Expired) break;
            try
            {
                var journal = MaintenanceRead(record, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceJournal);
                if (journal is null || journal.SchemaVersion != 1 || !Guid.TryParseExact(journal.Id, "N", out _) ||
                    Path.GetFileNameWithoutExtension(record) != journal.Id || !roots.Any(r => PathsEqual(r!, journal.Root)) ||
                    !MaintenanceContains(journal.Root, journal.Source) || MaintenanceContains(Path.Combine(journal.Root, MaintenanceQuarantine), journal.Source))
                {
                    run.Errors.Add("Quarantine record " + Path.GetFileName(record) + " failed its enrolled-root check and was retained.");
                    continue;
                }
                if (journal.RuntimeIpc && (journal.Directory || !MaintenanceRuntimeIpcOwner(journal.Source).HasValue || !run.Spec.Runners.Any(r =>
                    PathsEqual(journal.Root, r.TempPath) || PathsEqual(journal.Root, r.WorkRootPath) && MaintenanceContains(Path.Combine(r.WorkRootPath, "_temp"), journal.Source))))
                {
                    run.Errors.Add("Quarantine record " + Path.GetFileName(record) + " failed its private temporary-root check and was retained.");
                    continue;
                }
                // Only the cleanup process writes journals. An isolated target is never handed back to a job.
                var stage = MaintenanceStage(journal);
                if (File.Exists(stage) || Directory.Exists(stage))
                {
                    if (!run.DryRun) run.Isolated.Add(journal);
                }
                else if (!run.DryRun) File.Delete(record);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is JsonException || error is ArgumentException)
            {
                run.Errors.Add("Quarantine record " + Path.GetFileName(record) + " retained — " + error.Message);
            }
        }
    }

    private static void MaintenanceRecordJob(MaintenanceRun run, RunnerMaintenancePhase phase)
    {
        if (run.Job is null) return;
        var predecessors = run.State.Jobs.Where(j => PathsEqual(j.Workspace, run.Job.Workspace) && j.TrackingIds.Count > 0).ToArray();
        if (predecessors.Length > 0)
            run.Job.TrackingStartTicks = predecessors.Select(job => job.TrackingStartTicks > 0 ? job.TrackingStartTicks : job.WorkerStartTicks)
                .Concat(run.Job.TrackingStartTicks > 0 ? new[] { run.Job.TrackingStartTicks } : Array.Empty<long>()).Min();
        run.Job.Processes.AddRange(run.State.Jobs.Where(j => PathsEqual(j.Workspace, run.Job.Workspace)).SelectMany(j => j.Processes));
        run.Job.TrackingIds = run.Job.TrackingIds.Concat(run.State.Jobs.Where(j => PathsEqual(j.Workspace, run.Job.Workspace)).SelectMany(j => j.TrackingIds)).Distinct(StringComparer.Ordinal).ToList();
        run.State.Jobs.RemoveAll(j => PathsEqual(j.Workspace, run.Job.Workspace));
        run.Job.CompletedUtc = phase == RunnerMaintenancePhase.Completed ? DateTime.UtcNow : DateTime.MinValue;
        run.State.Jobs.Add(run.Job);
        if (phase == RunnerMaintenancePhase.Completed) MaintenancePackageReferences(run, run.Job.Workspace);
    }

    private void MaintenanceRefreshJobProcesses(MaintenanceRun run, Dictionary<int, RunnerMaintenanceProcess> processes)
    {
        foreach (var job in run.State.Jobs)
        {
            job.Processes.RemoveAll(identity => !processes.TryGetValue(identity.Id, out var process) || process.StartTicks != identity.StartTicks);
            if (!processes.TryGetValue(job.WorkerId, out var worker) || worker.StartTicks != job.WorkerStartTicks || !RunnerMaintenanceProcesses.IsWorker(worker)) continue;
            var descendants = processes.Values.Where(process => process.Id != worker.Id && process.StartTicks > 0
                && RunnerMaintenanceProcesses.Ancestors(processes, process.Id).Contains(worker.Id));
            foreach (var process in descendants)
                if (!job.Processes.Any(identity => identity.Id == process.Id && identity.StartTicks == process.StartTicks))
                    job.Processes.Add(new RunnerMaintenanceProcessIdentity { Id = process.Id, StartTicks = process.StartTicks });
        }
        var tracked = run.State.Jobs.Where(job => job.TrackingIds.Count > 0).ToArray();
        if (tracked.Length == 0) return;
        var earliest = tracked.Min(job => job.TrackingStartTicks > 0 ? job.TrackingStartTicks : job.WorkerStartTicks);
        var uncertain = run.ProcessSnapshotIncomplete;
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var process in processes.Values.Where(process => process.StartTicks >= earliest))
        {
            if (run.Expired)
            {
                foreach (var job in tracked) run.UnknownJobActivity.Add(job.RunnerRoot);
                break;
            }
            string? marker;
            try
            {
                marker = RunnerMaintenanceProcesses.ReadJobMarker(process.Id);
                using var current = System.Diagnostics.Process.GetProcessById(process.Id);
                if (current.HasExited || current.StartTime.ToUniversalTime().Ticks != process.StartTicks) { uncertain = true; continue; }
            }
            catch (ArgumentException) { uncertain = true; continue; }
            catch (InvalidOperationException) { uncertain = true; continue; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception)
            {
                if (!MaintenanceProcessAlive(process.Id)) { uncertain = true; continue; }
                foreach (var job in tracked) run.UnknownJobActivity.Add(job.RunnerRoot);
                run.Retained.Add("Job marker could not be inspected for " + process.Name + " PID " + process.Id + ".");
                continue;
            }
            if (marker is null) continue;
            found.Add(marker);
            foreach (var job in tracked.Where(job => job.TrackingIds.Contains(marker)))
                if (!job.Processes.Any(identity => identity.Id == process.Id && identity.StartTicks == process.StartTicks))
                    job.Processes.Add(new RunnerMaintenanceProcessIdentity { Id = process.Id, StartTicks = process.StartTicks });
        }
        // A marked parent can hand off to a new child while environments are read.
        // Require a complete second discovery with no new lifetime before retiring ownership.
        var after = _maintenanceProcesses(out var afterIncomplete);
        uncertain |= afterIncomplete || after.Values.Any(process => !processes.TryGetValue(process.Id, out var before) || before.StartTicks != process.StartTicks);
        if (uncertain)
            foreach (var job in tracked) run.UnknownJobActivity.Add(job.RunnerRoot);
        foreach (var job in tracked)
        {
            if (run.UnknownJobActivity.Contains(job.RunnerRoot) || processes.TryGetValue(job.WorkerId, out var worker) && worker.StartTicks == job.WorkerStartTicks) continue;
            job.TrackingIds.RemoveAll(marker => !found.Contains(marker));
        }
    }

    private static void MaintenancePackageReferences(MaintenanceRun run, string root)
    {
        try
        {
            foreach (var directory in MaintenanceDirectories(root, run))
            {
                var path = Path.Combine(directory, "project.assets.json");
                if (!File.Exists(path) || new FileInfo(path).Length > 32 * 1024 * 1024) continue;
                try
                {
                    using var assets = JsonDocument.Parse(File.ReadAllText(path));
                    if (!assets.RootElement.TryGetProperty("libraries", out var libraries)) continue;
                    foreach (var entry in libraries.EnumerateObject())
                        if (entry.Value.TryGetProperty("type", out var type) && type.GetString() == "package")
                            run.State.PackageUse[entry.Name.ToLowerInvariant()] = DateTime.UtcNow;
                }
                catch (JsonException) { run.Retained.Add(path + " — invalid assets metadata"); }
            }
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            run.Retained.Add(root + " — package references could not be inspected: " + error.Message);
            run.SharedIdle = false;
            run.Result.Deferred = run.State.Due = true;
        }
    }

    private static IEnumerable<string> MaintenanceDirectories(string root, MaintenanceRun run, bool includeGenerated = false)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0 && !run.Expired)
        {
            var current = pending.Pop();
            MaintenancePhysicalPath(current);
            yield return current;
            foreach (var child in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(child);
                if (name == ".git" || name == MaintenanceQuarantine || !includeGenerated && (name == "node_modules" || name == "bin" || name == "packages")) continue;
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
            }
        }
    }

    private static void MaintenanceRunnerOutput(MaintenanceRun run, RunnerMaintenanceRegistration runner)
    {
        MaintenanceRetainOnIo(run, runner.TempPath, () => MaintenanceClearChildren(run, runner.TempPath, runner.TempPath, DateTime.MaxValue));
        MaintenanceLegacyTemp(run, runner.TempPath);
        if (!run.ActiveRunnerRoots.Contains(runner.RootPath))
        {
            var temporary = Path.Combine(runner.WorkRootPath, "_temp");
            MaintenanceRetainOnIo(run, temporary, () => MaintenanceClearChildren(run, temporary, runner.WorkRootPath, DateTime.UtcNow.AddDays(-1)));
        }
        var diagnostics = Path.Combine(runner.RootPath, "_diag");
        MaintenanceRetainOnIo(run, diagnostics, () =>
        {
            if (!Directory.Exists(diagnostics)) return;
            foreach (var path in Directory.EnumerateFiles(diagnostics, "*.log"))
            {
                if (run.Expired) break;
                MaintenanceRetainOnIo(run, path, () =>
                {
                    if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7))
                        MaintenanceIsolate(run, path, diagnostics, directory: false);
                });
            }
        });

        foreach (var job in run.State.Jobs.Where(j => PathsEqual(j.RunnerRoot, runner.RootPath)).ToArray())
        {
            try { MaintenanceWorkspaceOutput(run, runner, job); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { run.Retained.Add(job.Workspace + " — " + error.Message); }
        }
        if (run.Deep && !run.ActiveRunnerRoots.Contains(runner.RootPath))
            MaintenanceRetainOnIo(run, runner.WorkRootPath, () => MaintenanceCodeQl(run, runner));
    }

    private static void MaintenanceRetainOnIo(MaintenanceRun run, string path, Action cleanup)
    {
        try { cleanup(); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        { run.Retained.Add(path + " — " + error.Message); }
    }

    private static void MaintenanceWorkspaceOutput(MaintenanceRun run, RunnerMaintenanceRegistration runner, RunnerMaintenanceJob job)
    {
            if (run.Expired) return;
            if (!MaintenanceContains(runner.WorkRootPath, job.Workspace) || !Directory.Exists(job.Workspace)) return;
            var family = job.Workspace.Substring(AppendDirectorySeparator(Path.GetFullPath(runner.WorkRootPath)).Length)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (runner.ProtectedWorkspaces.Contains(family, StringComparer.OrdinalIgnoreCase)) return;
            if (job.CompletedUtc == DateTime.MinValue && (run.Job is null || !PathsEqual(run.Job.Workspace, job.Workspace)))
            {
                // A cancelled worker may never reach its completion hook. The host gate already proved it has exited.
                job.CompletedUtc = DateTime.UtcNow;
            }
            if (!Directory.Exists(Path.Combine(job.Workspace, ".git"))) return;
            var git = RunnerMaintenanceProcesses.Run("git", job.Workspace, "rev-parse", "HEAD").Trim();
            if (!string.Equals(git, job.SourceSha, StringComparison.OrdinalIgnoreCase)) return;
            if (run.Deep && job.CompletedUtc != DateTime.MinValue && job.CompletedUtc < DateTime.UtcNow.AddDays(-run.Spec.WorkspaceRetentionDays)
                && string.IsNullOrWhiteSpace(RunnerMaintenanceProcesses.Run("git", job.Workspace, "status", "--porcelain", "--untracked-files=all"))
                && RunnerMaintenanceProcesses.Run("git", job.Workspace, "rev-parse", "--abbrev-ref", "HEAD").Trim() == "HEAD")
            {
                MaintenanceIsolate(run, job.Workspace, runner.WorkRootPath, directory: true, repository: true);
                return;
            }
            foreach (var directory in MaintenanceDirectories(job.Workspace, run).ToArray())
            {
                if (!Directory.Exists(directory)) continue;
                if (!Directory.EnumerateFiles(directory).Any(p => new[] { ".csproj", ".fsproj", ".vbproj", ".sln", ".slnf", ".slnx" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))) continue;
                foreach (var name in new[] { "bin", "obj", "TestResults", "artifacts", "Artifacts" })
                {
                    var output = Path.Combine(directory, name);
                    if (Directory.Exists(output) && string.IsNullOrWhiteSpace(RunnerMaintenanceProcesses.Run("git", job.Workspace, "ls-files", "--", output)))
                        MaintenanceIsolate(run, output, runner.WorkRootPath, directory: true);
                }
            }
    }

    private static void MaintenanceCodeQl(MaintenanceRun run, RunnerMaintenanceRegistration runner)
    {
        var root = Path.Combine(runner.WorkRootPath, "_tool", "CodeQL");
        if (!Directory.Exists(root)) return;
        MaintenancePhysicalPath(root);
        var versions = Directory.EnumerateDirectories(root).Select(path => new { Path = path,
            Version = NuGet.Versioning.NuGetVersion.TryParse(Path.GetFileName(path), out var version) ? version : null })
            .Where(entry => entry.Version is not null && !entry.Version.IsPrerelease).OrderByDescending(entry => entry.Version).Skip(2);
        foreach (var version in versions)
        {
            if (run.Expired) break;
            var entries = Directory.EnumerateFileSystemEntries(version.Path).ToArray();
            if (Directory.GetLastWriteTimeUtc(version.Path) >= DateTime.UtcNow.AddDays(-30) ||
                entries.Any(path => File.GetLastWriteTimeUtc(path) >= DateTime.UtcNow.AddDays(-30)) ||
                !entries.Any(path => path.EndsWith(".complete", StringComparison.Ordinal) && File.Exists(path))) continue;
            MaintenanceIsolate(run, version.Path, runner.WorkRootPath, directory: true);
        }
    }

    private static void MaintenanceClearChildren(MaintenanceRun run, string path, string root, DateTime olderThan)
    {
        if (!Directory.Exists(path)) return;
        MaintenancePhysicalPath(path);
        foreach (var child in Directory.EnumerateFileSystemEntries(path))
        {
            if (run.Expired) break;
            MaintenanceRetainOnIo(run, child, () =>
            {
                if (Path.GetFileName(child) == MaintenanceQuarantine || File.GetLastWriteTimeUtc(child) >= olderThan) return;
                var directory = Directory.Exists(child);
                var owner = directory ? null : MaintenanceRuntimeIpcOwner(child);
                if (owner.HasValue && MaintenanceProcessAlive(owner.Value)) return;
                MaintenanceIsolate(run, child, root, directory, runtimeIpc: owner.HasValue, olderThan: olderThan == DateTime.MaxValue ? null : olderThan);
            });
        }
    }

    private static bool MaintenanceProcessAlive(int id)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }

    private static void MaintenanceLegacyTemp(MaintenanceRun run, string? privateTempRoot = null)
    {
        if (privateTempRoot is not null && run.IndependentBuild) return;
        var producers = run.Spec.LegacyTemporaryRoots.Where(root => privateTempRoot is not null
            ? PathsEqual(root.RootPath, privateTempRoot)
            : !run.Spec.Runners.Any(runner => PathsEqual(root.RootPath, runner.TempPath))).ToArray();
        if (!run.Deep || run.Expired || producers.Length == 0) return;
        string servicePaths;
        try { servicePaths = MaintenanceServicePaths(run.Spec.Runners[0].RootPath); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            run.Retained.Add("Legacy fixtures retained because installed service paths could not be checked.");
            return;
        }
        foreach (var producer in producers)
        {
            MaintenanceRetainOnIo(run, producer.RootPath, () =>
            {
                if (!Directory.Exists(producer.RootPath)) return;
                foreach (var path in Directory.EnumerateDirectories(producer.RootPath, producer.Prefix + "*"))
                {
                    if (run.Expired) return;
                    MaintenanceRetainOnIo(run, path, () =>
                    {
                        if (!Guid.TryParseExact(Path.GetFileName(path).Substring(producer.Prefix.Length), "N", out _)) return;
                        if (servicePaths.IndexOf(path, GetPathComparison()) >= 0 || run.ExecutablePaths.Any(executable => MaintenanceContains(path, executable)))
                        {
                            run.Retained.Add(path + " — service or application path");
                            return;
                        }
                        var cutoff = DateTime.UtcNow.AddHours(-producer.MinimumAgeHours);
                        if (Directory.GetLastWriteTimeUtc(path) >= cutoff) return;
                        var entries = Directory.EnumerateFileSystemEntries(path).ToArray();
                        if (entries.Any(p => File.GetLastWriteTimeUtc(p) >= cutoff || (Directory.Exists(p)
                            ? !producer.DirectoryNames.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                            : !producer.FileNames.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase) && !producer.FileExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)))) return;
                        var qualified = MaintenanceDirectories(path, run, includeGenerated: true).All(directory => Directory.EnumerateFiles(directory).All(file =>
                            File.GetLastWriteTimeUtc(file) < cutoff && (producer.FileNames.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase) ||
                            producer.FileExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) &&
                            !new[] { ".nupkg", ".pfx", ".key" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)));
                        if (qualified) MaintenanceIsolate(run, path, producer.RootPath, directory: true, sensitive: false);
                    });
                }
            });
        }
    }

    private static string MaintenanceServicePaths(string directory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RunnerMaintenanceProcesses.Run("powershell.exe", directory, "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                "$ErrorActionPreference='Stop'; Get-CimInstance Win32_Service | ForEach-Object { $_.PathName }");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RunnerMaintenanceProcesses.Run("launchctl", directory, "print", "system") + RunnerMaintenanceProcesses.Run("launchctl", directory, "print",
                "gui/" + RunnerMaintenanceProcesses.Run("id", directory, "-u").Trim());
        var installed = MaintenanceServiceUnitNames(RunnerMaintenanceProcesses.Run("systemctl", directory,
            "list-unit-files", "--type=service", "--no-legend", "--no-pager"));
        var loaded = MaintenanceServiceUnitNames(RunnerMaintenanceProcesses.Run("systemctl", directory,
            "list-units", "--type=service", "--all", "--plain", "--no-legend", "--no-pager"));
        var templates = installed.Where(name => name.EndsWith("@.service", StringComparison.Ordinal)).ToArray();
        var units = installed.Concat(loaded).Where(name => !name.EndsWith("@.service", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
        var paths = units.Length == 0 ? string.Empty : RunnerMaintenanceProcesses.Run("systemctl", directory,
            new[] { "show", "--property=ExecStart", "--value" }.Concat(units).ToArray());
        // Uninstantiated templates reject systemctl show. Their installed definitions
        // retain literal service paths, while loaded instances above resolve specifiers.
        return templates.Length == 0 ? paths : paths + RunnerMaintenanceProcesses.Run("systemctl", directory,
            new[] { "cat", "--no-pager" }.Concat(templates).ToArray());
    }

    private static string[] MaintenanceServiceUnitNames(string text) => text.Split('\n')
        .Select(line => line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
        .Where(name => name is not null && name.EndsWith(".service", StringComparison.Ordinal)).Cast<string>().ToArray();

    private static void MaintenanceDocker(MaintenanceRun run)
    {
        if (!run.Deep || !run.Spec.DockerBuilderCacheGiB.HasValue || run.DryRun || run.Expired) return;
        try
        {
            var remaining = TimeSpan.FromSeconds(run.Spec.MaximumRunSeconds) - run.Clock.Elapsed;
            RunnerMaintenanceProcesses.Run("docker", run.Spec.StateRootPath, remaining,
                "builder", "prune", "--force", "--filter", "until=168h", "--keep-storage", run.Spec.DockerBuilderCacheGiB.Value + "GB");
        }
        catch (IOException error)
        {
            run.Retained.Add("Docker builder cache retained — " + error.Message);
        }
    }
}
