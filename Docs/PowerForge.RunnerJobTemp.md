# Automatic Windows runner temporary cleanup

Windows service runners need separate handling for temporary files. Current .NET
uses `GetTempPath2`: a SYSTEM process normally writes to `Windows\SystemTemp`,
outside GitHub's managed `RUNNER_TEMP` directory. Setting only `TEMP` and `TMP`
does not redirect that path. See [Microsoft's GetTempPath2 documentation](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-gettemppath2a).

`Build/RunnerMaintenance/Install-RunnerJobTempCleanup.ps1` configures one runner
with a private directory at `%SystemRoot%\SystemTemp\PFJ\<registered-agentId>` and
sets `TEMP`, `TMP` and `SystemTemp` in that runner's `.env`. It installs the same
bounded cleanup hook at job start and completion. Completion removes disposable
job temporary files; the next start recovers leftovers from an interrupted job.
The directory and its SYSTEM/administrators permissions remain in place.

The compact root leaves space for generated module-loader paths used by legacy
Windows `MAX_PATH` file APIs, including Windows PowerShell 5.1. It does not require
enabling Windows long paths. The Windows directory follows `SystemRoot`; the final
component is the current `.runner` registration's positive decimal `agentId`,
validated by both the installer and cleanup worker. Runner names remain in the
installation directory and configuration; they do not lengthen the job temp root.

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

The manual `Qualify Windows runner temp hooks` workflow provides a short SYSTEM
canary on one exact fleet label. Before dispatch, choose a unique 32-character
lowercase hexadecimal probe ID and seed `Canary-<id>.start.tmp` in that runner's
private root and `Canary-<id>.shared.tmp` in its shared NuGet scratch directory.
The job reads the installed configuration and its runner's `.runner` registration,
checks the name and ID against the compact root, and verifies start cleanup, the
shared control and both PowerShell temp paths. Windows PowerShell 5.1 and PowerShell
7 each create and read synthetic generated module-loader output with ordinary
file APIs under the actual private root, without an SDK or package dependency.
The probe follows the generated loader's nested project and assembly-name layout.
The job leaves that output and a completion marker for the completion hook.
After the job, independently verify that `Canary-<id>.complete.tmp` and the synthetic
output are gone and the shared marker survives. Remove only the owned shared
marker. A green job without that post-job check is incomplete proof.

To roll back, admit the idle runner and stop its service again. Run the installer
with the original arguments and `-Mode Restore`, then restore its service state.
The original `.env` bytes or original absence are restored. If `.env` changed after
installation, rollback stops rather than overwriting later edits. The private
backup and temporary data are retained for inspection.

Existing installations using `Windows\SystemTemp\PowerForge.RunnerJobs\<runner-name>`
need an idle restore with the **original installer revision** before replacement.
The replacement installer derives the compact root and cannot restore the earlier
layout's ownership record. The existing Restore retains its private original
environment backup, installed files and temporary data; preserve that original
backup during the bounded replacement so rollback still reaches the pre-hook
environment. Resolve the retained installation and recovery record before applying
the replacement, using the same idle admission and service-state safeguards.

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
