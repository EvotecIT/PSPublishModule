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
    secrets: inherit
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
