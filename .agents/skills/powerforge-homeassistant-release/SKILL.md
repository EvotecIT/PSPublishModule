---
name: powerforge-homeassistant-release
description: Onboard, operate, verify, or recover GitHub releases for Home Assistant custom integrations and HACS Lovelace plugins using PowerForge. Use for thin receiver workflows, release labels, synchronized manifest/pyproject/package versions, HACS assets, or failed post-merge release runs.
---

# PowerForge Home Assistant Release

Use PowerForge as the release owner. A receiver repository should contain only the event trigger and a call to `.github/workflows/powerforge-homeassistant-release.yml`; do not copy version, npm, zip, git, GitHub API, retry, or verification logic into it.

## Onboard a repository

1. Confirm the repository has `hacs.json` and exactly one supported layout:
   - integration: one `custom_components/<domain>/manifest.json`, with an optional matching `[project] version` in `pyproject.toml`;
   - Lovelace plugin: `package.json`, required `package-lock.json`, and `hacs.json` `filename`.
2. Inspect the existing validation workflow and latest GitHub release. The PR head must have at least one completed successful, neutral, or skipped check run.
3. Add only the `pull_request_target` receiver workflow documented in `Docs/PowerForge.HomeAssistantRelease.md`. Pin the reusable workflow to the full immutable SHA of a reviewed PSPublishModule release; a release tag may appear only as a maintenance comment. Keep the trigger on `closed`: the trusted default-branch workflow must handle fork and Dependabot merges without checking out or executing pull-request code with its write token.
4. Give the job `actions: read`, `checks: read`, `contents: write`, and `pull-requests: read`. Keep the workflow-dispatch PR number input textual and use the canonical `format` expression when passing either event source into the reusable workflow. Pass the merge SHA, default branch, and `github.token` unchanged.
5. Add the `release:none`, `release:patch`, `release:minor`, and `release:major` labels if the repository does not have them.
6. Validate the receiver YAML and open a release-ready PR. Use a release label only when that onboarding PR should intentionally prove a live release.

## Operate releases

- With no release label, product/config/dependency changes increment patch; docs, tests, workflows, and maintainer metadata do not release.
- Exactly one release label overrides that default. Conflicting release labels fail closed.
- PowerForge queues every merge trigger and runs three isolated jobs: privileged metadata prepare/push without receiver commands, read-only exact-commit build without write credentials, then privileged publish without receiver checkout or execution. It verifies the marker, tag target, conflict provenance, and required asset.
- Published releases use GitHub-generated change and contributor notes. PowerForge
  prepends only hidden provenance metadata needed for safe retry and recovery.
- Treat three-part versions as the public contract. Do not introduce four-part consumer versions.

## Recover a failed run

Rerun the failed Actions job first. PowerForge resumes safely:

- a release containing the source PR marker is verified and reused;
- metadata ahead of the latest release is resumed only when the requested PR owns the prepared release commit;
- an existing tag/release is reused and same-named assets are replaced only after marker and commit preflight;
- missing historical assets are rebuilt from a fresh read-only checkout of the exact tag commit;
- a tag pointing at the wrong commit or a missing required asset fails verification.

For a manual recovery, dispatch the receiver workflow with the original merged PR number, its merge SHA, and the same increment decision. The receiver's textual PR-number contract must match the canonical example; a numeric workflow-dispatch input is not compatible with the reusable-call graph. Do not hand-edit a second version bump or manually create a competing release.

## Keep the boundary thin

When another Home Assistant repository needs a new release rule, layout, artifact, or recovery case, implement and test it in `PowerForge`, update the reusable action/workflow and runbook, then repin receivers. A receiver-specific workaround is acceptable only for product-owned validation before the shared release call.
