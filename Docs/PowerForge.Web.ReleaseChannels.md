# PowerForge.Web Engine Release Channels

This document defines a low-risk rollout model for websites consuming `EvotecIT/PSPublishModule` in CI, whether they run the engine from source or from published binaries.

The key split is:

- workflow YAML should stay thin and mostly declare which shared workflow to call plus the selected `powerforge_web_tag`
- committed `.powerforge/*` files should hold durable site/repo policy such as baselines, engine locks for source mode, media profiles, or housekeeping config
- consumer repos should not carry download/extract/checksum implementation logic in workflow YAML

## Goals

- Avoid surprise breakage when engine defaults evolve.
- Roll out engine updates in controlled rings.
- Keep rollback to a one-line variable change.

## Channel Model

- `stable`: known-good commit used by most websites.
- `candidate`: pre-promotion commit used by a canary ring.
- `nightly`: latest integration commit for exploratory validation.

## Consumption Modes

- `source`: workflow resolves `repository` + immutable `ref` and runs `PowerForge.Web.Cli` from a checked-out engine repo.
- `binary`: workflow downloads an exact published release asset and runs the extracted `PowerForgeWeb` executable.
- The reusable workflow defaults to `source` unless a `powerforge_web_tag` is provided.

Source-mode websites should commit `.powerforge/engine-lock.json` and resolve checkout from that file:

```yaml
env:
  POWERFORGE_LOCK_PATH: ./.powerforge/engine-lock.json
```

Lock format:

```json
{
  "$schema": "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.web.enginelock.schema.json",
  "repository": "EvotecIT/PSPublishModule",
  "ref": "ab58992450def6b736a2ea87e6a492400250959f",
  "channel": "stable",
  "updatedUtc": "2026-02-19T00:00:00.0000000+00:00"
}
```

Optional canary override remains available via repository/org variables:

- `POWERFORGE_REPOSITORY`
- `POWERFORGE_REF`

For the normal binary path, pin the exact published tag directly in the workflow:

```yaml
jobs:
  website:
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-website-ci.yml@<workflow-sha>
    with:
      website_root: Website
      pipeline_config: Website/pipeline.json
      powerforge_web_tag: PowerForgeWeb-v1.0.0-preview-20260328151156
```

The shared runner resolves the matching asset for the current runner OS/architecture and uses GitHub's published asset digest when available.

Advanced mode still supports `.powerforge/tool-lock.json` when a repo needs an explicit asset/binary override:

```json
{
  "$schema": "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.web.toollock.schema.json",
  "repository": "EvotecIT/PSPublishModule",
  "target": "PowerForgeWeb",
  "tag": "PowerForgeWeb-v1.0.0-preview-20260328151156",
  "asset": "PowerForgeWeb-1.0.0-net10.0-linux-x64-SingleContained.zip",
  "binaryPath": "PowerForgeWeb",
  "sha256": "<optional sha256>"
}
```

Guidelines:

- prefer `powerforge_web_tag` in workflow inputs for normal consumers
- use exact `tag` + exact `asset` only when you intentionally need a fixed asset override
- the runner now infers the correct asset for the current OS/architecture
- add `sha256` only when you intentionally want to override or supplement GitHub's published asset digest
- do not use "latest"
- default production repos to stable tags
- opt into preview tags only in repos that intentionally test preview builds

## Promotion Flow

1. Update one canary lock to `candidate` SHA:
   - `powerforge-web engine-lock --mode update --path ./.powerforge/engine-lock.json --ref <candidate-sha> --channel candidate`
2. Let full CI/deploy + quality gates run.
3. If healthy, update production locks to new `stable` SHA.
4. If regression occurs, rollback by restoring previous lock ref (one-file revert).

## Canary Ring Recommendation

- Keep one website on `candidate` continuously.
- Keep production websites on `stable`.
- Optionally run a nightly job on one non-production site with `nightly`.

## Guardrails

- Use pipeline baselines with `failOnNewWarnings`/`failOnNewIssues`.
- Use `requireExplicitChecks: true` in `audit`/`doctor` to freeze check behavior across upgrades.
- Require changelog notes for any behavioral default change.

## Scaffold Alignment

`powerforge-web scaffold` and the shared workflow defaults now aim for the smallest normal consumer surface:

- `./.github/workflows/website-ci.yml` that can stay as a thin wrapper
- binary consumers can usually pin only `powerforge_web_tag`
- source-mode consumers keep `.powerforge/engine-lock.json` as the committed engine policy
- workflow-level concurrency cancelation and NuGet package caching enabled by default

This keeps websites aligned with channel pinning without pushing runner implementation details into each consuming repo.

## Reusable Workflow Inputs

The shared runner workflow accepts:

- `engine_mode: source|binary`
- `powerforge_lock_path` for source-mode commit locks
- `powerforge_web_tag` for the normal binary pin
- `powerforge_tool_lock_path` for binary-mode release locks

That keeps consuming repos small: the repo-level workflow can usually choose only the website path, pipeline config, and published tag, while the shared `powerforge-web website-runner` command performs the actual resolution and execution.
