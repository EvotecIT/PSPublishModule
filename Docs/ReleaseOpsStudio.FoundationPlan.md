# ReleaseOpsStudio Foundation Plan

Last updated: 2026-03-09

## Working Title

`ReleaseOpsStudio` is the working title for a Windows desktop release cockpit that coordinates
PowerForge/PSPublishModule module builds, project builds, signing, publish approvals, and repo health
across the maintainer's GitHub workspace.

The name is intentionally provisional. The architecture should assume the product name may change
without forcing namespace or storage rewrites outside the new app projects.

## Why This Exists

The current release flow already has good build engines:

- PowerShell modules expose `Build/Build-Module.ps1`
- Libraries expose `Build/Build-Project.ps1`
- PowerForge/PSPublishModule already supports plan/build/pack/sign/publish concerns

The pain is orchestration:

- too many repos and worktrees open at once
- no single queue for release readiness
- signing will soon require USB-token checkpoints
- PRs, branches, dirty states, and publish targets are spread across too many tools

This app is meant to become the control plane over those existing contracts, not a replacement for them.

## Product Goals

1. Discover repositories and worktrees across one or more scan roots.
2. Classify each repo by release contract:
   - module
   - library
   - mixed
   - website
   - legacy/unknown
3. Provide a persistent local catalog and run history using `DbaClientX.SQLite`.
4. Run plan/build/sign/publish as explicit queue stages with resumable checkpoints.
5. Surface "what needs attention now" without opening 10-15 VS Code windows.
6. Keep repo-specific logic behind adapters so the shell stays generic.

## Non-Goals For Early Milestones

- replacing existing `Build-Module.ps1` / `Build-Project.ps1` entrypoints
- full git UI parity with ForgeFlow
- custom package publishing engine
- hiding every detail behind a single magic button before the queue model is proven

## Technical Direction

### UI

- `WPF` on `.NET 10`
- desktop-first shell with intentional styling, not default stock controls
- a design system from day one: colors, typography, spacing, cards, queue surfaces, status states

### Persistence

- use `DbaClientX.SQLite` for local app state
- no direct ad-hoc raw SQLite connection code in the app shell
- keep schema bootstrap and write paths inside the orchestrator layer

### Layers

1. `ReleaseOpsStudio.Domain`
   - stable contracts, enums, repository catalog models
2. `ReleaseOpsStudio.Orchestrator`
   - discovery, storage bootstrap, later queueing and adapter execution
3. `ReleaseOpsStudio.Wpf`
   - shell, navigation, view models, release cockpit screens

## First-End-To-End Slice

The first additive PR series should produce a usable foundation:

1. Isolated worktree/branch for the release-manager work.
2. In-repo architecture plan and naming decisions.
3. Domain models for repository classification.
4. Discovery service that scans a GitHub root and classifies repos/worktrees.
5. SQLite-backed local catalog snapshot persisted through `DbaClientX.SQLite`.
6. WPF shell that:
   - loads the catalog
   - shows counts and repo status
   - previews the execution spine
   - proves the visual direction is modern enough to build on

## Execution Spine

The app should treat releases as a queue with explicit stages:

1. `Prepare`
   - scan repos
   - fetch git/branch/dirty state
   - discover build contracts
   - run plan-only operations
2. `Build`
   - execute build/pack outputs
   - collect artefacts and logs
3. `Sign`
   - wait for certificate/USB approval
   - sign pending artefacts
4. `Publish`
   - push to GitHub/NuGet/PowerShell Gallery/custom feeds
5. `Verify`
   - confirm publish receipts, versions, assets, and release notes

## Core Screens

### Portfolio

- all repos/worktrees in one searchable view
- readiness indicators
- branch/worktree context
- build contract detection

### Queue

- selected repos in release order
- current step, blocked step, retry, resume
- signing checkpoint visibility

### Repository Detail

- local git state
- entrypoints
- last plan/build/publish results
- future adapter configuration

### Signing Station

- pending artefacts
- batch approval flow
- "USB token required" checkpointing

## Repo Detection Rules

Early repo classification rules should stay deterministic and easy to reason about:

- `Build/Build-Module.ps1` => module
- `Build/Build-Project.ps1` => library
- both => mixed
- `Website` folder or top-level `build.ps1` without a PowerForge build script => website signal
- worktree heuristics derive from path patterns such as `_worktrees`, `_wt`, `.wt-`

Deeper repo-specific rules should be deferred to adapters, not hard-coded into the shell.

## State To Persist Early

- schema version
- latest repository catalog snapshot
- scan timestamp
- draft queue sessions and queue items
- future: run history, queue checkpoints, signing requests, publish receipts

## PR Strategy

Keep this additive and reviewable:

1. `PR 1`: foundation doc + app skeleton + repo discovery + SQLite bootstrap + shell
2. `PR 2`: git metadata and readiness indicators
3. `PR 3`: plan-only execution adapters
4. `PR 4`: queue runner and resumable checkpoints
5. `PR 5`: signing station and persisted signing receipts
6. `PR 6`: publish station, publish receipts, and verification adapters
7. `PR 7`: retry/resume affordances, richer custom-feed verification, and final release close-out polish

## Current Foundation Status

- `PR 1` complete: shell, discovery, and storage bootstrap
- `PR 2` complete: git telemetry and readiness shaping
- `PR 3` complete: plan-only preview for project repos and module-preview surfacing
- `PR 4` foundation complete: persisted draft queue session, queue ordering, real build execution with signing/publish/install safety rails, signing-station manifests, persisted signing receipts, command-driven stage transitions, and USB approval checkpoints
- `PR 5` foundation complete: publish station, persisted publish receipts, external publish safety gate, and first executable publish adapters for project/module flows
- `PR 6` foundation complete: verification station, persisted verification receipts, and evidence-based queue completion for GitHub, NuGet.org, and PSGallery scenarios
- `PR 7` foundation in progress: retry/resume affordances, an opt-in smoke harness for a live repository build contract, guarded real-signing smoke mode, richer custom-feed/private-repository verification, mixed-repo wrapper hardening, explicit PSPublishModule engine visibility in the shell, repository-detail drill-in for queue/checkpoint evidence and adapter summaries, adapter output/error tail visibility plus checkpoint payload inspection, a lightweight GitHub inbox for PR/workflow/release attention signals, persisted release-drift snapshots for "what changed since the last known release" visibility, portfolio focus filters for attention/ready/queue-active triage, saved portfolio triage state so the shell restores the last used focus/search view, one-click presets for Ready Today, Attention, Queue Active, Reset, and USB Waiting, clickable queue-aware dashboard cards for Ready Today, USB Waiting, Publish Ready, Verify Ready, and Failed, a ranked release inbox that merges queue blockers, USB pauses, publish/verify-ready work, GitHub pressure, and ready-today candidates into one action list, repository-family grouping so primary repos, worktrees, and review clones can be triaged as one operational unit, family-level queue actions for scoped prepare/retry flows, a clickable family-lane board that maps each family member into Ready/USB Waiting/Publish Ready/Verify Ready/Failed/Completed lanes, and persisted publish receipt source paths so verification can keep following real artifacts after recovery or restart

## Decisions Made In This Slice

- use `WPF` instead of `WinUI 3`
- target `.NET 10`
- store app state via `DbaClientX.SQLite`
- isolate work in a dedicated git worktree and branch
- optimize for maintainability and orchestration first, not full automation on day one
