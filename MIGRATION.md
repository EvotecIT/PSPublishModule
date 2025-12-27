# PSPublishModule Migration Guide (PowerShell → C# Core + PowerShell + CLI)

This repo is migrating from a PowerShell-only implementation to:

- **`PowerForge` (C# library)**: the reusable core (build/docs/publish logic).
- **`PSPublishModule` (PowerShell module)**: *thin* cmdlets (parameter binding + `ShouldProcess` + call `PowerForge`).
- **`PowerForge.Cli` (CLI)**: non-PowerShell entrypoint for CI, GitHub Actions, and future VSCode extension scenarios.

## What stays compatible

- Existing module repos that call `Build\\Build-Module.ps1` should continue to work.
- The “PowerShell configuration DSL” remains supported (typed segments now; legacy adapters only where unavoidable).

## What changes (notable)

- “Private” internal PowerShell helper functions are being removed as functionality moves to `PowerForge`.
  - Don’t depend on internal functions like `Test-*` helpers from the module.
  - Use the **public** cmdlets and/or the CLI.
- Output is increasingly **typed** (classes/enums) rather than `Hashtable`/`OrderedDictionary`.

## Recommended workflows

### PowerShell (interactive)

- Keep using existing build scripts, but prefer cmdlets that route through `PowerForge` services.
- When developing PSPublishModule itself, prefer **staging-first** builds to avoid file-locking (“in use”) issues.

### CLI (CI / GitHub Actions / future VSCode)

- Prefer JSON-driven execution so CI and tooling can call a stable contract.
- Typical flow: `plan` → `pipeline`/`build` with `--output json` and CI-safe UI settings.

## Publishing (PowerShellGet + PSResourceGet)

- Publishing is being unified behind `PowerForge` publishers.
- **No standalone “PSResourceGet cmdlets”** are exposed; `PSResourceGet` is used internally where configured/appropriate.

