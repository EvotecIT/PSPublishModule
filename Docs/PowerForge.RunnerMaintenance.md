# Host-local runner maintenance

PowerForge keeps self-hosted GitHub runners within bounded storage retention through a local hourly schedule and job lifecycle hooks. The installed payload lives outside runner workspaces and runs directly from prebuilt files.

I use one policy per host, including every runner registration that shares its caches. Each registration has a private job-temp directory. NuGet scratch locks remain shared across those registrations.

## Install

Build a self-contained CLI for the host's platform and copy its complete publish directory into a private maintenance directory. Prepare a machine-local policy with absolute physical paths.

```json
{
  "stateRootPath": "/var/lib/powerforge-runner-maintenance",
  "executablePath": "/var/lib/powerforge-runner-maintenance/payload/PowerForge.Cli",
  "runners": [
    {
      "rootPath": "/opt/actions-runner",
      "tempPath": "/var/tmp/powerforge-runner-jobs/primary",
      "inheritedStartedHook": "",
      "inheritedCompletedHook": "",
      "protectedWorkspaces": []
    }
  ],
  "nuGetPackagesPath": "/root/.nuget/packages",
  "nuGetHttpCachePath": "/root/.local/share/NuGet/http-cache",
  "nuGetScratchPath": "/tmp/NuGetScratchroot",
  "nuGetConfigPaths": ["/root/.nuget/NuGet/NuGet.Config"],
  "minimumFreeGiB": 20,
  "targetFreeGiB": 30,
  "workspaceRetentionDays": 3,
  "nuGetRetentionDays": 30,
  "deepIntervalHours": 24,
  "maximumRunSeconds": 90,
  "admissionTimeoutSeconds": 120
}
```

Drain the participating registrations and stop their idle services. Run the installer under their administrative service identity, then restore the services.

```powershell
./Build/RunnerMaintenance/Install-RunnerMaintenance.ps1 -Configuration /var/lib/powerforge-runner-maintenance/enrollment.json
```

On macOS, pass `-RunAsUser` with the existing runner account. Windows uses LocalSystem and Task Scheduler. Linux uses a systemd timer. macOS uses a launch daemon. Each schedule runs hourly and after startup.

Enrollment captures the installed agent identity and actual work directory from `.runner`, preserves existing custom hooks, enables NuGet package last-access metadata in the specified service configuration, and updates the runner's `.env`. Private temp, HTTP-cache and .NET bundle extraction paths follow the runner's job-temp directory. `NUGET_SCRATCH` remains the shared path from the policy.

Capture inherited hooks from each registered service before stopping it. Windows service environment values, systemd environment files and launchd environment settings can differ from an administrative shell. An explicit `.env` assignment takes precedence. When `.env` omits a hook, supply its captured `inheritedStartedHook` or `inheritedCompletedHook` value. An empty string records that the service has no inherited hook. Enrollment retains the existing configuration when that value is unknown.

## Run and inspect

The enrolled policy includes the captured identities. A direct invocation defaults to a dry run.

```powershell
powerforge github runner maintenance --config /var/lib/powerforge-runner-maintenance/policy.json
powerforge github runner maintenance --config /var/lib/powerforge-runner-maintenance/policy.json --apply
```

The result reports actual removed paths and logical bytes, free bytes before and after, deferred work, retained targets and errors. The state directory keeps four recent JSON receipts, persistent due/pressure state, and any interrupted quarantine journals.

Job hooks verify their enrolled `Runner.Worker` ancestry and GitHub job context. Start hooks enter the same admission lock as the timer. Completion hooks run after job steps and post-actions. A busy registration protects its work and temporary output. Shared caches wait until the host's relevant workers and build activity are idle. An unenrolled listener holds shared-cache cleanup.

The maintenance budget is checked between filesystem operations. The PowerShell launcher bounds its owned child process to four minutes. Windows Task Scheduler and systemd also bound the scheduled execution. Cleanup errors remain visible in receipts. A start hook that fails to acquire admission stops job startup.

Preserved custom hooks retain their foreground duration, output, exit status and background processes. The launcher enters admission once before executing a custom hook, then its live worker protects that job until the maintenance phase. Cleanup for that registration waits for worker exit when a custom hook is configured. The four-minute launcher budget applies to the maintenance child.

## Retention

| Storage | Treatment |
| --- | --- |
| Private job temp | Reconcile at job boundaries and idle timer passes. Preserve package, key and database payloads requiring a specific producer contract. Keep live runtime IPC and retire recognized abandoned runtime pipes. |
| Runner `_temp` | Retire closed entries older than one day after the worker exits. Current hook file-command storage stays available through runner finalization. |
| CI project output | Remove untracked `bin`, `obj`, `TestResults` and artifact staging beneath recorded CI workspaces with a matching source revision. Preserve protected workspace families and sensitive payloads. |
| Completed checkout | After three days, retire recorded, unchanged, detached, clean CI checkouts. Preserve modified files, branches, nested repositories and redirected paths. |
| Runner diagnostics | Remove closed logs older than seven days. |
| Legacy generated fixtures | Match explicit producer prefixes, GUID identities, allowed files/directories and minimum age from the local policy. |
| NuGet HTTP downloads | Retire legacy `.dat` files older than thirty minutes. New downloads use job-temp storage. |
| Expanded NuGet packages | Combine completion metadata with observed CI project and installed-tool references. Retire complete, known-public versions after thirty days of disuse. Disk pressure below twenty GiB retains a thirty-GiB recovery target until eligible cleanup finishes. Private, incomplete and unknown-source packages are retained. |
| CodeQL cache | Keep the two newest stable versions. Older completed versions become eligible after thirty days. |
| Docker builder cache | Optional `dockerBuilderCacheGiB` enables Docker's own unused-builder-cache pruning after seven days, with the configured retained budget. |

Package eviction uses NuGet's shared per-package lock keyed by the full `.nupkg` path. Directory isolation atomically removes the complete cache entry, including its completion marker. A durable journal records the move before deletion, so interrupted cleanup can resume without rebuilding or restoring the maintenance payload. The NuGet scratch directory must match the enrolled runners.

Browser revisions continue through HtmlTinkerX's installation and repair lifecycle. Installed SDKs, Windows servicing, Docker images, containers and volumes keep their separate maintenance procedures.

The GitHub runner refreshes action downloads at job preparation and retires old runner binaries through its updater. PowerForge preserves those installation lifecycles. The lifecycle hooks use GitHub's `ACTIONS_RUNNER_HOOK_JOB_STARTED` and `ACTIONS_RUNNER_HOOK_JOB_COMPLETED` settings. [GitHub job hooks](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/run-scripts), [action download lifecycle](https://github.com/actions/runner/blob/main/src/Runner.Worker/ActionManager.cs), [runner updater](https://github.com/actions/runner/blob/main/src/Runner.Listener/SelfUpdater.cs)

## Recovery

An interrupted isolated target remains under `.powerforge-quarantine` in its original storage root. Its journal identifies that enrolled root and original path. The next pass validates the record and finishes deletion before selecting new output. A failed target remains observable and independently recoverable.

The private state directory contains the original runner `.env` and NuGet configuration backups. During an idle service window, those files provide the original settings for rollback. Restore only after checking for later intentional edits. Remove the native maintenance schedule when retiring the installation, then verify the runner services and their original hooks.

Enrollment journals the exact previous environments, NuGet files, launcher, policy, scheduled configuration and installation marker before replacing them. A write failure restores the previous enrollment. An interrupted installation leaves `installation.pending.json`, which holds runtime cleanup until the installer replays recovery. Keep the participating services stopped through that recovery. Recorded job processes use their PID and start time together, so PID reuse and a host restart retire obsolete activity associations.
