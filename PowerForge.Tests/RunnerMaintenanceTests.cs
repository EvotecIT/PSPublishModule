using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

[CollectionDefinition("Runner maintenance", DisableParallelization = true)]
public sealed class RunnerMaintenanceCollection;

[Collection("Runner maintenance")]
public sealed class RunnerMaintenanceTests
{
    [Fact]
    public void Maintain_IncompleteProcessInventoryRetainsPrivateAndSharedOutput()
    {
        using var fixture = new Installation();
        var privateFile = Path.Combine(fixture.Runner.TempPath, "output.txt");
        File.WriteAllText(privateFile, "active output");
        var cache = Directory.CreateDirectory(Path.Combine(fixture.Root, "http", "source")).FullName;
        var sharedFile = Path.Combine(cache, "package.dat");
        File.WriteAllText(sharedFile, "download");
        File.SetLastWriteTimeUtc(sharedFile, DateTime.UtcNow.AddHours(-2));
        fixture.Spec.NuGetHttpCachePath = Path.GetDirectoryName(cache);
        var service = new RunnerHousekeepingService(new NullLogger(), (out bool incomplete) =>
        {
            incomplete = true;
            return new Dictionary<int, RunnerMaintenanceProcess>();
        });
        var result = service.Maintain(fixture.Spec, dryRun: false);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.True(result.Deferred);
        Assert.Empty(result.RemovedPaths);
        Assert.Equal("active output", File.ReadAllText(privateFile));
        Assert.Equal("download", File.ReadAllText(sharedFile));
    }

    [Fact]
    public void Maintain_RejectsNestedRunnerTemporaryRootsBeforeDeletion()
    {
        using var fixture = new Installation();
        using var other = new Installation();
        other.Runner.TempPath = Path.Combine(fixture.Runner.TempPath, "nested");
        Directory.CreateDirectory(other.Runner.TempPath);
        var file = Path.Combine(other.Runner.TempPath, "active.txt");
        File.WriteAllText(file, "active job");
        fixture.Spec.Runners = new[] { fixture.Runner, other.Runner };
        Assert.Throws<ArgumentException>(() => fixture.Service.Maintain(fixture.Spec, dryRun: false));
        Assert.Equal("active job", File.ReadAllText(file));
    }

    [Fact]
    public void InstallMaintenance_RelativeNuGetConfigurationLeavesEnrollmentUntouched()
    {
        using var fixture = new Installation();
        fixture.PreparePayload();
        var environment = Path.Combine(fixture.Runner.RootPath, ".env");
        File.WriteAllText(environment, "CUSTOM_VALUE=keep\n");
        fixture.Spec.NuGetConfigPaths = new[] { "relative.NuGet.Config" };
        Assert.Throws<ArgumentException>(() => fixture.Service.InstallMaintenance(fixture.Spec));
        Assert.Equal("CUSTOM_VALUE=keep\n", File.ReadAllText(environment));
        Assert.False(File.Exists(Path.Combine(fixture.Spec.StateRootPath, "installation.pending.json")));
        Assert.False(File.Exists(Path.Combine(fixture.Spec.StateRootPath, "Invoke-RunnerMaintenance.ps1")));
    }

    [Fact]
    public void Maintain_CorruptPublicPackageRetainsArchiveAndPersistsState()
    {
        using var fixture = new Installation();
        fixture.Spec.NuGetPackagesPath = Path.Combine(fixture.Root, "packages");
        fixture.Spec.NuGetScratchPath = NuGet.Common.NuGetEnvironment.GetFolderPath(NuGet.Common.NuGetFolderPath.Temp);
        var version = Directory.CreateDirectory(Path.Combine(fixture.Spec.NuGetPackagesPath, "example", "1.0.0")).FullName;
        var archive = Path.Combine(version, "example.1.0.0.nupkg");
        File.WriteAllText(archive, "corrupt archive");
        File.WriteAllText(archive + ".sha512", "unused");
        var metadata = Path.Combine(version, ".nupkg.metadata");
        File.WriteAllText(metadata, "{\"source\":\"https://api.nuget.org/v3/index.json\"}");
        File.SetLastWriteTimeUtc(metadata, DateTime.UtcNow.AddDays(-40));
        File.SetLastAccessTimeUtc(metadata, DateTime.UtcNow.AddDays(-40));
        var result = fixture.Service.Maintain(fixture.Spec, dryRun: false);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Contains(result.Retained, path => path.Contains("InvalidDataException"));
        Assert.Equal("corrupt archive", File.ReadAllText(archive));
        Assert.True(File.Exists(Path.Combine(fixture.Spec.StateRootPath, "state.json")));
    }

    [Fact]
    public void Maintain_InvalidQuarantineRecordPreservesRecordAndContinuesCleanup()
    {
        using var fixture = new Installation();
        var journal = Directory.CreateDirectory(Path.Combine(fixture.Spec.StateRootPath, "journal")).FullName;
        var record = Path.Combine(journal, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(record, "{}");
        var temporary = Path.Combine(fixture.Runner.TempPath, "output.txt");
        File.WriteAllText(temporary, "completed output");
        var result = fixture.Service.Maintain(fixture.Spec, dryRun: false);
        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Equal("{}", File.ReadAllText(record));
        Assert.False(File.Exists(temporary));
        Assert.True(File.Exists(Path.Combine(fixture.Spec.StateRootPath, "state.json")));
    }

    [Fact]
    public void Maintain_PlansThenRemovesDisposableTempAndRetainsPrivatePayload()
    {
        using var fixture = new Installation();
        var disposable = Path.Combine(fixture.Runner.TempPath, "build", "output.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(disposable)!);
        File.WriteAllText(disposable, "regenerable");
        var privatePackage = Path.Combine(fixture.Runner.TempPath, "private.nupkg");
        File.WriteAllText(privatePackage, "private qualification");
        var diagnostic = Path.Combine(fixture.Runner.RootPath, "_diag", "Worker_old.log");
        Directory.CreateDirectory(Path.GetDirectoryName(diagnostic)!);
        File.WriteAllText(diagnostic, "old log");
        File.SetLastWriteTimeUtc(diagnostic, DateTime.UtcNow.AddDays(-8));

        var plan = fixture.Service.Maintain(fixture.Spec);
        Assert.True(plan.Success, string.Join("\n", plan.Errors));
        Assert.Contains(Path.GetDirectoryName(disposable)!, plan.PlannedPaths);
        Assert.True(File.Exists(disposable));
        Assert.False(Directory.Exists(fixture.Spec.StateRootPath));

        var result = fixture.Service.Maintain(fixture.Spec, dryRun: false);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.False(File.Exists(disposable));
        Assert.False(File.Exists(diagnostic));
        Assert.Equal("private qualification", File.ReadAllText(privatePackage));
        Assert.Equal("regenerable".Length + "old log".Length, result.RemovedBytes);
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Spec.StateRootPath, "receipts"), "*.json"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Maintain_RealEnrolledOrUnknownWorkerDefersOutputAndSharedCleanup(bool enrolled)
    {
        using var fixture = new Installation();
        var file = Path.Combine(fixture.Runner.TempPath, "active.txt");
        File.WriteAllText(file, "running job");
        var bin = Directory.CreateDirectory(Path.Combine(enrolled ? fixture.Runner.RootPath : Path.Combine(fixture.Root, "foreign-runner"), "bin")).FullName;
        var windows = OperatingSystem.IsWindows();
        var executable = Path.Combine(bin, windows ? "Runner.Worker.exe" : "Runner.Worker");
        Installation.CopyRunnerProcess(executable);
        using var worker = Process.Start(new ProcessStartInfo(executable, windows ? "/d /c ping -n 60 127.0.0.1 >nul" : "60")
            { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            Assert.True(SpinWait.SpinUntil(() => RunnerMaintenanceProcesses.Read().TryGetValue(worker.Id, out var process)
                && RunnerMaintenanceProcesses.IsWorker(process) && process.Executable is not null, TimeSpan.FromSeconds(5)));
            var result = fixture.Service.Maintain(fixture.Spec, dryRun: false);
            Assert.True(result.Success, string.Join("\n", result.Errors));
            Assert.True(result.Deferred);
            Assert.Empty(result.RemovedPaths);
            Assert.Equal("running job", File.ReadAllText(file));
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            worker.WaitForExit();
        }
        var idle = fixture.Service.Maintain(fixture.Spec, dryRun: false);
        Assert.True(idle.Success, string.Join("\n", idle.Errors));
        Assert.False(File.Exists(file));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Maintain_PrivateSyntheticFixturesRespectIndependentBuildsAndForeignSharedStorage(bool independentBuild)
    {
        using var fixture = new Installation();
        var shared = Directory.CreateDirectory(Path.Combine(fixture.Root, "shared-temp")).FullName;
        var own = Path.Combine(fixture.Runner.TempPath, "fixture-" + Guid.NewGuid().ToString("N"));
        var other = Path.Combine(shared, "fixture-" + Guid.NewGuid().ToString("N"));
        foreach (var path in new[] { own, other })
        {
            Directory.CreateDirectory(path);
            var database = Path.Combine(path, "fixture.sqlite");
            File.WriteAllText(database, "synthetic fixture");
            File.SetLastWriteTimeUtc(database, DateTime.UtcNow.AddDays(-2));
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        }
        fixture.Spec.LegacyTemporaryRoots = new[] { fixture.Runner.TempPath, shared }.Select(root => new RunnerMaintenanceTemporaryRoot
        {
            RootPath = root, Prefix = "fixture-", FileNames = new[] { "fixture.sqlite" }
        }).ToArray();
        var foreign = Directory.CreateDirectory(Path.Combine(fixture.Root, "foreign-runner", "bin")).FullName;
        var windows = OperatingSystem.IsWindows();
        var executable = Path.Combine(foreign, windows ? "Runner.Listener.exe" : "Runner.Listener");
        Installation.CopyRunnerProcess(executable);
        using var listener = Process.Start(new ProcessStartInfo(executable, windows ? "/d /c ping -n 60 127.0.0.1 >nul" : "60")
            { UseShellExecute = false, CreateNoWindow = true })!;
        var buildExecutable = Path.Combine(foreign, windows ? "dotnet.exe" : "dotnet");
        Installation.CopyRunnerProcess(buildExecutable);
        using var build = independentBuild ? Process.Start(new ProcessStartInfo(buildExecutable, windows ? "/d /c ping -n 60 127.0.0.1 >nul" : "60")
            { UseShellExecute = false, CreateNoWindow = true }) : null;
        try
        {
            Assert.True(SpinWait.SpinUntil(() => RunnerMaintenanceProcesses.Read().TryGetValue(listener.Id, out var process)
                && process.Name == "Runner.Listener" && process.Executable is not null, TimeSpan.FromSeconds(5)));
            var result = fixture.Service.Maintain(fixture.Spec, dryRun: false);
            Assert.True(result.Success, string.Join("\n", result.Errors));
            Assert.True(result.Deferred);
            Assert.True(Directory.Exists(own) == independentBuild, string.Join("\n", result.Retained));
            Assert.Equal("synthetic fixture", File.ReadAllText(Path.Combine(other, "fixture.sqlite")));
        }
        finally
        {
            if (build is not null)
            {
                if (!build.HasExited) build.Kill(entireProcessTree: true);
                build.WaitForExit();
            }
            if (!listener.HasExited) listener.Kill(entireProcessTree: true);
            listener.WaitForExit();
        }
    }

    [Fact]
    public void Maintain_ReplaysMoveInterruptedBeforeJournalAcknowledgement()
    {
        using var fixture = new Installation();
        var id = Guid.NewGuid().ToString("N");
        var isolated = Path.Combine(fixture.Runner.TempPath, ".powerforge-quarantine", id);
        Directory.CreateDirectory(isolated);
        File.WriteAllText(Path.Combine(isolated, "output.txt"), "isolated");
        var journal = Path.Combine(fixture.Spec.StateRootPath, "journal");
        Directory.CreateDirectory(journal);
        File.WriteAllText(Path.Combine(journal, id + ".json"), JsonSerializer.Serialize(new RunnerMaintenanceJournal
        {
            Id = id, Root = fixture.Runner.TempPath, Source = Path.Combine(fixture.Runner.TempPath, "completed"),
            Directory = true, Bytes = 8, Moved = false
        }, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceJournal));

        var result = fixture.Service.Maintain(fixture.Spec, dryRun: false);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.False(Directory.Exists(isolated));
        Assert.Empty(Directory.GetFiles(journal));
        Assert.Equal(8, result.RemovedBytes);
        var again = fixture.Service.Maintain(fixture.Spec, dryRun: false);
        Assert.True(again.Success, string.Join("\n", again.Errors));
        Assert.Equal(0, again.RemovedBytes);
    }

    [Fact]
    public void Maintain_RejectsJournalOutsideEnrollment()
    {
        using var fixture = new Installation();
        var other = Directory.CreateDirectory(Path.Combine(fixture.Root, "other")).FullName;
        var id = Guid.NewGuid().ToString("N");
        var target = Path.Combine(other, ".powerforge-quarantine", id);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "private.txt"), "keep");
        var journal = Directory.CreateDirectory(Path.Combine(fixture.Spec.StateRootPath, "journal")).FullName;
        File.WriteAllText(Path.Combine(journal, id + ".json"), JsonSerializer.Serialize(new RunnerMaintenanceJournal
            { Id = id, Root = other, Source = Path.Combine(other, "original"), Directory = true, Moved = true }, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceJournal));
        var result = fixture.Service.Maintain(fixture.Spec, dryRun: false);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("enrolled-root"));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "private.txt")));
    }

    [Fact]
    public void InstallMaintenance_ReenrollmentPreservesCustomHookAndSharedScratch()
    {
        using var fixture = new Installation();
        Directory.CreateDirectory(fixture.Spec.StateRootPath);
        fixture.Spec.ExecutablePath = Path.Combine(fixture.Spec.StateRootPath, "PowerForge.Cli");
        File.WriteAllText(fixture.Spec.ExecutablePath, "prebuilt placeholder");
        var originalHook = Path.Combine(fixture.Root, "custom-hook.ps1");
        File.WriteAllText(originalHook, "exit 0");
        File.WriteAllText(Path.Combine(fixture.Runner.RootPath, ".env"), "CUSTOM_VALUE=keep\nACTIONS_RUNNER_HOOK_JOB_STARTED=" + originalHook + "\n");
        fixture.Spec.NuGetScratchPath = Path.Combine(fixture.Root, "nuget-scratch");
        fixture.Service.InstallMaintenance(fixture.Spec);
        fixture.Service.InstallMaintenance(fixture.Spec);
        var environment = File.ReadAllLines(Path.Combine(fixture.Runner.RootPath, ".env"));
        Assert.Contains("CUSTOM_VALUE=keep", environment);
        Assert.Contains("NUGET_SCRATCH=" + fixture.Spec.NuGetScratchPath, environment);
        Assert.Contains("SystemTemp=" + fixture.Runner.TempPath, environment);
        var start = environment.Single(l => l.StartsWith("ACTIONS_RUNNER_HOOK_JOB_STARTED=")).Split('=', 2)[1];
        var settings = Path.Combine(Path.GetDirectoryName(start)!, "starting.json");
        var hook = JsonSerializer.Deserialize(File.ReadAllText(settings), RunnerMaintenanceJsonContext.Default.RunnerMaintenanceHook)!;
        Assert.Equal(originalHook, hook.PreviousHook);
        Assert.Equal(fixture.Runner.RootPath, hook.RunnerRoot);
        Assert.True(File.Exists(Path.Combine(fixture.Spec.StateRootPath, "scheduled.json")));
    }

    [Fact]
    public void Maintain_RejectsTemporaryRootOverlappingInstallationBeforeDeleting()
    {
        using var fixture = new Installation();
        fixture.Runner.TempPath = fixture.Runner.RootPath;
        var error = Assert.Throws<ArgumentException>(() => fixture.Service.Maintain(fixture.Spec, dryRun: false));
        Assert.Contains("disjoint", error.Message);
        Assert.True(File.Exists(Path.Combine(fixture.Runner.RootPath, ".runner")));
    }

    [Fact]
    public void Maintain_RetiresRecordedDetachedCheckoutAndPreservesProtectedFamily()
    {
        using var fixture = new Installation();
        fixture.Runner.ProtectedWorkspaces = new[] { "retained" };
        var state = new RunnerMaintenanceState();
        foreach (var family in new[] { "completed", "retained" })
        {
            var workspace = Directory.CreateDirectory(Path.Combine(fixture.Runner.WorkRootPath, family, family)).FullName;
            RunnerMaintenanceProcesses.Run("git", workspace, "init", "--quiet");
            RunnerMaintenanceProcesses.Run("git", workspace, "config", "core.hooksPath", Path.Combine(workspace, ".git", "no-hooks"));
            File.WriteAllText(Path.Combine(workspace, "Example.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(workspace, ".gitignore"), "bin/\nobj/\n");
            RunnerMaintenanceProcesses.Run("git", workspace, "add", "Example.csproj", ".gitignore");
            RunnerMaintenanceProcesses.Run("git", workspace, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "fixture");
            var sha = RunnerMaintenanceProcesses.Run("git", workspace, "rev-parse", "HEAD").Trim();
            RunnerMaintenanceProcesses.Run("git", workspace, "checkout", "--quiet", "--detach");
            Directory.CreateDirectory(Path.Combine(workspace, "bin"));
            File.WriteAllText(Path.Combine(workspace, "bin", "generated.txt"), "completed output");
            state.Jobs.Add(new RunnerMaintenanceJob { RunnerRoot = fixture.Runner.RootPath, Workspace = workspace, SourceSha = sha, CompletedUtc = DateTime.UtcNow.AddDays(-4) });
        }
        Directory.CreateDirectory(fixture.Spec.StateRootPath);
        File.WriteAllText(Path.Combine(fixture.Spec.StateRootPath, "state.json"), JsonSerializer.Serialize(state, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceState));
        var result = fixture.Service.Maintain(fixture.Spec, dryRun: false);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.False(Directory.Exists(Path.Combine(fixture.Runner.WorkRootPath, "completed", "completed")));
        Assert.Equal("completed output", File.ReadAllText(Path.Combine(fixture.Runner.WorkRootPath, "retained", "retained", "bin", "generated.txt")));
    }

    [Fact]
    public void Maintain_SpecialTemporaryFileDoesNotBlockOtherCleanup()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new Installation();
        var pipe = Path.Combine(fixture.Runner.TempPath, "runtime.fifo");
        RunnerMaintenanceProcesses.Run("mkfifo", fixture.Root, pipe);
        var stale = Path.Combine(fixture.Runner.TempPath, "clr-debug-pipe-2147483647-0-in");
        var live = Path.Combine(fixture.Runner.TempPath, "clr-debug-pipe-" + Environment.ProcessId + "-0-in");
        RunnerMaintenanceProcesses.Run("mkfifo", fixture.Root, stale);
        RunnerMaintenanceProcesses.Run("mkfifo", fixture.Root, live);
        var disposable = Path.Combine(fixture.Runner.TempPath, "output.txt");
        File.WriteAllText(disposable, "completed output");
        var clock = Stopwatch.StartNew();
        var result = fixture.Service.Maintain(fixture.Spec, dryRun: false);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
        Assert.True(File.Exists(pipe));
        Assert.True(File.Exists(live));
        Assert.False(File.Exists(stale));
        Assert.False(File.Exists(disposable));
        Assert.Contains(result.Retained, path => path.StartsWith(pipe));
    }

    [Fact]
    public void InstallMaintenance_RequiresCapturedServiceHookWhenEnvOmitsIt()
    {
        using var fixture = new Installation();
        fixture.PreparePayload();
        fixture.Runner.InheritedStartedHook = null;
        var environment = Path.Combine(fixture.Runner.RootPath, ".env");
        File.WriteAllText(environment, "CUSTOM_VALUE=keep\n");
        var old = Environment.GetEnvironmentVariable("ACTIONS_RUNNER_HOOK_JOB_STARTED");
        try
        {
            Environment.SetEnvironmentVariable("ACTIONS_RUNNER_HOOK_JOB_STARTED", Path.Combine(fixture.Root, "installer-only.ps1"));
            var error = Assert.Throws<InvalidOperationException>(() => fixture.Service.InstallMaintenance(fixture.Spec));
            Assert.Contains("registered service", error.Message);
            Assert.Equal("CUSTOM_VALUE=keep\n", File.ReadAllText(environment));
            Assert.False(File.Exists(Path.Combine(fixture.Spec.StateRootPath, "policy.json")));
        }
        finally { Environment.SetEnvironmentVariable("ACTIONS_RUNNER_HOOK_JOB_STARTED", old); }
    }

    [Fact]
    public void InstallMaintenance_LateMetadataFailureRestoresPreviousEnrollment()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Installation();
        fixture.PreparePayload();
        fixture.Service.InstallMaintenance(fixture.Spec);
        var paths = new[] { Path.Combine(fixture.Runner.RootPath, ".env"), Path.Combine(fixture.Spec.StateRootPath, "policy.json"),
            Path.Combine(fixture.Spec.StateRootPath, "scheduled.json"), Path.Combine(fixture.Spec.StateRootPath, "installed.json"),
            Path.Combine(fixture.Spec.StateRootPath, "Invoke-RunnerMaintenance.ps1") };
        var original = paths.ToDictionary(path => path, File.ReadAllBytes);
        fixture.Runner.ProtectedWorkspaces = new[] { "new-hold" };
        using var blocker = new FileStream(paths[3], FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => fixture.Service.InstallMaintenance(fixture.Spec));
        foreach (var path in paths) Assert.Equal(original[path], File.ReadAllBytes(path));
        Assert.False(File.Exists(Path.Combine(fixture.Spec.StateRootPath, "installation.pending.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Maintain_UsesCapturedProcessLifetimeForSurvivingJobActivity(bool sameLifetime)
    {
        using var fixture = new Installation();
        var windows = OperatingSystem.IsWindows();
        using var background = Process.Start(new ProcessStartInfo(windows ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sleep",
            windows ? "/d /c ping -n 60 127.0.0.1 >nul" : "60") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var ticks = background.StartTime.ToUniversalTime().Ticks;
            var state = new RunnerMaintenanceState();
            var job = new RunnerMaintenanceJob { RunnerRoot = fixture.Runner.RootPath,
                Workspace = Path.Combine(fixture.Runner.WorkRootPath, "Example"), WorkerId = background.Id, WorkerStartTicks = ticks - 1 };
            job.Processes.Add(new RunnerMaintenanceProcessIdentity { Id = background.Id, StartTicks = sameLifetime ? ticks : ticks - 1 });
            state.Jobs.Add(job);
            Directory.CreateDirectory(fixture.Spec.StateRootPath);
            File.WriteAllText(Path.Combine(fixture.Spec.StateRootPath, "state.json"), JsonSerializer.Serialize(state, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceState));
            var output = Path.Combine(fixture.Runner.TempPath, "output.txt");
            File.WriteAllText(output, "completed output");
            var result = fixture.Service.Maintain(fixture.Spec, dryRun: false);
            Assert.True(result.Success, string.Join("\n", result.Errors));
            Assert.Equal(sameLifetime, File.Exists(output));
        }
        finally
        {
            if (!background.HasExited) background.Kill(entireProcessTree: true);
            background.WaitForExit();
        }
    }

    [Fact]
    public void Maintain_RecognizesInheritedHookMarkerBeforeCurrentWorkerAndResumesAfterItsExit()
    {
        using var fixture = new Installation();
        var windows = OperatingSystem.IsWindows();
        var marker = Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(windows ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sleep",
            windows ? "/d /c ping -n 60 127.0.0.1 >nul" : "60") { UseShellExecute = false, CreateNoWindow = true };
        start.Environment[RunnerMaintenanceProcesses.JobMarker] = marker;
        using var background = Process.Start(start)!;
        try
        {
            Assert.True(SpinWait.SpinUntil(() =>
            {
                try { return RunnerMaintenanceProcesses.ReadJobMarker(background.Id) == marker; }
                catch (IOException) { return false; }
            }, TimeSpan.FromSeconds(5)), "Marked fixture process did not finish native startup.");
            var state = new RunnerMaintenanceState();
            var job = new RunnerMaintenanceJob { RunnerRoot = fixture.Runner.RootPath,
                Workspace = Path.Combine(fixture.Runner.WorkRootPath, "Example"), WorkerId = int.MaxValue,
                WorkerStartTicks = background.StartTime.ToUniversalTime().Ticks + TimeSpan.TicksPerSecond,
                TrackingStartTicks = background.StartTime.ToUniversalTime().Ticks - 1 };
            job.TrackingIds.Add(marker);
            state.Jobs.Add(job);
            Directory.CreateDirectory(fixture.Spec.StateRootPath);
            File.WriteAllText(Path.Combine(fixture.Spec.StateRootPath, "state.json"), JsonSerializer.Serialize(state, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceState));
            var output = Path.Combine(fixture.Runner.TempPath, "output.txt");
            File.WriteAllText(output, "live helper data");
            var held = fixture.Service.Maintain(fixture.Spec, dryRun: false);
            Assert.True(held.Success, string.Join("\n", held.Errors));
            Assert.True(File.Exists(output));
            background.Kill(entireProcessTree: true);
            background.WaitForExit();
            var resumed = fixture.Service.Maintain(fixture.Spec, dryRun: false);
            Assert.True(resumed.Success, string.Join("\n", resumed.Errors));
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (!background.HasExited) background.Kill(entireProcessTree: true);
            background.WaitForExit();
        }
    }

    [Fact]
    public void Maintain_ConcurrentSchedulerDefersWithoutWaitingOrDeleting()
    {
        using var fixture = new Installation();
        Directory.CreateDirectory(fixture.Spec.StateRootPath);
        var path = Path.Combine(fixture.Runner.TempPath, "job.txt");
        File.WriteAllText(path, "keep");
        using var gate = new FileStream(Path.Combine(fixture.Spec.StateRootPath, "admission.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var result = fixture.Service.Maintain(fixture.Spec, dryRun: false);
        Assert.True(result.Success);
        Assert.True(result.Deferred);
        Assert.Empty(result.RemovedPaths);
        Assert.Equal("keep", File.ReadAllText(path));
    }

    private sealed class Installation : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "powerforge-maintenance-" + Guid.NewGuid().ToString("N"));
        internal readonly RunnerMaintenanceSpec Spec;
        internal readonly RunnerMaintenanceRegistration Runner;
        internal readonly RunnerHousekeepingService Service = new(new NullLogger(), ReadOwnedProcesses);

        internal static void CopyRunnerProcess(string executable)
        {
            File.Copy(OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sleep", executable);
            if (OperatingSystem.IsWindows()) return;
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (OperatingSystem.IsMacOS())
                RunnerMaintenanceProcesses.Run("/usr/bin/codesign", Path.GetDirectoryName(executable)!, "--force", "--sign", "-", executable);
        }

        private static Dictionary<int, RunnerMaintenanceProcess> ReadOwnedProcesses(out bool incomplete)
        {
            var native = RunnerMaintenanceProcesses.Read();
            var owner = Environment.ProcessId;
            incomplete = false;
            return native.Where(entry => RunnerMaintenanceProcesses.Ancestors(native, entry.Key).Contains(owner))
                .ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        internal Installation()
        {
            Runner = new RunnerMaintenanceRegistration
            {
                RootPath = Path.Combine(Root, "runner"), WorkRootPath = Path.Combine(Root, "runner", "_work"),
                TempPath = Path.Combine(Root, "job-temp"), AgentId = 42, Name = "fixture", GitHubUrl = "https://github.com/example",
                InheritedStartedHook = string.Empty, InheritedCompletedHook = string.Empty
            };
            Directory.CreateDirectory(Runner.WorkRootPath);
            Directory.CreateDirectory(Runner.TempPath);
            File.WriteAllText(Path.Combine(Runner.RootPath, ".runner"), "{\"agentId\":42,\"agentName\":\"fixture\",\"gitHubUrl\":\"https://github.com/example\",\"workFolder\":\"_work\"}");
            Spec = new RunnerMaintenanceSpec { StateRootPath = Path.Combine(Root, "maintenance"), Runners = new[] { Runner }, MinimumFreeGiB = 0, TargetFreeGiB = 0 };
        }

        internal void PreparePayload()
        {
            Directory.CreateDirectory(Spec.StateRootPath);
            Spec.ExecutablePath = Path.Combine(Spec.StateRootPath, "PowerForge.Cli");
            File.WriteAllText(Spec.ExecutablePath, "prebuilt placeholder");
        }

        public void Dispose()
        {
            if (!Directory.Exists(Root)) return;
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.Delete(Root, recursive: true);
        }
    }
}
