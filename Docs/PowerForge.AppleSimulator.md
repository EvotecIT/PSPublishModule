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

After independently resolving an interrupted session, `acknowledge` requires
its exact session ID, verified owner and recorded validation-process exit, and a
shutdown device. A pre-existing booted device can remain booted when the receipt
proves that the session never requested a boot. Acknowledgment never stops a
device. Process age or death alone does not authorize automatic recovery.

The command does not erase/delete devices, reset CoreSimulator services, install
runner hooks, or schedule maintenance. Runner hooks and reusable workflows can
invoke this owner after their configuration is reviewed; no runner service
changes are required to use it directly.
