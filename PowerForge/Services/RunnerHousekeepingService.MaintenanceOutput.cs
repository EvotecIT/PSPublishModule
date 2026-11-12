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
            var journal = MaintenanceRead(record, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceJournal);
            if (journal is null || journal.SchemaVersion != 1 || !Guid.TryParseExact(journal.Id, "N", out _) ||
                Path.GetFileNameWithoutExtension(record) != journal.Id || !roots.Any(r => PathsEqual(r!, journal.Root)) ||
                !MaintenanceContains(journal.Root, journal.Source) || MaintenanceContains(Path.Combine(journal.Root, MaintenanceQuarantine), journal.Source))
                throw new IOException("Quarantine record failed its enrolled-root check.");
            if (journal.RuntimeIpc && (journal.Directory || !MaintenanceRuntimeIpcOwner(journal.Source).HasValue || !run.Spec.Runners.Any(r =>
                PathsEqual(journal.Root, r.TempPath) || PathsEqual(journal.Root, r.WorkRootPath) && MaintenanceContains(Path.Combine(r.WorkRootPath, "_temp"), journal.Source))))
                throw new IOException("Runtime IPC journal failed its private temporary-root check.");
            // Only the cleanup process writes journals. An isolated target is never handed back to a job.
            var stage = MaintenanceStage(journal);
            if (File.Exists(stage) || Directory.Exists(stage))
            {
                if (!run.DryRun) run.Isolated.Add(journal);
            }
            else if (!run.DryRun) File.Delete(record);
        }
    }

    private static void MaintenanceRecordJob(MaintenanceRun run, RunnerMaintenancePhase phase)
    {
        if (run.Job is null) return;
        run.State.Jobs.RemoveAll(j => PathsEqual(j.Workspace, run.Job.Workspace));
        run.Job.CompletedUtc = phase == RunnerMaintenancePhase.Completed ? DateTime.UtcNow : DateTime.MinValue;
        run.State.Jobs.Add(run.Job);
        if (phase == RunnerMaintenancePhase.Completed) MaintenancePackageReferences(run, run.Job.Workspace);
    }

    private static void MaintenancePackageReferences(MaintenanceRun run, string root)
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
        MaintenanceClearChildren(run, runner.TempPath, runner.TempPath, DateTime.MaxValue);
        if (!run.ActiveRunnerRoots.Contains(runner.RootPath))
            MaintenanceClearChildren(run, Path.Combine(runner.WorkRootPath, "_temp"), runner.WorkRootPath, DateTime.UtcNow.AddDays(-1));
        var diagnostics = Path.Combine(runner.RootPath, "_diag");
        if (Directory.Exists(diagnostics))
            foreach (var path in Directory.EnumerateFiles(diagnostics, "*.log"))
                if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7))
                    MaintenanceIsolate(run, path, diagnostics, directory: false);

        foreach (var job in run.State.Jobs.Where(j => PathsEqual(j.RunnerRoot, runner.RootPath)).ToArray())
        {
            try { MaintenanceWorkspaceOutput(run, runner, job); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { run.Retained.Add(job.Workspace + " — " + error.Message); }
        }
        if (run.Deep && !run.ActiveRunnerRoots.Contains(runner.RootPath)) MaintenanceCodeQl(run, runner);
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
            if (Path.GetFileName(child) == MaintenanceQuarantine || File.GetLastWriteTimeUtc(child) >= olderThan) continue;
            var directory = Directory.Exists(child);
            var owner = directory ? null : MaintenanceRuntimeIpcOwner(child);
            if (owner.HasValue && MaintenanceProcessAlive(owner.Value)) continue;
            MaintenanceIsolate(run, child, root, directory, runtimeIpc: owner.HasValue, olderThan: olderThan == DateTime.MaxValue ? null : olderThan);
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

    private static void MaintenanceLegacyTemp(MaintenanceRun run)
    {
        if (!run.Deep || run.Spec.LegacyTemporaryRoots.Length == 0) return;
        string servicePaths;
        try { servicePaths = MaintenanceServicePaths(run.Spec.Runners[0].RootPath); }
        catch (IOException)
        {
            run.Retained.Add("Legacy fixtures retained because installed service paths could not be checked.");
            return;
        }
        foreach (var producer in run.Spec.LegacyTemporaryRoots)
        {
            if (!Directory.Exists(producer.RootPath)) continue;
            foreach (var path in Directory.EnumerateDirectories(producer.RootPath, producer.Prefix + "*"))
            {
                if (run.Expired) return;
                if (!Guid.TryParseExact(Path.GetFileName(path).Substring(producer.Prefix.Length), "N", out _)) continue;
                if (servicePaths.IndexOf(path, GetPathComparison()) >= 0 || run.ExecutablePaths.Any(executable => MaintenanceContains(path, executable)))
                {
                    run.Retained.Add(path + " — service or application path");
                    continue;
                }
                var cutoff = DateTime.UtcNow.AddHours(-producer.MinimumAgeHours);
                if (Directory.GetLastWriteTimeUtc(path) >= cutoff) continue;
                var entries = Directory.EnumerateFileSystemEntries(path).ToArray();
                if (entries.Any(p => File.GetLastWriteTimeUtc(p) >= cutoff || (Directory.Exists(p)
                    ? !producer.DirectoryNames.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                    : !producer.FileNames.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase) && !producer.FileExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)))) continue;
                var qualified = MaintenanceDirectories(path, run, includeGenerated: true).All(directory => Directory.EnumerateFiles(directory).All(file =>
                    File.GetLastWriteTimeUtc(file) < cutoff && (producer.FileNames.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase) ||
                    producer.FileExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) &&
                    !new[] { ".nupkg", ".pfx", ".key" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)));
                if (qualified) MaintenanceIsolate(run, path, producer.RootPath, directory: true, sensitive: false);
            }
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
        var units = RunnerMaintenanceProcesses.Run("systemctl", directory, "list-unit-files", "--type=service", "--no-legend", "--no-pager")
            .Split('\n').Select(line => line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(name => name is not null && name.EndsWith(".service", StringComparison.Ordinal)).Cast<string>().ToArray();
        return units.Length == 0 ? string.Empty : RunnerMaintenanceProcesses.Run("systemctl", directory,
            new[] { "show", "--property=ExecStart", "--value" }.Concat(units).ToArray());
    }

    private static void MaintenanceDocker(MaintenanceRun run)
    {
        if (!run.Deep || !run.Spec.DockerBuilderCacheGiB.HasValue || run.DryRun || run.Expired) return;
        RunnerMaintenanceProcesses.Run("docker", run.Spec.StateRootPath, new[] { "builder", "prune", "--force", "--filter", "until=168h", "--keep-storage", run.Spec.DockerBuilderCacheGiB.Value + "GB" });
    }
}
