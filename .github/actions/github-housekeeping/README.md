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
      runner-labels: '["self-hosted","ubuntu"]'
```

Minimal config:

```json
{
  "$schema": "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/github.housekeeping.schema.json",
  "repository": "EvotecIT/YourRepo",
  "tokenEnvName": "GITHUB_TOKEN",
  "dryRun": false,
  "artifacts": {
    "enabled": true,
    "keepLatestPerName": 10,
    "maxAgeDays": 7,
    "maxDelete": 200
  },
  "caches": {
    "enabled": true,
    "keepLatestPerKey": 2,
    "maxAgeDays": 14,
    "maxDelete": 200
  },
  "runner": {
    "enabled": false
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
- A dry-run can still report large cache or artifact totals with `0 eligible` deletes when current keep/latest and age rules retain everything; the Markdown summary explains that breakdown.
- Hosted-runner repos should usually keep `runner.enabled` set to `false` in config.
- The public reusable workflow entrypoint is `powerforge-github-housekeeping.yml`.
- The composite action exposes `report-path` and `summary-path` outputs for callers that want to publish the generated reports elsewhere.
