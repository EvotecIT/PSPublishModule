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

Each website workflow should resolve engine checkout from:

- `POWERFORGE_REPOSITORY` (usually `EvotecIT/PSPublishModule`)
- `POWERFORGE_REF` (branch/tag/SHA)

Recommended pattern:

```yaml
env:
  POWERFORGE_REPOSITORY: EvotecIT/PSPublishModule
  POWERFORGE_REF: ${{ vars.POWERFORGE_REF != '' && vars.POWERFORGE_REF || 'ab58992450def6b736a2ea87e6a492400250959f' }}
```

## Promotion Flow

1. Set canary website variable `POWERFORGE_REF` to `candidate` SHA.
2. Let full CI/deploy + quality gates run.
3. If healthy, update `stable` SHA (repo/org variable) for broad rollout.
4. If regression occurs, rollback by resetting `POWERFORGE_REF` to previous stable SHA.

## Canary Ring Recommendation

- Keep one website on `candidate` continuously.
- Keep production websites on `stable`.
- Optionally run a nightly job on one non-production site with `nightly`.

## Guardrails

- Use pipeline baselines with `failOnNewWarnings`/`failOnNewIssues`.
- Use `requireExplicitChecks: true` in `audit`/`doctor` to freeze check behavior across upgrades.
- Require changelog notes for any behavioral default change.

## Scaffold Alignment

`powerforge-web scaffold` now generates `./.github/workflows/website-ci.yml` with this pattern:

- `POWERFORGE_REPOSITORY: EvotecIT/PSPublishModule`
- `POWERFORGE_REF` resolved from GitHub variable fallback to pinned stable SHA
- CI executes `pipeline --mode ci` using the checked-out engine
- workflow-level concurrency cancelation and NuGet package caching enabled by default

This keeps new websites aligned with channel pinning by default.
