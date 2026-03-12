# PowerForge GitHub Housekeeping

Reusable composite action that wraps the new C# housekeeping commands from `PowerForge.Cli`.

## What it does

- Cleans runner working sets (`powerforge github runner cleanup`)
- Prunes GitHub Actions caches (`powerforge github caches prune`)
- Prunes GitHub Actions artifacts (`powerforge github artifacts prune`)
- Builds the CLI from this repository, so other repos can consume the action with one `uses:` step

## Minimal usage

```yaml
permissions:
  contents: read
  actions: write

jobs:
  housekeeping:
    runs-on: ubuntu-latest
    steps:
      - uses: EvotecIT/PSPublishModule/.github/actions/github-housekeeping@main
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
```

## Typical self-hosted usage

```yaml
permissions:
  contents: read
  actions: write

jobs:
  housekeeping:
    runs-on: [self-hosted, ubuntu]
    steps:
      - uses: EvotecIT/PSPublishModule/.github/actions/github-housekeeping@main
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          min-free-gb: "20"
          cache-max-age-days: "14"
          cache-max-delete: "200"
```

## Notes

- Cache deletion needs `actions: write`.
- Set `apply: "false"` to preview without deleting anything.
- Set `cleanup-runner: "false"` if you only want remote GitHub storage cleanup.
- Set `cleanup-caches: "false"` or `cleanup-artifacts: "false"` to narrow what gets pruned.
