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
- `package-lock.json` whose root versions match; PowerForge requires the lockfile and uses `npm ci` for reproducible releases;
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

Before changing anything, PowerForge confirms that the pull request is merged, its merge SHA matches the event, the checked-out default branch contains that merge, and the PR head has completed checks with accepted conclusions (`success`, `neutral`, or `skipped`). PowerForge excludes the current release run from settling itself. During recovery it excludes a prior failed release check only after the Actions API proves that it came from the same trusted receiver workflow path and release event; other pending or failed checks remain blocking.

The reusable workflow uses GitHub's durable `queue: max` concurrency mode. Every merged-PR trigger waits for the repository release lock instead of replacing an older pending run, so an intermediate `release:minor` or `release:major` decision is not discarded during a merge burst. Each queued trigger applies its own policy to the then-current default branch.

## Thin receiver workflow

Replace `POWERFORGE_COMMIT_SHA` below with the full commit SHA of a reviewed PSPublishModule release. Keep a release-name comment beside the pin when it helps maintenance. Do not grant a movable tag a cross-repository write token. The receiver contains no release implementation:

The receiver intentionally uses `pull_request_target` for the `closed` event. GitHub otherwise downgrades `GITHUB_TOKEN` to read-only when a merged pull request came from a fork or Dependabot. The trusted default-branch receiver only passes merge metadata into PowerForge: privileged prepare and publish jobs never execute receiver code, and the credential-free build job checks out the already-merged release commit.

```yaml
name: Release

on:
  pull_request_target:
    types: [closed]
  workflow_dispatch:
    inputs:
      pr_number:
        description: Merged pull request number to release or recover
        required: true
        type: string
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
      actions: read
      checks: read
      contents: write
      pull-requests: read
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-homeassistant-release.yml@POWERFORGE_COMMIT_SHA # PowerForge-vX.Y.Z
    with:
      pr_number: ${{ format('{0}', github.event.pull_request.number || inputs.pr_number) }}
      merge_commit_sha: ${{ github.event.pull_request.merge_commit_sha || inputs.merge_commit_sha }}
      default_branch: ${{ github.event.repository.default_branch }}
      increment: ${{ inputs.increment || 'auto' }}
    secrets:
      github-token: ${{ secrets.GITHUB_TOKEN }}
```

Keep the pull request number textual at the workflow boundary. Manual-dispatch inputs are strings, and `format` also converts the numeric pull-request event value to the reusable workflow's single stable input type.

The reusable workflow serializes and queues releases per repository and invokes the matching action at the immutable release commit. The action installs an exact `PowerForge.Build` package version, so the workflow, action, and engine do not drift between retries.

The shared workflow deliberately uses three jobs with different permissions:

1. `prepare` has `contents: write`, validates the merged PR, updates only recognized version files, creates the release commit, and pushes it. It never runs receiver build commands.
2. `build` checks out that exact commit with `contents: read`, runs plugin scripts or creates the integration zip, and uploads only the declared asset as a short-lived Actions artifact. It receives no write token or release secret.
3. `publish` has `contents: write`, downloads the artifact, and publishes/verifies the release without checking out or executing receiver source code.

This is a job-level trust boundary. Scrubbing a token only from an npm child process is not treated as isolation because same-user repository code could inspect or alter its parent job.

## What a successful run changes

When a release is required, PowerForge:

1. synchronizes `manifest.json` and the bounded `[project]` table in `pyproject.toml`, or `package.json` and `package-lock.json`;
2. creates and pushes a commit containing only the version metadata and source PR/merge trailers, using an explicit repository URL, disabled Git hooks, disabled redirects, and ephemeral authentication;
3. starts a separate read-only job at that exact commit and runs the plugin npm validation/build or produces the configured integration zip;
4. rejects any tracked source mutation made by build scripts and transfers only the declared asset;
5. starts a separate privileged publish job that never executes receiver code;
6. preflights any existing `v<version>` release and tag for the expected PowerForge marker and commit before same-named assets may be replaced;
7. creates or safely resumes the release with GitHub-generated change and contributor
   notes, while retaining hidden PowerForge provenance metadata;
8. reads the release back from GitHub to verify the source marker, tag target, and
   required asset.

The version commit is pushed with `GITHUB_TOKEN`, so GitHub does not recursively start ordinary push workflows. Safety comes from the merged PR checks, constrained version edit, job-level credential boundary, conflict preflight, and post-publication verification.

## Recovery

Rerun the failed release job before making manual changes. The workflow is designed to resume after a version push, release creation, or partial asset upload:

- a release with the same source PR/merge marker is verified and returned;
- repository metadata ahead of the latest release is resumed only when its reachable commit trailer proves that it belongs to the requested pull request;
- an existing release is reused or modified only after its source marker and exact tag commit are verified;
- a missing historical asset is rebuilt by the read-only job from a fresh checkout of the recorded tag commit, never from newer default-branch source;
- mismatched tag targets, foreign prepared versions, and build-time tracked mutations fail closed.

If the original run is no longer available, dispatch the receiver workflow with the original PR number and merge SHA. Do not create a second version commit just to retry publication.
