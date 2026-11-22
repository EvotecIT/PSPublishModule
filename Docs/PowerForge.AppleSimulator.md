# Apple simulator validation sessions

`powerforge apple-simulator` runs bounded foreground validation and releases a
simulator that the session boots. It preserves devices that were already booted,
their apps and sign-ins, and other simulators on the Mac.

```bash
powerforge apple-simulator run --owner "ci-run-123" --device <UUID> \
  --timeout-seconds 1800 --output json -- \
  xcodebuild test -scheme MyApp -destination "platform=iOS Simulator,id=<UUID>" \
    -parallel-testing-enabled NO -maximum-concurrent-test-simulator-destinations 1
```

Select an exact UUID with `xcrun simctl list devices --json`. Names, `booted`, and
`all` are rejected. The session accepts a stable `Shutdown` or `Booted` device.
It boots a shutdown device, waits for readiness, runs the command, and verifies
owned shutdown. Failed or ambiguous boot attempts are reported without claiming
authority to shut down a device another actor may have booted.

Use `--require-shutdown` for disposable capture or installation fixtures that
must not reuse an active device. PowerForge checks this requirement under the
shared coordination lock, before boot, readiness, or foreground execution. A
pre-existing booted device is rejected without changing it or replacing the
previous ownership receipt. The default still permits deliberate reuse.

Automation can set `POWERFORGE_APPLE_SIMULATOR_REQUIRE_SHUTDOWN=1` to enforce
the same requirement for every CLI `run`, including callers that omit the flag.
An unset or empty value preserves the default; other values fail before the
session starts. JSON run results record `requireInitialShutdown` alongside the
ownership receipt so capture provenance can verify the effective requirement.
The persisted receipt format stays compatible with older operators. The Apple
screenshot workflow sets this policy for configured simulator captures and
requires matching guarded results.

Successful JSON command responses include `tool.cliVersion` and
`tool.engineVersion` from the executing assemblies' informational versions.
Managed builds also report `tool.cliModuleId` and `tool.engineModuleId` from the
loaded modules. The screenshot workflow compares these with the tool it built,
requires both source revisions to match its pinned checkout, and records the
verified identities with each session. Missing or different identities fail
capture validation, including different builds from the same source revision.
Shared ownership receipts remain unchanged.

Boot/discovery/readiness commands default to 120 seconds each, command execution
to 1800 seconds, and the entire release attempt to 30 seconds. Override them with
`--simulator-timeout-seconds`, `--timeout-seconds`, and `--cleanup-timeout-seconds`.
Validation output is capped at 16,384 characters per stream. The JSON result
reports output truncation, primary failure, and cleanup failure separately.
Log truncation is informational and does not change a successful command's exit
status; simulator discovery must return complete valid output.
Ctrl-C and SIGTERM cancel validation while allowing an independently bounded
cleanup attempt. Forced process termination can still bypass that attempt.

The child runs as a foreground operation. PowerForge terminates descendants
remaining in its owned process scope before returning, including after command
exit. Programs that deliberately detach into another Unix session/process group
are outside that cooperative scope; do not launch persistent services through
this validation wrapper.

## Capture on an iPhone Duo

App Store Connect has separate screenshot sets for the iPhone Duo: the outer
display at 1398x2034 and the inner display at 2007x2853, in portrait or
landscape. `--duo-pose` makes sure the right display is live before the
command starts:

```bash
powerforge apple-simulator run --owner "local-capture/duo-inner" --device <UUID> \
  --require-shutdown --duo-pose open --duo-orientation portrait --output json -- \
  ./scripts/capture.sh
```

| Option | Values | Default |
| --- | --- | --- |
| `--duo-pose` | `closed` (outer display) or `open` (inner display) | not set: the device is left as it is |
| `--duo-orientation` | `any`, `portrait` or `landscape` | `any` |
| `--duo-fold-tool` | path to the pinned duoctl executable | `POWERFORGE_DUO_FOLD_TOOL`, else none |
| `--duo-pose-timeout-seconds` | how long to wait for a person to set the pose | 300 |

After boot, PowerForge takes a screenshot of the target display with
`xcrun simctl io <UUID> screenshot --display=primary` (outer) or
`--display=primary-1` (inner). The display counts as live when the screenshot
isn't black and has the display's App Store size in an accepted orientation.
The command then runs with:

- `POWERFORGE_DUO_POSE`: `closed` or `open`
- `POWERFORGE_DUO_DISPLAY`: `primary` or `primary-1`, for `simctl io screenshot --display`
- `POWERFORGE_DUO_SIZE`: the live display's size, such as `2007x2853`

Capture with `xcrun simctl io <UUID> screenshot --display="$POWERFORGE_DUO_DISPLAY"`
and compare the file's size with `POWERFORGE_DUO_SIZE`. The JSON result's
`duoPose` records the size, orientation and whether the pose was `already`
live, set by the `fold-tool`, or set by a person after a `prompt`.

### Set the pose by hand

Xcode 27 has no public command for the Duo's hinge: `simctl` and `devicectl`
can't fold the simulator, and powering a display off doesn't move iOS to the
other one. Without a fold tool, PowerForge prints which pose and orientation to
choose in Device Hub to standard error, then checks the display every second
until it's live. Nothing else is needed: no marker file, no key press. A
freshly booted Duo starts closed in portrait, so an outer-display capture
usually doesn't need a prompt.

### Fold automatically with duoctl (opt-in)

[duoctl](https://github.com/skarol/duoctl) (MIT) folds and rotates the Duo
simulator from the command line. It compiles a small helper with Xcode's clang
into `~/.cache/duoctl` and runs it inside the simulator with `simctl spawn`,
where it sends the same HID events as Device Hub's hinge slider and rotation
control. It needs no Accessibility permission.

**duoctl relies on private, undocumented simulator interfaces.** An Xcode or
simulator runtime update can break it. PowerForge treats it as a convenience:

- It only runs duoctl when its `--version` reports exactly
  `duoctl 0.2.1`, the release PowerForge was checked against.
- It doesn't trust duoctl's exit code. duoctl confirms each change through
  `devicectl`, which doesn't list simulators on device-specific runtimes such
  as `iOS-27-2-iPhone19-4`, so it can report a failure after folding correctly.
  The screenshot check decides.
- When the display still isn't live in the requested orientation after one
  fold and one rotate, PowerForge falls back to the Device Hub prompt instead
  of failing the run.

duoctl is a build tool. Install it on the capture Mac, never in what the app
ships. Pin the release by tag and commit:

```bash
git clone --depth 1 --branch v0.2.1 https://github.com/skarol/duoctl.git "$TOOLS/duoctl"
test "$(git -C "$TOOLS/duoctl" rev-parse HEAD)" = c6ff3e961a9858fb061907448e702d7d1617dc4a
export POWERFORGE_DUO_FOLD_TOOL="$TOOLS/duoctl/skills/duoctl/scripts/duoctl"
```

On Xcode 27.2 with the `iOS-27-2-iPhone19-4` runtime, duoctl folds reliably but
its `rotate` can't confirm the result, so it doesn't change the orientation.
Opening the Duo there leaves the inner display in landscape (2853x2007). Use
`--duo-orientation any` or `landscape` for a fully automatic inner capture, or
`portrait` to be asked to rotate in Device Hub.

## Coordinate a shared Mac

Cooperating sessions use an exclusive lock and one latest receipt in
`AppleSimulatorSessionService.DefaultStateRoot`, under the current user's local
application-data directory. Use `--state-root <shared-path>` when agents and
runner installations need an explicitly configured coordination directory. All
actors sharing simulator resources must use the same root; different roots or
user accounts do not coordinate. The lock file stays in place and is released
by closing its handle. Never delete a lock file to bypass an active session.

The default serializes one simulator session at a time. It does not stop other
pre-existing simulators or coordinate clients that bypass this command. Keep
those clients from taking over the selected device during validation. A paired
phone/Watch matrix needs its own explicit lifecycle plan.
Disable child-owned test cloning and additional simulator destinations for this
single-device wrapper. It releases the selected UUID, not extra devices created
by an arbitrary child script or Xcode's parallel-testing machinery.

`--device-set <existing-path>` selects a separate simctl device set. The default
set and explicit sets are resolved to physical paths and recorded in the receipt.
This option does not automatically redirect Xcode or XCTest to a custom device
set: qualify the child command's device selection separately.

## Inspect an interrupted session

```bash
powerforge apple-simulator status --output json
powerforge apple-simulator acknowledge --session <session-id> --output json
```

The receipt records device/set, owner process identity, validation process
identity when observed, initial/final states, and release outcome. It does not
store child arguments, environment, or captured output. An incomplete receipt
blocks another session from overwriting it. `status` reconciles process identity
and current simulator state without changing the receipt or stopping a device.
If discovery fails, status retains the receipt and process identities, reports
`Unknown` device state and a separate discovery diagnostic, and exits with code 1.

Forced termination can leave descendants even after the recorded command leader
exits. Inspect and resolve the remaining validation process scope independently;
the receipt's leader identity alone does not prove that descendants are gone.

After independently resolving an interrupted session, `acknowledge` requires
its exact session ID, verified owner and recorded validation-process exit, and a
shutdown device. A pre-existing booted device can remain booted when the receipt
proves that the session never requested a boot. Acknowledgment never stops a
device. Process age or death alone does not authorize automatic recovery.

The command does not erase/delete devices, reset CoreSimulator services, install
runner hooks, or schedule maintenance. Runner hooks and reusable workflows can
invoke this owner after their configuration is reviewed; no runner service
changes are required to use it directly.
