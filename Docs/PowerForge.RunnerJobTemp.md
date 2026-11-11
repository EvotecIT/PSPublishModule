# Automatic Windows runner temporary cleanup

Windows service runners need separate handling for temporary files. Current .NET
uses `GetTempPath2`: a SYSTEM process normally writes to `Windows\SystemTemp`,
outside GitHub's managed `RUNNER_TEMP` directory. Setting only `TEMP` and `TMP`
does not redirect that path. See [Microsoft's GetTempPath2 documentation](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-gettemppath2a).

`Build/RunnerMaintenance/Install-RunnerJobTempCleanup.ps1` configures one runner
with a private directory under `Windows\SystemTemp\PowerForge.RunnerJobs` and
sets `TEMP`, `TMP` and `SystemTemp` in that runner's `.env`. It installs the same
bounded cleanup hook at job start and completion. Completion removes disposable
job temporary files; the next start recovers leftovers from an interrupted job.
The directory and its SYSTEM/administrators permissions remain in place.

The shared NuGet scratch/lock directory is passed explicitly and survives every
cleanup. Package caches, installed modules and SDKs, runner workspaces, other
runner installations and existing system temporary files are outside this hook's
scope. Existing leftovers need their own source and activity qualification.

## Installation

First resolve the runner's actual registration, service and existing NuGet scratch
path. Preview the change:

```powershell
& .\Build\RunnerMaintenance\Install-RunnerJobTempCleanup.ps1 `
    -RunnerRoot C:\actions-runner `
    -NuGetScratchRoot C:\Windows\SystemTemp\NuGetScratch `
    -ExpectedOrganization EvotecIT
```

Before applying, establish that the exact runner is idle through GitHub and native
worker/process checks, take its maintenance reservation/lock, and stop only that
runner's service through the approved admission procedure. Apply the reviewed
script with the same arguments and `-Mode Apply`, then restore the original service
state in `finally`. The installer refuses to run with a listener or worker still
using that installation. It preserves an existing hook instead of replacing it.

The scripts and local configuration live under
`Program Files\PowerForge.RunnerMaintenance\<runner-name>`, outside the runner
application and checkout. GitHub requires a restart after `.env` changes. See
[GitHub's runner hook documentation](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/run-scripts).

The installer validates trusted ownership and namespace permissions along the
installation ancestry and creates private directories with their DACL at creation.
It preserves hooks configured through the machine, SYSTEM account or exact service
environment as well as `.env`. A failed installation rolls back its owned files
and original environment; a retained preparing record has a supported Restore path.

The hook checks its registration, installation ownership, private-directory
permissions and runner-worker ancestry before deleting anything. Reparse points
and locked files are retained. Its child process has a 45-second limit; a timeout
stops only that child and reports retained output. Cleanup warnings remain visible
in the job log without replacing the job's test result.

## Verification and rollback

Installation is separate from successful operation. Observe a real job under the
runner's SYSTEM identity: verify `Path.GetTempPath()` resolves to its private root,
create a disposable marker there, and confirm the completion hook removes it.
Check that the shared NuGet lock path and another runner's marker survive. Inspect
the start/completion receipts in the job log and compare disk space before/after.

To roll back, admit the idle runner and stop its service again. Run the installer
with the original arguments and `-Mode Restore`, then restore its service state.
The original `.env` bytes or original absence are restored. If `.env` changed after
installation, rollback stops rather than overwriting later edits. The private
backup and temporary data are retained for inspection.

This job-level cleanup complements the existing scheduled runner housekeeping;
it adds no scheduler. Broad workspace/tool cleanup and Windows Update installation
retain their separate configuration, retention, activity and servicing rules.

The scheduled housekeeping config supports `Runner.ProtectedWorkspaces`, a map
from machine name to top-level workspace directory names. These directories are
excluded before age qualification, including when `WorkspacesRetentionDays` is
zero. A hold on one machine does not retain another machine's completed checkout.
Private fleet holds can instead be kept in that runner's `.env` as
`POWERFORGE_RUNNER_PROTECTED_WORKSPACES=["retained-checkout"]`. The core combines
this machine-local list with configured holds. Invalid local JSON fails before
cleanup. Keep private workspace names in machine-local configuration.
