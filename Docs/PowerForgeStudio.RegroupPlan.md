# PowerForgeStudio Regroup Plan

Last updated: 2026-03-12

## Purpose

Pause feature-first PowerForge Studio work long enough to lock the architecture boundary:

- `PSPublishModule` cmdlets stay thin.
- reusable build/publish/sign/verify logic lives in `PowerForge` / `PowerForge.PowerShell`.
- `PowerForgeStudio` acts as an orchestration shell over those services, not a second build engine.
- future integrations such as `IntelligenceX` plug into provider seams instead of forcing Studio-specific business rules into the queue runner.

## What Is Already Good

The repo already contains two strong patterns worth preserving:

1. Cmdlets are mostly orchestration shells over shared services.
   - `Invoke-ProjectBuild` prepares inputs and calls `ProjectBuildWorkflowService`.
   - `Invoke-ModuleBuild` prepares inputs and calls `ModuleBuildWorkflowService`.
2. Studio already has host reuse inside its own app boundary.
   - `PowerForgeStudio.Orchestrator` is consumed by both `PowerForgeStudio.Wpf` and `PowerForgeStudio.Cli`.
   - workspace snapshots, queue commands, and portfolio projections already live outside WPF.

Those are the right instincts. The regroup work is about extending the same discipline across the repo boundary.

## Current Drift We Need To Stop

### 1. Studio does not consume the shared build engines directly

`PowerForgeStudio.Orchestrator` currently references:

- `PowerForgeStudio.Domain`
- `DbaClientX.SQLite`

It does **not** reference:

- `PowerForge`
- `PowerForge.PowerShell`

That means Studio cannot call the same reusable workflow services that the cmdlets call today.

### 2. Studio re-creates execution behavior through inline PowerShell scripts

Current examples:

- `RepositoryPlanPreviewService` builds ad-hoc scripts for `Invoke-ProjectBuild` and `Invoke-ModuleBuild`.
- `ReleaseBuildExecutionService` rewrites project config JSON and patches module DSL behavior by wrapping `New-ConfigurationBuild`.
- `ReleasePublishExecutionService` mixes direct `dotnet nuget push`, `Send-GitHubRelease`, and Studio-owned publish receipt logic.

This works as a bootstrap, but it creates a second orchestration layer with different defaults, safety rails, and failure shapes than the shared engine.

### 3. Engine workflows are reusable in design, but not yet host-facing in shape

The core workflow services exist:

- `ProjectBuildWorkflowService`
- `ModuleBuildWorkflowService`
- `DotNetRepositoryReleaseWorkflowService`
- publish/signing helpers across `PowerForge` and `PowerForge.PowerShell`

But the workflow entry points are still effectively cmdlet-internal:

- many services are `internal`
- some assume cmdlet-style setup or logging
- Studio-friendly request/response contracts are not yet first-class

### 4. Queue orchestration and execution orchestration are mixed together

Studio should own:

- queue ordering
- checkpoint persistence
- approval flow
- repo/worktree attention views

Studio should not own:

- package push semantics
- GitHub release composition
- module build plan shaping
- repo build-policy mutation

Right now some of that lower-level execution behavior is duplicated inside Studio adapters.

## Architecture Decision

### Rule 0: Studio does not call cmdlets

`PSPublishModule` cmdlets are for PowerShell hosts.

Studio should not execute cmdlets as its normal integration path.
Studio should call reusable C# services and typed contracts exposed by:

- `PowerForge`
- `PowerForge.PowerShell`

If a cmdlet exists first, that is a signal to extract or expose the reusable C# logic underneath it, not a reason for Studio to invoke the cmdlet.

### Rule 1: one execution engine, many hosts

All build/release business logic that could be reused by:

- cmdlets
- Studio
- CLI
- tests
- future services

must live in `PowerForge` or `PowerForge.PowerShell`.

`PSPublishModule` remains a PowerShell host adapter.
`PowerForgeStudio` remains a desktop/CLI orchestration host.

### Rule 2: Studio coordinates workflows, it does not redefine them

Studio is responsible for:

- discovering repositories and worktrees
- deciding what should run next
- storing queue state and receipts
- showing status, blockers, and approvals
- selecting the right shared adapter for module/library/mixed repos

Studio is **not** responsible for inventing alternate implementations of:

- plan generation
- build execution
- publish execution
- signing behavior
- verification rules

### Rule 3: PowerShell-specific compatibility stays in `PowerForge.PowerShell`

Anything tied to:

- DSL scriptblocks
- `ScriptBlock`
- module manifest conventions
- PowerShell repository registration/install behavior
- PowerShell host/runtime concerns

belongs in `PowerForge.PowerShell`, even when Studio later invokes it through a C# API.

### Rule 4: repo-specific rules become adapters, not shell logic

Per-repository differences should resolve into small adapters that answer:

- what contract does this repo use?
- where is the config or entrypoint?
- which shared workflow request should be built?
- which capabilities are supported?

The adapter may map repo layout into a shared request, but the actual execution should happen in shared PowerForge services.

### Rule 5: external tools live behind reusable infrastructure services

Sometimes the right implementation still requires an external executable or host-specific process, for example:

- `git`
- `dotnet`
- `pwsh` / `powershell`
- signing tools

That is acceptable only when the process boundary is wrapped by a reusable C# service with typed inputs and outputs.

Do not scatter raw command strings or per-host process logic across:

- Studio UI
- Studio queue adapters
- cmdlets
- repo-specific orchestration code

Instead, keep the process boundary in one reusable service and let every host call that same service.

## Command Boundary Policy

Preferred order of implementation:

1. pure C# logic inside `PowerForge` / `PowerForge.PowerShell`
2. reusable C# service that wraps an external executable with typed request/result contracts
3. host adapter (`PSPublishModule`, Studio, CLI, WPF) that calls the reusable C# service

Avoid this order:

1. Studio builds a shell string
2. Studio calls a cmdlet
3. Studio parses console text and treats it as business state

## Git Example

`git` is the right example for the rule above.

Target shape:

- a reusable Git service in shared code
- typed methods for operations such as status, fetch, pull, push, switch, branch creation, and upstream setup
- typed results for exit code, stdout/stderr, parsed branch state, and remediation hints

Consumers:

- Studio can ask for git status, safe actions, and command execution through one shared service.
- `PowerForge` workflows can reuse the same git service for release preflight or repo automation.
- cmdlets can call the same service when PowerShell needs that functionality.

Non-goal:

- Studio owning its own git command catalog, its own process runner strategy, and its own parsing rules forever.

## Target Layering

### `PowerForge`

Owns host-agnostic build/release logic:

- project/library planning and execution
- NuGet/GitHub publish primitives
- external tool abstractions such as git/dotnet/process-backed infrastructure when pure C# is not possible
- signing workflows
- verification workflows
- typed request/result models

### `PowerForge.PowerShell`

Owns PowerShell-specific reusable logic:

- module DSL preparation
- module build preparation/workflow
- PowerShell repository/module publish behavior
- PowerShell-host-adjacent compatibility services

### `PSPublishModule`

Owns only PowerShell UX:

- parameter binding
- `ShouldProcess`
- host rendering
- mapping shared results to stable cmdlet-facing output contracts

### `PowerForgeStudio.Domain`

Owns Studio-specific state contracts:

- queue/session/checkpoint models
- portfolio/workspace projections
- inbox/dashboard/readiness models

### `PowerForgeStudio.Orchestrator`

Owns Studio coordination only:

- repository discovery
- queue planning
- persistence through `DbaClientX.SQLite`
- calling shared PowerForge execution services
- composing shared results into Studio receipts/checkpoints

### `PowerForgeStudio.Wpf` and `PowerForgeStudio.Cli`

Own only presentation and host interaction.

## Concrete Refactor Direction

### Phase A: expose host-friendly shared execution services

Before moving more Studio features, add public host-oriented services and contracts in shared libraries for:

1. project plan/build execution
2. module plan/build execution
3. publish execution
4. verification execution
5. external tool boundaries that Studio currently owns directly (`git` first)

These should accept typed requests and return typed results without requiring:

- PowerShell script generation
- temp JSON mutation in Studio
- cmdlet-only wrappers
- raw process command strings embedded in Studio services

### Phase B: move Studio from script wrappers to shared services

Replace these Studio seams first:

1. `RepositoryPlanPreviewService`
2. `ReleaseBuildExecutionService`
3. `ReleasePublishExecutionService`
4. `ReleaseVerificationExecutionService`
5. Studio-owned git execution and parsing seams

The goal is not “remove PowerShell from existence”; the goal is “Studio stops using PowerShell wrapper scripts as its primary business logic path.”

### Phase C: keep queue receipts, but base them on shared result contracts

Studio should still persist its own checkpoint/receipt models, but those receipts should be projections of shared results, not bespoke interpretations of shell output.

### Phase D: add provider seams for external attention sources

For future `IntelligenceX` integration, define provider interfaces around attention and governance signals, for example:

- repository health/inbox items
- release recommendations
- GitHub governance/policy checks
- future automation hints

That keeps GitHub support strong today without making Studio depend on one product forever.

## Near-Term Rules For Ongoing Work

Effective immediately:

1. Do not add new business logic to `PSPublishModule\Cmdlets\` if Studio or tests could reuse it.
2. Do not have Studio call cmdlets as its primary implementation path.
3. Do not add new Studio-only build/publish/git logic when the same behavior belongs in `PowerForge` / `PowerForge.PowerShell`.
4. Do not treat PowerShell script generation inside Studio as the long-term API surface.
5. Prefer extracting shared request/result contracts before adding new queue stages or new UI affordances.
6. Keep WPF and CLI on the same orchestrator service surface so the shell never becomes the only usable host.

## Recommended Next PR Order

1. Introduce a small host-facing execution contract review doc or issue list for project/module/publish/verify services.
2. Extract a reusable shared git/process boundary so Studio no longer owns git execution semantics.
3. Make the shared execution services public where appropriate and normalize request/result models.
4. Refactor Studio project plan/build execution to call shared services directly.
5. Refactor Studio module plan/build execution to call shared services directly through `PowerForge.PowerShell`.
6. Refactor Studio publish/verify execution to reuse shared publish and verification services.
7. Only then continue broader Studio UX work and any IntelligenceX-driven inbox expansion.

## Definition Of Success

We are back on track when:

- cmdlets remain thin and mostly unchanged when new execution logic is added
- Studio can run project/module flows without inventing alternate shell-script wrappers or calling cmdlets
- external tools such as git are wrapped once in reusable C# services instead of being hardcoded in Studio
- the same shared services are testable without PowerShell host plumbing
- WPF and CLI remain thin over one Studio orchestrator
- future IntelligenceX integration plugs into provider seams instead of bypassing PowerForge
