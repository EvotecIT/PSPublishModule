# macOS Runner Storage

Self-hosted Apple builds can fill the Mac's internal disk even when the GitHub Actions
work folder is already on an external drive. CoreSimulator, SwiftPM, NuGet, and
Playwright keep their own state under the runner user's home directory.

`powerforge github runner storage` moves that runner-owned state to an external APFS
volume while preserving the paths expected by Xcode and package tools.

## What it configures

The command:

- points the runner work folder at the external work directory
- stores NuGet, Playwright, and SwiftPM caches under the external state directory
- creates an APFS sparse bundle for CoreSimulator
- mounts that image at `~/Library/Developer/CoreSimulator`
- installs a runner wrapper that mounts the image before `runsvc.sh` starts
- updates the runner LaunchAgent to call the wrapper
- retains moved local directories and managed configuration files in a stable
  `runner-storage-original` backup directory

The sparse bundle is important. Xcode's simulator services do not reliably accept a
plain symlink for the CoreSimulator device set. Mounting an APFS image at the standard
path keeps Apple's expected filesystem behavior while the image data lives on the
external disk.

## Plan first

Stop the runner service before applying the plan. PowerForge refuses to apply while
`Runner.Worker`, `Runner.Listener`, or `runsvc.sh` is active.

```bash
cd ~/actions-runner
./svc.sh stop

powerforge github runner storage \
  --runner-root "$HOME/actions-runner" \
  --state-root "/Volumes/BuildStorage/GitHubActions/runner-state/macos-01" \
  --work-root "/Volumes/BuildStorage/GitHubActions/work/macos-01" \
  --core-simulator-size-gb 120 \
  --dry-run \
  --output json
```

Review the changed step ids and resolved paths. The state and work roots must be
runner-specific, non-symlinked directories on the same mounted APFS volume under
`/Volumes`.

## Apply

Run the same command with `--apply`, then start the service:

```bash
powerforge github runner storage \
  --runner-root "$HOME/actions-runner" \
  --state-root "/Volumes/BuildStorage/GitHubActions/runner-state/macos-01" \
  --work-root "/Volumes/BuildStorage/GitHubActions/work/macos-01" \
  --core-simulator-size-gb 120 \
  --apply

cd ~/actions-runner
./svc.sh start
```

The generated wrapper waits for both external directories and verifies the APFS
volume UUID before starting the runner. If the disk is unavailable or a different
volume appears under the same name, the runner stays offline instead of silently
writing build or simulator data back to the internal disk.

Run the dry-run again after the service starts. A settled configuration reports the
storage steps as skipped. A wrapper or LaunchAgent change can still appear when an
older hand-written wrapper is in use.

## Recovery

Applied migrations retain the prior local data. The result reports this stable path:

```text
/Volumes/BuildStorage/GitHubActions/runner-state/macos-01/backups/runner-storage-original
```

Keep that backup until a clean workflow has:

- built the Apple targets
- booted the required iPhone, iPad, and Watch simulators
- left `~/Library/Developer/CoreSimulator` mounted from the sparse bundle
- kept healthy free space on the internal disk

To roll back, stop the runner, detach the CoreSimulator image, restore the reported
backup directories to their original paths, restore the prior LaunchAgent
`ProgramArguments`, and start the service. PowerForge will not replace an unrelated
CoreSimulator mount or an existing cache symlink that points somewhere else.

Retries reuse the same original backup and a persistent operation lock prevents two
apply processes from migrating the runner at once. Once the backup has been validated
and deliberately removed or archived, a later migration can establish a new baseline.
