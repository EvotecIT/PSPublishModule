# PowerForge GitHub Housekeeping

Reusable composite action that runs the config-driven `powerforge github housekeeping` command from `PowerForge.Cli`.

## What it does

- Loads housekeeping settings from a repo config file, typically `.powerforge/github-housekeeping.json`
- Runs artifact cleanup, cache cleanup, and optional runner cleanup from one C# entrypoint
- Writes a workflow summary with the requested sections plus before/after cleanup stats

## Recommended usage

Use the reusable workflow for the leanest repo wiring:

```yaml
permissions:
  contents: read
  actions: write

jobs:
  housekeeping:
    uses: EvotecIT/PSPublishModule/.github/workflows/reusable-github-housekeeping.yml@main
    with:
      config-path: ./.powerforge/github-housekeeping.json
      powerforge-ref: main
    secrets: inherit
```

For immutable pinning, use the same PSPublishModule commit SHA for both the reusable workflow ref and `powerforge-ref`.

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
- Hosted-runner repos should usually keep `runner.enabled` set to `false` in config.
