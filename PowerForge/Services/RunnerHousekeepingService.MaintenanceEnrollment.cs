using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class RunnerHousekeepingService
{
    private const string MaintenanceInstallationJournal = "installation.pending.json";

    private static RunnerMaintenanceInstallationRecovery MaintenanceCaptureInstallation(RunnerMaintenanceSpec spec, IEnumerable<string> paths)
    {
        var recovery = new RunnerMaintenanceInstallationRecovery { RunnerRoots = spec.Runners.Select(r => r.RootPath).ToArray(), ConfigurationPaths = spec.NuGetConfigPaths };
        foreach (var path in paths.Distinct(GetPathStringComparer()))
        {
            MaintenancePhysicalPath(path);
            if (Directory.Exists(path)) throw new IOException("An enrollment file path is occupied by a directory.");
            if (File.Exists(path) && new FileInfo(path).Length > 8 * 1024 * 1024) throw new IOException("Enrollment backup exceeds its bounded recovery size.");
            recovery.Files.Add(new RunnerMaintenanceInstallationBackup { Path = path, Bytes = File.Exists(path) ? File.ReadAllBytes(path) : null });
        }
        if (recovery.Files.Count > 64 || recovery.Files.Sum(file => (long)(file.Bytes?.Length ?? 0)) > 8 * 1024 * 1024)
            throw new IOException("Enrollment backup exceeds its bounded recovery size.");
        return recovery;
    }

    private static void MaintenanceRestoreInstallation(string stateRoot)
    {
        var path = Path.Combine(stateRoot, MaintenanceInstallationJournal);
        var recovery = MaintenanceRead(path, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceInstallationRecovery);
        if (recovery is null) return;
        if (recovery.SchemaVersion != 1 || recovery.Files.Count == 0 || recovery.Files.Count > 64)
            throw new IOException("Invalid enrollment recovery record.");
        var metadata = new[] { "policy.json", "scheduled.json", "installed.json", "Invoke-RunnerMaintenance.ps1" };
        foreach (var file in recovery.Files)
        {
            var name = Path.GetFileName(file.Path);
            if (!Path.IsPathRooted(file.Path) || !(PathsEqual(Path.GetDirectoryName(file.Path)!, stateRoot) && metadata.Contains(name, StringComparer.Ordinal)
                || recovery.RunnerRoots.Any(root => PathsEqual(file.Path, Path.Combine(root, ".env"))) && File.Exists(Path.Combine(Path.GetDirectoryName(file.Path)!, ".runner"))
                || recovery.ConfigurationPaths.Any(config => PathsEqual(config, file.Path)))) throw new IOException("Unsupported enrollment recovery target.");
            MaintenancePhysicalPath(file.Path);
        }
        var errors = new List<Exception>();
        foreach (var file in recovery.Files.AsEnumerable().Reverse())
        {
            try
            {
                if (file.Bytes is null) File.Delete(file.Path);
                else if (!File.Exists(file.Path) || !File.ReadAllBytes(file.Path).SequenceEqual(file.Bytes)) MaintenanceWriteBytes(file.Path, file.Bytes);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Enrollment recovery remains pending. Keep its runner services stopped and retry the installer.", errors);
        File.Delete(path);
    }

    private static string? MaintenancePreviousHook(RunnerMaintenanceRegistration runner, string variable, IEnumerable<string> lines)
    {
        var assigned = lines.LastOrDefault(line => line.StartsWith(variable + "=", StringComparison.OrdinalIgnoreCase));
        var value = assigned is not null ? assigned.Substring(variable.Length + 1)
            : variable.EndsWith("STARTED", StringComparison.Ordinal) ? runner.InheritedStartedHook : runner.InheritedCompletedHook;
        if (value is null)
            throw new InvalidOperationException("Capture the registered service's inherited " + variable + " before enrollment. Use an explicit empty value when the service has no inherited hook.");
        return MaintenanceMigrateTemporaryHook(runner, variable, value);
    }
}
