# TestFlight builds after merges

PowerForge provides a request-only GitHub button and a trusted local Mac worker
for internal TestFlight builds. Automatic builds start **Off**. Choose **Selected
PRs** to build merged PRs labelled `testflight`, or **Every merged PR** to request
a build after each eligible merge. `testflight-skip` excludes an automatic trigger;
the label cannot remove code already merged into main. **Build now** works while
automatic mode is Off.

The source is latest qualified main containing the requested merged PR. This
does not promise a binary for each historical merge SHA or an unmerged PR preview.
Every request has durable local history. Commit statuses show the actual source,
stage, version/build, included PRs, and observed automatic mode. The Actions job
says **request recorded**; the Mac later reports **ReadyInHome** only when every
selected platform has a valid build available to the enrolled internal group.

## Add the button

Commit a caller such as `.github/workflows/testflight.yml` on the application's
default `main` branch. Replace `OWNER_COMMIT` with the reviewed immutable
PSPublishModule commit; the local policy must enroll that same pin.

```yaml
name: TestFlight
on:
  workflow_dispatch:
    inputs:
      operation:
        description: Build latest qualified main, or request an automatic mode change
        type: choice
        required: true
        options:
          - Build now
          - 'Automatic: Off'
          - 'Automatic: Selected PRs'
          - 'Automatic: Every merged PR'
      pull_request:
        description: Optional merged PR number; builds latest qualified main containing it
        type: string
        required: false
permissions:
  contents: read
jobs:
  request:
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-apple-testflight-request.yml@OWNER_COMMIT
    with:
      operation: ${{ inputs.operation }}
      pull_request: ${{ inputs.pull_request }}
```

The workflow runs on GitHub-hosted Linux without checking out application code or
receiving Apple credentials. It retains a small request artifact for 90 days.
The worker accepts only successful default-branch dispatches using the enrolled
shared workflow pin and verifies the request and rerun actors have repository
write permission. Keep the Mac polling within the artifact retention period.

## Enroll the trusted Mac

Use separate clean owner and application checkouts. The owner stays at its exact
reviewed pin; the application's worker checkout stays on main and advances only
by fast-forward. Preserve ordinary development checkouts separately.

The default durable state is the operator user's local application data directory
under `PowerForge/Apple` (`~/Library/Application Support/PowerForge/Apple` on macOS).
Create that private directory before enrollment and use its absolute path as
`StateRoot`; normal manual entrypoints then share it without a shell export.
For another location outside both checkouts, set `POWERFORGE_APPLE_STATE_ROOT` to
the same existing absolute path for **both the worker and manual release
entrypoints**. Keep source, temporary builds and heavy scratch on the configured
mounted development volume. App leases and build reservations are small durable
state and must survive cleanup, checkout changes and worker restarts. Source-bound uploads reserve build numbers
before mutation; subsequent sources advance past all reservations, including
failed deliveries absent from Apple's build list.

Keep credentials in the existing private `~/.appstoreconnect` profile. The pinned
operator's `-LoadFixedLocalAppleProfile` switch reads literal credential assignments
from `~/.appstoreconnect/env`; it does not execute shell code. Existing validated
credential environment variables remain supported without that switch. Never
copy Apple credentials into GitHub secrets or a LaunchAgent plist.

The local policy is trusted operator configuration. Its paths, target identities,
check names and group id are not taken from GitHub request inputs. For example:

```json
{
  "Repository": "owner/application",
  "ConsumerRoot": "/mounted-development/application-worker",
  "OwnerRoot": "/mounted-development/powerforge-pinned",
  "OwnerCommit": "0000000000000000000000000000000000000000",
  "StateRoot": "/Users/operator/Library/Application Support/PowerForge/Apple",
  "ScratchRoot": "/mounted-scratch/application-testflight",
  "AcceptRequestsAfter": "2026-10-12T10:00:00Z",
  "BaselineSourceCommit": "0000000000000000000000000000000000000000",
  "WorkflowPath": ".github/workflows/testflight.yml",
  "QualificationWorkflowPath": ".github/workflows/apple-ci.yml",
  "ConfigPath": "powerforge.release.json",
  "RequiredChecks": ["Swift contracts", "Apple builds"],
  "Targets": [
    { "Name": "Primary App Store surface", "BundleId": "com.example.app", "AppId": "123456789", "Platform": "iOS" },
    { "Name": "Mac Catalyst App Store surface", "BundleId": "com.example.app", "AppId": "123456789", "Platform": "macOS" }
  ],
  "PublisherPaths": ["project.yml", "Application.xcodeproj/project.pbxproj"],
  "BetaGroupId": "00000000-0000-0000-0000-000000000000",
  "BetaGroupName": "Home"
}
```

Set enrollment time and baseline to the actual current main when enrolling.
Use the exact successful job names in the application's main-push qualification
workflow, including matrix suffixes. The worker verifies that workflow's latest
exact-source push run and all enrolled jobs succeeded. PR-head success and skipped
jobs do not qualify another merged commit. Qualification reuse across behavior
changes is outside this lane.

In the release configuration, name the enrolled group explicitly in
`AppleApps.TestFlightBetaGroupIds`. Use `TestFlightPolicy: Internal` for each
selected target, durable receipts, resume and processing waits. Do not configure
tester creation in this lane. `Ship` receives only `--apple-testflight-target` and
excludes store selection, screenshots, public metadata and review submission.

Run a bounded pass through the existing pinned operator:

```text
pwsh -NoProfile -File <owner>/scripts/Invoke-PinnedPowerForge.ps1 -ExpectedCommit <owner-sha> -ConsumerRoot <worker-checkout> -ExpectedConsumerRepository <owner/application> -LoadFixedLocalAppleProfile apple-testflight run --policy <absolute-local-policy.json> --config powerforge.release.json --output json
```

Use `status`, `pause`, or `resume` instead of `run` to inspect or change local
mutation availability. Pause applies to the app's shared local release entrypoints,
including manually queued GitHub requests. Automatic Off affects automatic work;
manual Build now remains available unless locally paused. Changes are checked
before version mutation, each archive, each upload and group assignment. An upload
already underway finishes before the next stage checks the gate.
Status and pause acknowledgement remain available during an active worker pass.
Unstarted automatic intents held by Off or selection labels do not block a later
manual request. A checkpoint or delivery recovery keeps its place in the queue.
Removing `testflight` in Selected PRs mode or adding `testflight-skip` cancels that
unstarted trigger without blocking other eligible PRs. Its request history remains
recorded; use Build now to request it again. Turning automatic mode Off preserves
unstarted intent for the next time automatic mode is enabled.

The worker reads Off requests while a release subprocess runs. If GitHub polling
fails or the worker heartbeat expires, subsequent mutation stages stop. GitHub
authentication needs Actions, pull-request and metadata read access plus commit
status write access. A publisher checkpoint also needs ordinary contents write
access. The worker never changes repository protection or requests hosted reviews.

## Version checkpoints and recovery

The existing Ship engine selects versions, signs, archives, uploads and maintains
immutable recovery receipts. The worker invokes that engine through its strict
pinned operator. It runs full BuildExecution source validation before confirming
an initial plan. Each plan binds source and exact internal target routes.

A version checkpoint may commit only the explicitly enrolled existing publisher
files, changing `MARKETING_VERSION` and `CURRENT_PROJECT_VERSION` values. The worker
checks both the working diff and committed tree, retains original qualification
and checkpoint lineage, and pushes without force only when main is unprotected
and has no applicable rules. Unexpected graph, behavior, dependency or script
changes stop for an operator. It replans against the actual committed source.
Previous-source preview plans are retained under the queue's `previous-plans`
directory before a fresh plan replaces them; they cannot approve another source.
The upload engine binds retained export output, including `ExportOptions.plist`,
to its immutable receipt. Later pinned entries admit those verified bytes as local
recovery evidence. Changed or unbound output requires local reconciliation and
cannot authorize an upload.

If main advances before delivery, the waiting intent returns to qualification
against the new latest main. A processing timeout retains the same source and
build and retries bounded Ship recovery with the original approved intent. A
restart during mutation is **NeedsOperator**: inspect canonical immutable receipts
and remote delivery before resuming with the existing pinned Ship commands.
A NeedsOperator job holds the queue, including later manual requests. After
inspecting and reconciling the canonical receipts and remote delivery, use the
same local operator with `apple-testflight retire --job <id> --confirm-reconciled
--policy <policy> --config powerforge.release.json --output json`. This preserves
the held intent as Excluded and permits a new request. Retirement is a local
operator acknowledgement; it does not cancel or roll back an Apple delivery.
The worker never assumes an interrupted upload failed or blindly allocates a
replacement. Recovery of a delivered historical source after main advances is
also an operator hold under the strict latest-main source policy.
Transient tool failures outside a mutation stage retain that stage and retry on
the next scheduled pass. Terminal validation failures and interrupted mutations
require reconciliation.

Internal group access is read back for the exact build (or verified from the
group's automatic access to all builds). Assignment success alone does not mark
the worker ready. Per-platform outcomes remain in `queue.json`, alongside every request, actor and
run URL. A one-platform failure remains visible and retains recovery evidence;
it does not become ReadyInHome. Fix terminal Apple rejection or signing setup
before requesting another candidate. No unattended provisioning retry loop is
added.

## Scheduling and storage

`Build/AppleTestFlightWorker.plist.template` is a reviewable LaunchAgent template.
Replace its tokens with enrolled absolute paths and pin, XML-escape values, and
create its scratch `tmp` directory before installation. Installation and activation
are deliberate local operator steps. This template starts a bounded pass about
every 15 minutes while the Mac is awake; launchd does not overlap one running job.
The current operator source-builds privately per invocation, so compilation adds
real minutes to each pass. Request acknowledgement therefore takes up to about
15 minutes plus compilation time; active release subprocesses refresh the gate
every 30 seconds. Choose a longer interval for a rarely used machine. There is no daily or 24-hour release timer.

Keep the durable state and canonical release receipts. Request extraction is
bounded and removed after ingestion; the pinned operator removes its private
compiled sandbox after each invocation. Retain archive/export recovery evidence
until the existing release retention policy permits cleanup. Rotate worker logs
through the machine's established storage review; do not treat queue history,
reservations or signing material as disposable cache.
