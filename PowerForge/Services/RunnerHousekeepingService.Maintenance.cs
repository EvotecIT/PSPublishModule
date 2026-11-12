using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge;

public sealed partial class RunnerHousekeepingService
{
    private readonly RunnerMaintenanceProcessReader _maintenanceProcesses = RunnerMaintenanceProcesses.Read;

    internal RunnerHousekeepingService(ILogger logger, RunnerMaintenanceProcessReader processes) : this(logger)
        => _maintenanceProcesses = processes ?? throw new ArgumentNullException(nameof(processes));

    /// <summary>Runs host-local maintenance or an installed job lifecycle hook.</summary>
    /// <param name="spec">Enrolled machine-local policy.</param>
    /// <param name="phase">Scheduler, job admission or completion phase.</param>
    /// <param name="runnerRoot">Owning installation for a job hook.</param>
    /// <param name="dryRun">Plan cleanup without changing output.</param>
    /// <returns>Cleanup, deferral and admission result.</returns>
    public RunnerMaintenanceResult Maintain(RunnerMaintenanceSpec spec, RunnerMaintenancePhase phase = RunnerMaintenancePhase.Scheduled,
        string? runnerRoot = null, bool dryRun = true)
        => MaintainCore(spec, phase, runnerRoot, dryRun, hookCleanupAllowed: true);

    private RunnerMaintenanceResult MaintainCore(RunnerMaintenanceSpec spec, RunnerMaintenancePhase phase, string? runnerRoot, bool dryRun, bool hookCleanupAllowed)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        ValidateMaintenanceSpec(spec);
        var run = new MaintenanceRun { Spec = spec, DryRun = dryRun };
        run.Result.DryRun = dryRun;
        run.Result.FreeBytesBefore = MaintenanceFreeBytes(spec.StateRootPath);
        run.SimulatedFree = run.Result.FreeBytesBefore;
        FileStream? admission = null;
        string? readyPath = null;
        try
        {
            var processes = _maintenanceProcesses(out _);
            var ready = MaintenanceHookContext(spec, phase, runnerRoot, processes, out var job);
            if (ready is not null) ready.CleanupAllowed = hookCleanupAllowed;
            run.Job = job;
            if (!dryRun)
            {
                Directory.CreateDirectory(spec.StateRootPath);
                if (ready is not null)
                {
                    readyPath = Path.Combine(spec.StateRootPath, "ready", ready.HookId + ".json");
                    MaintenanceWrite(readyPath, ready, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceReady);
                }
                var timeout = phase == RunnerMaintenancePhase.Scheduled ? TimeSpan.Zero : TimeSpan.FromSeconds(spec.AdmissionTimeoutSeconds);
                admission = MaintenanceLock(Path.Combine(spec.StateRootPath, "admission.lock"), timeout);
                if (admission is null)
                {
                    run.Result.Deferred = true;
                    if (phase != RunnerMaintenancePhase.Scheduled) run.Errors.Add("Job admission lock timed out.");
                    return MaintenanceFinish(run);
                }
            }
            if (File.Exists(Path.Combine(spec.StateRootPath, MaintenanceInstallationJournal)))
                throw new IOException("Enrollment recovery is pending. Keep its services stopped and retry the installer.");
            run.Result.Admitted = phase != RunnerMaintenancePhase.Scheduled;
            run.State = MaintenanceRead(Path.Combine(spec.StateRootPath, "state.json"), RunnerMaintenanceJsonContext.Default.RunnerMaintenanceState) ?? new RunnerMaintenanceState();
            if (run.State.SchemaVersion != 1) throw new IOException("Unsupported maintenance state version.");
            if (run.Result.FreeBytesBefore < GiB(spec.MinimumFreeGiB)) run.State.PressureTargetBytes = GiB(spec.TargetFreeGiB);
            run.Deep = run.State.Due || run.State.PressureTargetBytes > 0 || run.State.LastDeepSweepUtc <= DateTime.UtcNow.AddHours(-spec.DeepIntervalHours);
            var fresh = _maintenanceProcesses(out var snapshotIncomplete);
            run.ProcessSnapshotIncomplete = snapshotIncomplete;
            if (run.Job is not null)
            {
                var parent = RunnerMaintenanceProcesses.Ancestors(fresh, run.Job.WorkerId).Select(id => fresh.TryGetValue(id, out var process) ? process : null)
                    .FirstOrDefault(process => process is not null && process.Name.Equals("Runner.Listener", StringComparison.OrdinalIgnoreCase)
                        && spec.Runners.Any(r => PathsEqual(r.RootPath, run.Job.RunnerRoot) && MaintenanceProcessBelongs(r, process)));
                if (parent is not null) run.State.Listeners[run.Job.RunnerRoot] = new RunnerMaintenanceProcessIdentity { Id = parent.Id, StartTicks = parent.StartTicks };
            }
            run.ExecutablePaths = fresh.Values.Select(p => p.Executable).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToArray();
            foreach (var registration in spec.Runners)
                if (fresh.Values.Any(p => RunnerMaintenanceProcesses.IsWorker(p) && MaintenanceProcessBelongs(registration, p)))
                    run.ActiveRunnerRoots.Add(registration.RootPath);
            var readyWorkers = MaintenanceReadyWorkers(spec, fresh, out var admittedProcesses);
            if (ready is not null && ready.CleanupAllowed) readyWorkers.Add(ready.WorkerId);
            var busyWorkers = fresh.Values.Where(RunnerMaintenanceProcesses.IsWorker).Where(p => !readyWorkers.Contains(p.Id)).ToArray();
            var ancestors = RunnerMaintenanceProcesses.Ancestors(fresh, Process.GetCurrentProcess().Id);
            var builds = fresh.Values.Where(p => !ancestors.Contains(p.Id) && !admittedProcesses.Contains(p.Id)
                && RunnerMaintenanceProcesses.IsBuildProcess(p) && !RunnerMaintenanceProcesses.IsIdleBuildServer(p)).ToArray();
            var independentBuild = builds.Length > 0;
            run.SharedIdle = busyWorkers.Length == 0 && !independentBuild;
            if (!MaintenanceListenersEnrolled(run, fresh))
            {
                run.SharedIdle = false;
                run.Retained.Add("Shared caches retained because a listener has not loaded this enrollment.");
            }
            foreach (var process in busyWorkers.Concat(builds)) run.Retained.Add("Active " + process.Name + " PID " + process.Id + " retains shared caches.");
            run.Result.Deferred = !run.SharedIdle;
            run.State.Due = !run.SharedIdle;

            MaintenanceRecover(run);
            MaintenanceDeleteIsolated(run);
            run.Isolated.Clear();
            MaintenanceRecordJob(run, phase);
            MaintenanceRefreshJobProcesses(run, fresh);
            foreach (var runner in spec.Runners)
            {
                var active = busyWorkers.Any(p => MaintenanceProcessBelongs(runner, p));
                var unknown = busyWorkers.Any(p => string.IsNullOrEmpty(p.Executable) || !spec.Runners.Any(r => MaintenanceProcessBelongs(r, p)));
                var jobProcesses = run.State.Jobs.Where(j => PathsEqual(j.RunnerRoot, runner.RootPath)).SelectMany(j => j.Processes).ToArray();
                var orphanProcesses = jobProcesses.Where(identity => fresh.TryGetValue(identity.Id, out var process) && process.StartTicks == identity.StartTicks
                    && !ancestors.Contains(process.Id) && !admittedProcesses.Contains(process.Id) && !RunnerMaintenanceProcesses.IsIdleBuildServer(process)
                    && !RunnerMaintenanceProcesses.IsConsoleHost(process)).Select(identity => fresh[identity.Id]).ToArray();
                var orphanActivity = orphanProcesses.Length > 0;
                orphanActivity |= run.UnknownJobActivity.Contains(runner.RootPath);
                if (orphanActivity)
                {
                    run.SharedIdle = false;
                    run.Result.Deferred = run.State.Due = true;
                    foreach (var process in orphanProcesses) run.Retained.Add(runner.Name + " retains output for recorded " + process.Name + " PID " + process.Id + ".");
                }
                if (active || unknown || orphanActivity) continue;
                MaintenanceRunnerOutput(run, runner);
            }
            if (run.SharedIdle && !run.Expired)
            {
                MaintenanceLegacyTemp(run);
                MaintenanceNuGet(run);
                MaintenanceDocker(run);
                if (run.Deep && !run.Expired) run.State.LastDeepSweepUtc = DateTime.UtcNow;
            }
            else if (run.Deep) run.Retained.Add("Shared maintenance is due after other active work finishes.");

            MaintenanceDeleteIsolated(run);
            if (!dryRun)
            {
                if (!run.Expired && run.SharedIdle && MaintenanceFreeBytes(spec.StateRootPath) >= run.State.PressureTargetBytes)
                    run.State.PressureTargetBytes = 0;
                run.State.Due |= run.Expired || run.State.PressureTargetBytes > 0 || run.Isolated.Any(j => File.Exists(Path.Combine(spec.StateRootPath, "journal", j.Id + ".json")));
                MaintenanceWrite(Path.Combine(spec.StateRootPath, "state.json"), run.State, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceState);
            }
        }
        catch (Exception error)
        {
            run.Errors.Add(error.GetType().Name + " — " + error.Message);
        }
        finally
        {
            // Remove readiness before releasing admission so a returning start hook cannot be mistaken for idle work.
            if (readyPath is not null)
            {
                try { File.Delete(readyPath); }
                catch (IOException error) { run.Errors.Add(error.GetType().Name); }
                catch (UnauthorizedAccessException error) { run.Errors.Add(error.GetType().Name); }
            }
            admission?.Dispose();
        }
        return MaintenanceFinish(run);
    }

    private static long GiB(int value) => checked(value * 1024L * 1024L * 1024L);

    private static long MaintenanceFreeBytes(string path)
    {
        var existing = Path.GetFullPath(path);
        while (!Directory.Exists(existing)) existing = Path.GetDirectoryName(existing) ?? throw new IOException("Maintenance volume is unavailable.");
        var volume = DriveInfo.GetDrives().Where(d => PathsEqual(d.Name, existing) || MaintenanceContains(d.Name, existing))
            .OrderByDescending(d => d.Name.Length).FirstOrDefault() ?? throw new IOException("Maintenance volume is unavailable.");
        return volume.AvailableFreeSpace;
    }

    private static void ValidateMaintenanceSpec(RunnerMaintenanceSpec spec)
    {
        if (!Path.IsPathRooted(spec.StateRootPath) || spec.Runners.Length == 0 || spec.MinimumFreeGiB < 0 ||
            spec.TargetFreeGiB < spec.MinimumFreeGiB || spec.MaximumRunSeconds < 1 || spec.AdmissionTimeoutSeconds < 1 ||
            spec.WorkspaceRetentionDays < 0 || spec.NuGetRetentionDays < 0 || spec.DeepIntervalHours < 1 || spec.DockerBuilderCacheGiB < 1)
            throw new ArgumentException("Invalid maintenance roots, thresholds or retention policy.");
        MaintenancePhysicalPath(spec.StateRootPath);
        var durableRoots = spec.Runners.SelectMany(r => new[] { r.RootPath, r.WorkRootPath }).Concat(new[]
            { spec.StateRootPath, spec.NuGetPackagesPath, spec.NuGetHttpCachePath, spec.NuGetScratchPath }).Where(p => !string.IsNullOrEmpty(p)).ToArray();
        foreach (var runner in spec.Runners)
        {
            if (durableRoots.Any(p => PathsEqual(p!, runner.TempPath) || MaintenanceContains(runner.TempPath, p!) || MaintenanceContains(p!, runner.TempPath)))
                throw new ArgumentException("Private job temporary storage must be disjoint from installations, workspaces, caches and state.");
            if (PathsEqual(runner.RootPath, runner.WorkRootPath)) throw new ArgumentException("The runner work directory cannot be its installation root.");
        }
        foreach (var runner in spec.Runners)
        {
            if (runner.AgentId <= 0 || string.IsNullOrWhiteSpace(runner.Name) || string.IsNullOrWhiteSpace(runner.GitHubUrl))
                throw new ArgumentException("Runner identity must be captured by maintenance enrollment.");
            foreach (var path in new[] { runner.RootPath, runner.WorkRootPath, runner.TempPath })
            {
                if (!Path.IsPathRooted(path) || PathsEqual(path, Path.GetPathRoot(path)!) || PathsEqual(path, spec.StateRootPath) || MaintenanceContains(path, spec.StateRootPath))
                    throw new ArgumentException("Cleanup roots must be physical directories outside the persistent maintenance state.");
                MaintenancePhysicalPath(path);
            }
            using var registration = JsonDocument.Parse(File.ReadAllText(Path.Combine(runner.RootPath, ".runner")));
            var value = registration.RootElement;
            var work = Path.GetFullPath(Path.Combine(runner.RootPath, value.GetProperty("workFolder").GetString()!));
            if (value.GetProperty("agentId").GetInt64() != runner.AgentId || value.GetProperty("agentName").GetString() != runner.Name ||
                value.GetProperty("gitHubUrl").GetString() != runner.GitHubUrl || !PathsEqual(work, runner.WorkRootPath))
                throw new IOException("Enrolled runner identity or work directory changed.");
            foreach (var name in runner.ProtectedWorkspaces)
                if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
                    throw new ArgumentException("Protected workspaces must be top-level names.");
        }
        if (spec.Runners.Select(r => Path.GetFullPath(r.RootPath)).Distinct(GetPathStringComparer()).Count() != spec.Runners.Length ||
            spec.Runners.Select(r => Path.GetFullPath(r.TempPath)).Distinct(GetPathStringComparer()).Count() != spec.Runners.Length)
            throw new ArgumentException("Runner installation and private temporary roots must be distinct.");
        foreach (var runner in spec.Runners)
            if (spec.Runners.Any(other => !ReferenceEquals(runner, other) && (PathsEqual(runner.WorkRootPath, other.WorkRootPath) ||
                MaintenanceContains(runner.WorkRootPath, other.WorkRootPath) || MaintenanceContains(other.WorkRootPath, runner.WorkRootPath))))
                throw new ArgumentException("Enrolled runners must have separate work directories.");
        foreach (var root in spec.LegacyTemporaryRoots)
        {
            if (!Path.IsPathRooted(root.RootPath) || string.IsNullOrWhiteSpace(root.Prefix) || root.Prefix.IndexOfAny(new[] { '/', '\\', '*', '?', '[', ']' }) >= 0 || root.MinimumAgeHours < 1)
                throw new ArgumentException("Legacy fixtures require a physical parent and literal producer prefix.");
            MaintenancePhysicalPath(root.RootPath);
        }
        foreach (var path in new[] { spec.NuGetPackagesPath, spec.NuGetHttpCachePath, spec.NuGetScratchPath }.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (!Path.IsPathRooted(path!) || PathsEqual(path!, Path.GetPathRoot(path!)!) || PathsEqual(path!, spec.StateRootPath) || MaintenanceContains(path!, spec.StateRootPath))
                throw new ArgumentException("Invalid shared cache path.");
            MaintenancePhysicalPath(path!);
        }
    }

    private static bool MaintenanceProcessBelongs(RunnerMaintenanceRegistration runner, RunnerMaintenanceProcess process)
    {
        if (process.Executable is null || !MaintenanceContains(runner.RootPath, process.Executable)) return false;
        var relative = process.Executable.Substring(AppendDirectorySeparator(Path.GetFullPath(runner.RootPath)).Length);
        var bin = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return bin.Equals("bin", StringComparison.OrdinalIgnoreCase) || (bin.StartsWith("bin.", StringComparison.OrdinalIgnoreCase) && Version.TryParse(bin.Substring(4), out _));
    }

    private static RunnerMaintenanceReady? MaintenanceHookContext(RunnerMaintenanceSpec spec, RunnerMaintenancePhase phase, string? runnerRoot,
        Dictionary<int, RunnerMaintenanceProcess> processes, out RunnerMaintenanceJob? job)
    {
        job = null;
        if (phase == RunnerMaintenancePhase.Scheduled) return null;
        var runner = spec.Runners.SingleOrDefault(r => runnerRoot is not null && PathsEqual(r.RootPath, runnerRoot))
            ?? throw new ArgumentException("A hook must identify its enrolled runner root.");
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || !long.TryParse(Environment.GetEnvironmentVariable("GITHUB_RUN_ID"), out _))
            throw new InvalidOperationException("A job hook requires an assigned GitHub Actions job.");
        var currentId = Process.GetCurrentProcess().Id;
        var worker = RunnerMaintenanceProcesses.Ancestors(processes, currentId).Select(id => processes.TryGetValue(id, out var p) ? p : null)
            .FirstOrDefault(p => p is not null && RunnerMaintenanceProcesses.IsWorker(p) && MaintenanceProcessBelongs(runner, p));
        if (worker is null || worker.StartTicks == 0 || !processes.TryGetValue(currentId, out var current) || current.StartTicks == 0)
            throw new InvalidOperationException("A job hook must descend from the enrolled runner worker.");
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? string.Empty;
        if (!MaintenanceContains(runner.WorkRootPath, workspace)) throw new InvalidOperationException("Job workspace is outside its enrolled work root.");
        job = new RunnerMaintenanceJob { RunnerRoot = runner.RootPath, Workspace = Path.GetFullPath(workspace),
            SourceSha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? string.Empty, WorkerId = worker.Id, WorkerStartTicks = worker.StartTicks };
        var trackingId = Environment.GetEnvironmentVariable(RunnerMaintenanceProcesses.JobMarker);
        if (trackingId is not null && Guid.TryParseExact(trackingId, "N", out _))
        {
            job.TrackingIds.Add(trackingId);
            job.TrackingStartTicks = worker.StartTicks;
        }
        return new RunnerMaintenanceReady { WorkerId = worker.Id, WorkerStartTicks = worker.StartTicks, HookId = current.Id,
            HookStartTicks = current.StartTicks, RunnerRoot = runner.RootPath };
    }

    private static HashSet<int> MaintenanceReadyWorkers(RunnerMaintenanceSpec spec, Dictionary<int, RunnerMaintenanceProcess> processes, out HashSet<int> admittedProcesses)
    {
        var ready = new HashSet<int>();
        admittedProcesses = new HashSet<int>();
        var root = Path.Combine(spec.StateRootPath, "ready");
        if (!Directory.Exists(root)) return ready;
        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            var value = MaintenanceRead(path, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceReady);
            if (value is null || !value.CleanupAllowed || !processes.TryGetValue(value.WorkerId, out var worker) || !processes.TryGetValue(value.HookId, out var hook) ||
                worker.StartTicks != value.WorkerStartTicks || hook.StartTicks != value.HookStartTicks || !RunnerMaintenanceProcesses.IsWorker(worker)) continue;
            var runner = spec.Runners.FirstOrDefault(r => PathsEqual(r.RootPath, value.RunnerRoot));
            var lineage = RunnerMaintenanceProcesses.Ancestors(processes, hook.Id);
            if (runner is not null && MaintenanceProcessBelongs(runner, worker) && lineage.Contains(worker.Id))
            {
                ready.Add(worker.Id);
                admittedProcesses.UnionWith(lineage);
            }
        }
        return ready;
    }

    private static bool MaintenanceListenersEnrolled(MaintenanceRun run, Dictionary<int, RunnerMaintenanceProcess> processes)
    {
        var spec = run.Spec;
        foreach (var listener in processes.Values.Where(p => p.Name.Equals("Runner.Listener", StringComparison.OrdinalIgnoreCase)))
        {
            var runner = spec.Runners.FirstOrDefault(r => MaintenanceProcessBelongs(r, listener));
            var marker = Path.Combine(spec.StateRootPath, "installed.json");
            if (runner is null || !File.Exists(marker)) return false;
            var observedHook = run.State.Listeners.TryGetValue(runner.RootPath, out var loaded) && loaded.Id == listener.Id && loaded.StartTicks == listener.StartTicks;
            if (!observedHook && listener.StartTicks <= File.GetLastWriteTimeUtc(marker).Ticks) return false;
            var lines = File.ReadAllLines(Path.Combine(runner.RootPath, ".env"));
            foreach (var phase in new[] { "STARTED", "COMPLETED" })
            {
                var prefix = "ACTIONS_RUNNER_HOOK_JOB_" + phase + "=";
                var hook = lines.SingleOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length);
                if (string.IsNullOrEmpty(hook) || !MaintenanceContains(Path.Combine(spec.StateRootPath, "hooks"), hook!) || !File.Exists(hook)) return false;
            }
        }
        return true;
    }

    private static RunnerMaintenanceResult MaintenanceFinish(MaintenanceRun run)
    {
        run.Result.Success = run.Errors.Count == 0;
        run.Result.RemovedPaths = run.Removed.ToArray();
        run.Result.PlannedPaths = run.Planned.ToArray();
        run.Result.Retained = run.Retained.ToArray();
        run.Result.Errors = run.Errors.ToArray();
        run.Result.FreeBytesAfter = MaintenanceFreeBytes(run.Spec.StateRootPath);
        run.Result.CompletedUtc = DateTime.UtcNow;
        run.Result.ElapsedMilliseconds = run.Clock.ElapsedMilliseconds;
        if (!run.DryRun && Directory.Exists(run.Spec.StateRootPath))
        {
            try
            {
                var root = Path.Combine(run.Spec.StateRootPath, "receipts");
                Directory.CreateDirectory(root);
                MaintenanceWrite(Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Process.GetCurrentProcess().Id + ".json"), run.Result, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceResult);
                foreach (var file in Directory.EnumerateFiles(root, "*.json").OrderByDescending(p => p, StringComparer.Ordinal).Skip(4)) File.Delete(file);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                run.Errors.Add("Maintenance receipt could not be persisted — " + error.GetType().Name);
                run.Result.Success = false;
                run.Result.Errors = run.Errors.ToArray();
            }
        }
        return run.Result;
    }
}
