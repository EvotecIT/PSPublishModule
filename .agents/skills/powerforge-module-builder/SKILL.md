---
name: powerforge-module-builder
description: Build, validate, install, and publish PowerShell modules with PSPublishModule/PowerForge. Use when working with Invoke-ModuleBuild, Build/Build-Module.ps1, New-ConfigurationBuild, merge/approved modules, versioned install behavior, legacy flat-module migration, and module packaging/signing troubleshooting.
---

# PowerForge Module Builder

Use this skill for module build pipeline work, not website work.

## Golden Path (Do This In Order)

1. Confirm repo/branch hygiene before changes.
   - Prefer a feature branch or git worktree.
   - Keep unrelated paths clean.
2. Preflight configuration.
   - Locate `Build/Build-Module.ps1` and module root (`Module/`).
   - Check `New-ConfigurationBuild` and install settings first.
3. Produce JSON plan/config before invasive changes.
   - Use `Invoke-ModuleBuild -JsonOnly -JsonPath ...` when possible.
4. Run the real module build.
   - Prefer repo script `Build/Build-Module.ps1`.
5. Validate outcomes from summary + logs.
   - Check merge summary, missing commands, required modules, and import step.
6. Apply install compatibility policy intentionally.
   - Default is warn-only for legacy flat installs.
   - Use explicit behavior when migrating old flat installs.
7. Keep fail-fast ordering.
   - Validate/import before signing when changing pipeline order.
8. Verify both engines.
   - Validate PowerShell 5.1 path (`WindowsPowerShell`) and PowerShell 7+ path.
9. Validate tests/build.
   - Run focused tests first, then broader tests.
10. Document config and migration behavior.
   - Update docs/schema/help when adding parameters.

## High-Value Commands

```powershell
# Generate pipeline JSON only (no execution)
Invoke-ModuleBuild -ModuleName 'MyModule' -Path . -JsonOnly -JsonPath .\powerforge.json -Settings { ... }

# Run standard module build entrypoint
.\Build\Build-Module.ps1

# Focused tests for pipeline changes
dotnet test .\PowerForge.Tests\PowerForge.Tests.csproj -c Release
```

## Decision Rules

- For mixed legacy flat + versioned installs, prefer explicit config:
  - `VersionedInstallLegacyFlatHandling`: `Warn`, `Delete`, or `Convert`.
  - `VersionedInstallPreserveVersions`: versions that must not be removed.
- Do not silently change install policy defaults in behavior-changing PRs.
- If missing commands are environment-specific (for example RSAT), classify clearly and avoid noisy false positives.

## Reference Files (Read As Needed)

- `references/checklist.md` for fast preflight + troubleshooting sequence.
- `Module/Docs/Invoke-ModuleBuild.md` for command surface.
- `Module/Docs/New-ConfigurationBuild.md` for build/install parameters.
- `Docs/PSPublishModule.ProjectBuild.md` when module build and repo release flow intersect.
