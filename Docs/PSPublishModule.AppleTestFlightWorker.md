# TestFlight builds after merges

PowerForge provides a request-only GitHub button and a trusted local Mac worker
for TestFlight builds. Enroll an internal or external group on the Mac. Automatic builds start **Off**. Choose **Selected
PRs** to build merged PRs labelled `testflight`, or **Every merged PR** to request
a build after each eligible merge. Configure `ReleaseHoldLabel` to use
`testflight-skip` as a not-ready exception that holds releases containing that PR.
The label cannot remove code already merged into main. **Build now** works while
automatic mode is Off.

The source is latest qualified main containing the requested merged PR. This
does not promise a binary for each historical merge SHA or an unmerged PR preview.
Every request has durable local history. Commit statuses show the actual source,
stage, version/build, included PRs, and observed automatic mode. The Actions job
says **request recorded**; the Mac later reports **ReadyInHome** for an internal
group or **ReadyInTestFlight** for an external group only when every selected
platform has a valid build available to that group.

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
write permission. Invalid or failed attempts are recorded with a diagnostic and do
not block later requests. An incomplete attempt holds request ordering for at most
ten minutes. A successful rerun of a rejected attempt remains eligible; an accepted
run is applied only once across reruns. Transient observation failures retain the
request for retry and close the active mutation gate. Keep the Mac polling within
the artifact retention period.

## Enroll the trusted Mac

Use separate clean owner and application checkouts. The owner stays at its exact
reviewed pin; the application's worker checkout stays on its configured default branch and advances only
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
from `~/.appstoreconnect/env`. Aliases can reference an earlier canonical issuer,
key id, or key-path assignment with `$NAME` or `${NAME}`, for example
`ASC_KEY_ID="$APP_STORE_CONNECT_KEY_ID"`. Paths can start with `$HOME/` or
`${HOME}/`. The reader rejects other variable expressions and does not execute
shell code. Existing validated
credential environment variables remain supported without that switch. Never
copy Apple credentials into GitHub secrets or a LaunchAgent plist.

The local policy is trusted operator configuration. Its paths, target identities,
check names and group id are not taken from GitHub request inputs. For example:

```json
{
  "Repository": "owner/application",
  "DefaultBranch": "main",
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
  "BetaGroupName": "Home",
  "BetaGroupIsInternal": true,
  "SubmitBetaReviewAutomatically": false,
  "ReleaseHoldLabel": "testflight-skip"
}
```

Set enrollment time and baseline to the actual current configured default branch when enrolling.
For a repository whose default branch is `master`, set `DefaultBranch` to
`master` and pass `-ExpectedConsumerBranch master` to the pinned operator.
The worker verifies the enrolled branch against GitHub and uses it for requests,
qualification, fast-forward updates, and version checkpoints. Existing policies
default to `main`.
Use the exact successful job names in the application's default-branch push qualification
workflow, including matrix suffixes. The worker verifies that workflow's latest
exact-source push run and all enrolled jobs succeeded. PR-head success and skipped
jobs do not qualify another merged commit. Qualification reuse across behavior
changes is outside this lane.

In the release configuration, name the enrolled group explicitly in
`AppleApps.TestFlightBetaGroupIds`. Match each selected target’s
`TestFlightPolicy` to the enrolled audience: `Internal` when
`BetaGroupIsInternal` is true (the default), or `External` when it is false. Use
durable receipts, resume and processing waits. Do not configure tester creation
in this lane. `Ship` receives only `--apple-testflight-target` and
excludes store selection, screenshots, public metadata and review submission.

For external testers, enroll the existing external group by its exact id and
name. Internal groups that already receive all builds retain that access.
External readiness requires Apple’s external testing state and read-back of the
exact build in the enrolled group; an uploaded build alone is insufficient.
For unattended external delivery, explicitly set the trusted local policy's
`SubmitBetaReviewAutomatically` to `true`. That grants standing authority to
submit only the enrolled uploaded builds to **Beta App Review**. The worker
observes each platform, submits only builds that still need review, waits for
Apple, then assigns the exact approved builds to the enrolled group and verifies
their access. Pending submissions are observed without duplicate submission.
Public App Store review and public releases remain deliberate local operations.
The worker does not invite testers, change membership, enable public links, invent
export-compliance answers, or retry rejected builds as new candidates. Missing
compliance, rejection, expired builds, or uncertain mutation outcomes hold for an
operator. Existing policies default to `false` and retain manual beta review.

Run a bounded pass through the existing pinned operator:

```text
pwsh -NoProfile -File <owner>/scripts/Invoke-PinnedPowerForge.ps1 -ExpectedCommit <owner-sha> -ConsumerRoot <worker-checkout> -ExpectedConsumerRepository <owner/application> -ExpectedConsumerBranch <main-or-master> -LoadFixedLocalAppleProfile apple-testflight run --policy <absolute-local-policy.json> --config <enrolled-config-path> --output json
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
Removing `testflight` in Selected PRs mode cancels that
unstarted trigger without blocking other eligible PRs. Its request history remains
recorded; a source-bound cancelled job receives a terminal `Excluded` error status.
Failed status writes are retried from durable state. Use Build now to request the
trigger again. Turning automatic mode Off preserves
unstarted intent for the next time automatic mode is enabled.

For default release after merges, deliberately choose **Automatic: Every merged
PR** in the request workflow. The trusted Mac can also record the same mode
without a GitHub request artifact, using its pinned entrypoint:

```text
pwsh -NoProfile -File <owner>/scripts/Invoke-PinnedPowerForge.ps1 -ExpectedCommit <owner-sha> -ConsumerRoot <worker-checkout> -ExpectedConsumerRepository <owner/application> -ExpectedConsumerBranch <main-or-master> apple-testflight mode --automatic-mode EveryMergedPR --confirm-mode-change --policy <absolute-local-policy.json> --config <enrolled-config-path> --output json
```

`mode` records intent and preserves enrollment, requests, reservations and queue
history. It does not build or release anything. `Off` and `SelectedPRs` are also
accepted. A newly selected EveryMergedPR mode covers merges after that intent;
it does not replay every historical PR.
The local mode command takes the queue writer lease, so run it between worker
passes. During an active delivery, local `pause` remains available immediately;
the request workflow can also record an Off intent for the active worker to read.

## Not-ready PR exception

Set `ReleaseHoldLabel` to `testflight-skip` in the local policy. Apply that PR label
before merging a change that must not reach testers. The worker retains the
request and holds any build containing the labelled merged PR, including later
merges and manual Build now requests. Older already-uploaded builds that do not
contain the change can finish their beta review and distribution.

After the correction or revert is merged, remove the label from the original PR
to acknowledge that main is ready. The queued intent resumes against qualified
main; no replacement request or manual version number is needed. The label is
checked again at mutation boundaries and during long-running children. An upload
already underway cannot be recalled; add the label before merging whenever possible.
Unknown ancestry or an unavailable label read closes the mutation gate.

Without `ReleaseHoldLabel`, the legacy `testflight-skip` behavior still cancels
only its automatic trigger and requires Build now to request it again. It cannot
keep that code out of a later build. Use the configured hold for not-ready code.

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
A restart before checkpoint publication admits the pending diff only when its
saved `CheckpointReady` job matches the original current main, integrity-validated
completed receipt, approved plan hash, target set and version/build. The strict
entrypoint admits only those validated publisher paths. Other release commands
retain their normal clean-source requirement.
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
A positive child-command app-lease rejection means no mutation started and is
retryable, including when another checkout acquired the lease after preview.
A closed or expired gate at child entry is a definitive pre-mutation rejection
and retains the retryable stage, just like a busy app lease. A gate rejection
after release work has started, an unrelated state I/O failure, or an interrupted
or otherwise ambiguous mutation remains NeedsOperator. Off or pause also holds
an unpublished local version checkpoint. If the checkpoint was already committed
when the stop arrived, its exact commit remains held for operator reconciliation.
A NeedsOperator job holds the queue, including later manual requests. After
inspecting and reconciling the canonical receipts and remote delivery, use the
same local operator with `apple-testflight retire --job <id> --confirm-reconciled
--policy <policy> --config powerforge.release.json --output json`. This preserves
the held intent as Excluded and permits a new request. Retirement is a local
operator acknowledgement; it does not cancel or roll back an Apple delivery.
The worker never assumes an interrupted upload failed or blindly allocates a
replacement. With automatic beta review enabled, once every enrolled platform
has positive upload evidence, processing recovery observes the same uploaded
source even when main advances. A timeout before a later platform's upload keeps
ordinary Ship recovery and its latest-main gate; it cannot silently abandon that
platform or use beta commands to upload it. Only Status, TestFlight and
SubmitTestFlightReview use that clean ancestor with an explicit full source SHA;
Ship, archive, upload and public store actions retain latest-main requirements.
Beta mutations retain their own approved plan hash and exact per-platform build
IDs. A restart can acknowledge a matching integrity-validated completed canonical
receipt; an interrupted action without that receipt requires reconciliation.
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
set `@@DEFAULT_BRANCH@@` and `@@CONFIG_PATH@@` to the policy's exact `DefaultBranch`
and `ConfigPath`, and create its scratch `tmp` directory before installation.
Set `@@LOG_ROOT@@` to an existing local log directory, normally
`~/Library/Logs/PowerForge`, so launchd can open its output before the external
development volume is available. Installation and activation
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

Request scans retain a durable discovery cursor and pending run IDs, so completed
history does not grow each pass. Failed or stalled attempts are checked for
reruns in rotating batches of twenty per pass; successful accepted runs are not
polled again. Fresh requests are discovered independently of that historical
retry batch. Journal upgrades retain the consumed frontier and recover outstanding
runs before advancing it. Mode requests retain their intent time and run ID, so a
late rerun cannot override a newer Off request; Build now remains eligible.
Initial enrollment uses a bounded scan; choose an enrollment time with fewer than
10,000 new runs.
