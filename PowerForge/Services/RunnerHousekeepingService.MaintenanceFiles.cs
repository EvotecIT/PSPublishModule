using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class RunnerHousekeepingService
{
    private const string MaintenanceQuarantine = ".powerforge-quarantine";

    private sealed class MaintenanceRun
    {
        internal RunnerMaintenanceSpec Spec = null!;
        internal RunnerMaintenanceResult Result = new();
        internal RunnerMaintenanceState State = new();
        internal Stopwatch Clock = Stopwatch.StartNew();
        internal readonly List<string> Removed = new();
        internal readonly List<string> Planned = new();
        internal readonly List<string> Retained = new();
        internal readonly List<string> Errors = new();
        internal readonly List<RunnerMaintenanceJournal> Isolated = new();
        internal readonly HashSet<string> ActiveRunnerRoots = new(GetPathStringComparer());
        internal string[] ExecutablePaths = Array.Empty<string>();
        internal readonly HashSet<string> UnknownJobActivity = new(GetPathStringComparer());
        internal bool DryRun;
        internal bool SharedIdle;
        internal bool Deep;
        internal long SimulatedFree;
        internal RunnerMaintenanceJob? Job;
        internal bool Expired => Clock.Elapsed.TotalSeconds >= Spec.MaximumRunSeconds;
    }

    private static bool MaintenanceContains(string root, string path) =>
        Path.GetFullPath(path).StartsWith(AppendDirectorySeparator(Path.GetFullPath(root)), GetPathComparison());

    private static void MaintenancePhysicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        for (var entry = new DirectoryInfo(full); entry is not null; entry = entry.Parent)
        {
            if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Maintenance path contains a redirected directory.");
        }
        if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Maintenance file is redirected.");
    }

    private static FileStream? MaintenanceLock(string path, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            MaintenancePhysicalPath(path);
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (clock.Elapsed < timeout) { Thread.Sleep(100); }
            catch (IOException) { return null; }
        } while (clock.Elapsed < timeout);
        return null;
    }

    private static void MaintenanceWrite<T>(string path, T value, JsonTypeInfo<T> type)
        => MaintenanceWriteText(path, JsonSerializer.Serialize(value, type));

    private static void MaintenanceWriteText(string path, string value)
        => MaintenanceWriteBytes(path, new UTF8Encoding(false).GetBytes(value));

    private static void MaintenanceWriteBytes(string path, byte[] value)
    {
        MaintenancePhysicalPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, value);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static T? MaintenanceRead<T>(string path, JsonTypeInfo<T> type) where T : class
    {
        MaintenancePhysicalPath(path);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new IOException("Maintenance record exceeds its size limit.");
        return JsonSerializer.Deserialize(File.ReadAllText(path), type) ?? throw new IOException("Empty maintenance record.");
    }

    private static long MaintenanceSnapshot(MaintenanceRun run, string path, bool directory, bool sensitive = true, bool repository = false, DateTime? olderThan = null)
    {
        MaintenancePhysicalPath(path);
        var pending = new Stack<string>();
        long bytes = 0;
        if (directory) pending.Push(path);
        else InspectFile(path);
        while (pending.Count > 0)
        {
            if (run.Expired) throw new IOException("Maintenance execution budget reached.");
            var current = pending.Pop();
            MaintenancePhysicalPath(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if (olderThan.HasValue && File.GetLastWriteTimeUtc(entry) >= olderThan.Value) throw new IOException("Recent temporary entry retained.");
                if ((attributes & FileAttributes.ReparsePoint) != 0 || (Path.GetFileName(entry).Equals(".git", StringComparison.OrdinalIgnoreCase) && !(repository && PathsEqual(current, path))))
                    throw new IOException("Redirected or nested Git payload retained.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else InspectFile(entry);
            }
        }
        return bytes;

        void InspectFile(string file)
        {
            if (run.Expired) throw new IOException("Maintenance execution budget reached.");
            MaintenancePhysicalPath(file);
            if (olderThan.HasValue && File.GetLastWriteTimeUtc(file) >= olderThan.Value) throw new IOException("Recent temporary entry retained.");
            if (sensitive && MaintenanceSensitive(file)) throw new IOException("Package, key or database payload retained.");
            using var handle = RunnerMaintenanceProcesses.OpenRegularFile(file);
            bytes += handle.Length;
        }
    }

    private static bool MaintenanceSensitive(string path)
    {
        var extension = Path.GetExtension(path);
        return new[] { ".nupkg", ".pfx", ".key", ".db", ".sqlite" }.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) || path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase);
    }

    private static string MaintenanceStage(RunnerMaintenanceJournal journal) => Path.Combine(journal.Root, MaintenanceQuarantine, journal.Id);

    private static void MaintenanceIsolate(MaintenanceRun run, string path, string root, bool directory, bool sensitive = true, bool repository = false, bool runtimeIpc = false, DateTime? olderThan = null)
    {
        if (run.Expired || !MaintenanceContains(root, path) || Path.GetFileName(path) == MaintenanceQuarantine) return;
        try
        {
            MaintenancePhysicalPath(root);
            MaintenancePhysicalPath(path);
            var bytes = runtimeIpc ? 0 : MaintenanceSnapshot(run, path, directory, sensitive, repository, olderThan);
            if (run.DryRun)
            {
                run.Planned.Add(path);
                run.Result.PlannedBytes += bytes;
                run.SimulatedFree += bytes;
                return;
            }
            var journal = new RunnerMaintenanceJournal { Id = Guid.NewGuid().ToString("N"), Root = root, Source = path, Directory = directory, Bytes = bytes, Repository = repository, RuntimeIpc = runtimeIpc };
            var stage = MaintenanceStage(journal);
            MaintenancePhysicalPath(stage);
            Directory.CreateDirectory(Path.GetDirectoryName(stage)!);
            var record = Path.Combine(run.Spec.StateRootPath, "journal", journal.Id + ".json");
            MaintenanceWrite(record, journal, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceJournal);
            if (directory) Directory.Move(path, stage);
            else File.Move(path, stage);
            journal.Moved = true;
            MaintenanceWrite(record, journal, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceJournal);
            run.Isolated.Add(journal);
            run.SimulatedFree += bytes;
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            run.Retained.Add(path + " — " + error.GetType().Name);
        }
    }

    private void MaintenanceDeleteIsolated(MaintenanceRun run)
    {
        foreach (var journal in run.Isolated)
        {
            if (run.Expired) { run.Retained.Add("Quarantine cleanup deferred by the execution budget."); break; }
            var stage = MaintenanceStage(journal);
            try
            {
                MaintenancePhysicalPath(stage);
                if (Directory.Exists(stage) || File.Exists(stage))
                {
                    if (!journal.RuntimeIpc) MaintenanceSnapshot(run, stage, journal.Directory, sensitive: false, repository: journal.Repository);
                    DeleteTarget(stage, allowSudo: false, isDirectory: journal.Directory, allowedRootPath: journal.Root);
                    run.Result.RemovedBytes += journal.Bytes;
                    run.Removed.Add(journal.Source);
                }
                File.Delete(Path.Combine(run.Spec.StateRootPath, "journal", journal.Id + ".json"));
                var parent = Path.GetDirectoryName(stage)!;
                if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                run.Errors.Add("Quarantine cleanup retained " + journal.Id + " — " + error.GetType().Name);
            }
        }
    }

    private static int? MaintenanceRuntimeIpcOwner(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Length > 200) return null;
        var match = Regex.Match(name, @"^(?:clr-debug-pipe-(?<pid>[0-9]+)-[0-9]+-(?:in|out)|dotnet-diagnostic-(?<pid>[0-9]+)-[0-9]+-socket|CoreFxPipe_PSHost\.[0-9A-F]+\.(?<pid>[0-9]+)\.[A-Za-z0-9_-]+\.pwsh)$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return match.Success && int.TryParse(match.Groups["pid"].Value, out var id) ? id : null;
    }
}
