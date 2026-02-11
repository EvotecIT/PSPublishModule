# Module Builder Checklist

## Preflight

1. Confirm target branch/worktree and clean status.
2. Confirm entrypoint:
   - `Build/Build-Module.ps1`
   - or direct `Invoke-ModuleBuild`.
3. Confirm module build settings:
   - merge behavior
   - required/approved modules
   - install strategy and keep count
   - legacy flat handling and preserved versions.

## Run Order

1. `-JsonOnly` plan/config run (optional but recommended).
2. Real build run.
3. Validate:
   - import step
   - compatibility/file consistency/module validation
   - artefacts/install paths.

## Troubleshooting Pointers

- Duplicate warning lines:
  - check repeated validation/reporting call sites and summary aggregation.
- Scriptblock text reported as unresolved command:
  - ensure unresolved-token filtering excludes non-command AST fragments.
- Missing RSAT commands:
  - classify as environment dependency and respect configured ignore/fail behavior.
- Encoding/mojibake in summary:
  - verify process output encoding setup and console code page.
