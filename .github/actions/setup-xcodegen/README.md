# Setup pinned XcodeGen

This macOS action installs the version in the application's checked-in
`.xcodegen-version` file. It downloads the official release, verifies the reviewed
SHA-256 checksum, checks the executable's version, and adds its `bin` directory to
the job's path. The machine's existing XcodeGen and Homebrew installation remain
untouched.

Use this after checking out the application, and pin the action to a reviewed
PowerForge commit:

```yaml
- uses: EvotecIT/PSPublishModule/.github/actions/setup-xcodegen@<full-commit-sha>
- run: xcodegen generate --spec project.yml --project .
```

The action supports XcodeGen 2.46.0. Add another version and its official archive
digest to the shared release catalogue before changing an application's pin.
`version-file` selects a different checked-in version file when needed.

The complete vendor directory, including setting presets, stays under
`RUNNER_TEMP` for the rest of the job. GitHub's normal runner cleanup removes it
afterward. Failed downloads, checksum checks and version checks remove their
partial installation immediately. Each invocation has its own directory, so
concurrent jobs do not replace each other's tools.
