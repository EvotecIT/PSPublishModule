# PowerForge.Web Engine Release Channels

This document defines a low-risk rollout model for websites consuming `EvotecIT/PSPublishModule` in CI.

## Goals

- Avoid surprise breakage when engine defaults evolve.
- Roll out engine updates in controlled rings.
- Keep rollback to a one-line variable change.

## Channel Model

- `stable`: known-good commit used by most websites.
- `candidate`: pre-promotion commit used by a canary ring.
- `nightly`: latest integration commit for exploratory validation.

Each website should commit `.powerforge/engine-lock.json` and resolve checkout from that file:

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

`powerforge-web scaffold` now generates:

- `.powerforge/engine-lock.json` (pinned ref)
- `./.github/workflows/website-ci.yml` that resolves checkout from the lock file
- CI executes `pipeline --mode ci` using the checked-out engine
- workflow-level concurrency cancelation and NuGet package caching enabled by default

This keeps new websites aligned with channel pinning by default.
