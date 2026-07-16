# Home Assistant and HACS releases

PowerForge can release a Home Assistant custom integration or HACS Lovelace plugin after a pull request is merged. It owns the version decision, synchronized metadata, plugin build or integration zip, version commit, GitHub release, retry behavior, and final tag/asset verification.

The application repository only decides when to call the shared workflow.

## Supported repositories

An integration repository needs:

- `hacs.json`;
- exactly one `custom_components/<domain>/manifest.json` with a three-part `version`;
- an optional `pyproject.toml` with the same `[project] version`;
- for HACS `zip_release`, both `zip_release: true` and `filename` in `hacs.json`.

A Lovelace plugin needs:

- `hacs.json` with `filename`;
- `package.json` with a three-part `version`;
- an optional `package-lock.json` whose root versions match;
- npm scripts named `test`, `check`, and `pack`; `pack` must create `release/<hacs filename>`.

## Release policy

The merged pull request controls the increment:

| Pull request state | Result |
| --- | --- |
| `release:none` | No release |
| `release:patch` | Patch increment |
| `release:minor` | Minor increment |
| `release:major` | Major increment |
| No release label, product/config/dependency change | Patch increment |
| No release label, only docs/tests/workflows/maintainer files | No release |

Use at most one release label. Conflicting labels fail the run rather than guessing.

If the repository has no GitHub release yet, PowerForge publishes the synchronized metadata version as the initial baseline instead of incrementing past an unreleased version.

Before changing anything, PowerForge confirms that the pull request is merged, its merge SHA matches the event, the checked-out default branch contains that merge, and the PR head has completed checks with accepted conclusions (`success`, `neutral`, or `skipped`).

The reusable workflow uses GitHub's durable `queue: max` concurrency mode. Every merged-PR trigger waits for the repository release lock instead of replacing an older pending run, so an intermediate `release:minor` or `release:major` decision is not discarded during a merge burst. Each queued trigger applies its own policy to the then-current default branch.

## Thin receiver workflow

Pin `POWERFORGE_REF` below to a reviewed PSPublishModule commit or release ref. The receiver contains no release implementation:

```yaml
name: Release

on:
  pull_request:
    types: [closed]
  workflow_dispatch:
    inputs:
      pr_number:
        description: Merged pull request number to release or recover
        required: true
        type: number
      merge_commit_sha:
        description: Expected merge commit SHA
        required: false
        type: string
      increment:
        description: auto, none, patch, minor, or major
        required: false
        default: auto
        type: string

jobs:
  release:
    if: github.event_name == 'workflow_dispatch' || github.event.pull_request.merged == true
    permissions:
      checks: read
      contents: write
      pull-requests: read
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-homeassistant-release.yml@POWERFORGE_REF
    with:
      pr_number: ${{ github.event.pull_request.number || inputs.pr_number }}
      merge_commit_sha: ${{ github.event.pull_request.merge_commit_sha || inputs.merge_commit_sha }}
      default_branch: ${{ github.event.repository.default_branch }}
      increment: ${{ inputs.increment || 'auto' }}
    secrets:
      github-token: ${{ secrets.GITHUB_TOKEN }}
```

The reusable workflow serializes and queues releases per repository and invokes the matching tagged action. The action installs an exact `PowerForge.Build` package version, so the workflow, action, and engine do not drift between retries.

## What a successful run changes

When a release is required, PowerForge:

1. synchronizes `manifest.json` and `pyproject.toml`, or `package.json` and `package-lock.json`;
2. creates a local commit containing only the version metadata and source PR/merge trailers;
3. runs the plugin npm validation/build or produces the configured integration zip from that immutable commit without persisted checkout credentials or inherited GitHub tokens;
4. rejects any tracked source mutation made by build scripts;
5. pushes the validated version commit to the default branch with ephemeral Git authentication;
6. creates or resumes `v<version>` at that exact commit and uploads the HACS asset;
7. reads the release and tag back from GitHub and verifies the source marker, tag target, and required asset.

The version commit is pushed with `GITHUB_TOKEN`, so GitHub does not recursively start ordinary push workflows. Safety comes from the merged PR checks plus PowerForge's constrained version edit, artifact build, and post-publication verification.

## Recovery

Rerun the failed release job before making manual changes. The workflow is designed to resume after a version push, release creation, or partial asset upload:

- a release with the same source PR/merge marker is verified and returned;
- repository metadata ahead of the latest release is resumed only when its reachable commit trailer proves that it belongs to the requested pull request;
- existing releases and same-named assets are reused or replaced;
- a missing historical asset is rebuilt in a detached worktree at the recorded tag commit, never from newer default-branch source;
- mismatched tag targets, foreign prepared versions, and build-time tracked mutations fail closed.

If the original run is no longer available, dispatch the receiver workflow with the original PR number and merge SHA. Do not create a second version commit just to retry publication.
