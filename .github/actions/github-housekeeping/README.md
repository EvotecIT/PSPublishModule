# PowerForge GitHub Housekeeping

Reusable composite action that runs the config-driven `powerforge github housekeeping` command from `PowerForge.Cli`.

## What it does

- Loads housekeeping settings from a repo config file, typically `.powerforge/github-housekeeping.json`
- Runs artifact cleanup, cache cleanup, and optional runner cleanup from one C# entrypoint
- Writes a rich workflow summary with requested sections, storage deltas, and item details
- Persists a machine-readable JSON report plus a Markdown report artifact for later review

## Recommended usage

Use the public reusable workflow for the leanest repo wiring:

```yaml
permissions:
  contents: read
  actions: write

jobs:
  housekeeping:
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-github-housekeeping.yml@main
    with:
      config-path: ./.powerforge/github-housekeeping.json
      powerforge-ref: main
    secrets: inherit
```

For immutable pinning, use the same PSPublishModule commit SHA for both the reusable workflow ref and `powerforge-ref`.

The reusable workflow uploads the generated JSON and Markdown reports as an artifact by default.

For self-hosted runner disk cleanup, use the dedicated reusable workflow entrypoint:

```yaml
jobs:
  housekeeping:
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-github-runner-housekeeping.yml@main
    with:
      config-path: ./.powerforge/runner-housekeeping.json
      powerforge-ref: main
      runner-labels: '["self-hosted","linux"]'
```

Minimal config:

```json
{
  "$schema": "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/github.housekeeping.schema.json",
  "Repository": "EvotecIT/YourRepo",
  "TokenEnvName": "GITHUB_TOKEN",
  "Artifacts": {
    "Enabled": true,
    "KeepLatestPerName": 10,
    "MaxAgeDays": 7,
    "MaxDelete": 200
  },
  "Caches": {
    "Enabled": true,
    "KeepLatestPerKey": 2,
    "MaxAgeDays": 14,
    "MaxDelete": 200
  },
  "Runner": {
    "Enabled": false
  }
}
```

## Direct action usage

```yaml
permissions:
  contents: read
  actions: write

jobs:
  housekeeping:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: EvotecIT/PSPublishModule/.github/actions/github-housekeeping@main
        with:
          config-path: ./.powerforge/github-housekeeping.json
          github-token: ${{ secrets.GITHUB_TOKEN }}
```

## Notes

- Cache and artifact deletion need `actions: write`.
- Set `apply: "false"` to preview without deleting anything.
- Prefer letting the workflow decide apply vs dry-run; omit `DryRun` from checked-in repo config unless you have a non-workflow caller that truly needs a local default.
- A dry-run can still report large cache or artifact totals with `0 eligible` deletes when current keep/latest and age rules retain everything; the Markdown summary explains that breakdown.
- Hosted-runner repos should usually keep `Runner.Enabled` set to `false` in config.
- Runner cleanup keeps the active `GITHUB_WORKSPACE` checkout and known internal `_work` folders, then removes old top-level repository workspaces by `Runner.WorkspacesRetentionDays`.
- Repository workspace cleanup is opt-in for direct API and CLI callers; enable `Runner.CleanWorkspaces` in checked-in housekeeping configs or pass `--clean-workspaces` to the direct runner cleanup command.
- Workspace age uses the newer timestamp of `_work/<repo>` and the standard `_work/<repo>/<repo>` checkout directory when that nested directory exists. Workflows that check out into a custom subpath can under-report recency because only the top-level workspace timestamp is available.
- `Runner.WorkspacesRetentionDays: 0` means repository workspaces can be removed immediately when they are not the active checkout and not a known runner-internal folder.
- Config deserialization is case-insensitive, so existing camelCase housekeeping files continue to load even though examples use the public model casing.
- Matrix fan-out with the same runner labels is best-effort: multiple slots can land on the same physical runner while another idle host is skipped. Add unique runner labels when cleanup must target specific physical hosts.
- Direct CLI cleanup flags follow normal command-line precedence: if both a cleanup flag and its skip flag are provided, the later flag wins.
- The public reusable workflow entrypoint is `powerforge-github-housekeeping.yml`.
- The composite action exposes `report-path` and `summary-path` outputs for callers that want to publish the generated reports elsewhere.
