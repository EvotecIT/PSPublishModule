---
name: powerforge-library-builder
description: Build, version, pack, sign, and publish multi-project .NET libraries using Invoke-ProjectBuild and project.build.json. Use for ExpectedVersionMap/X-pattern resolution, NuGet publishing, GitHub release mode/tag strategy, and release troubleshooting.
---

# PowerForge Library Builder

Use this skill for repository/package pipelines (`Invoke-ProjectBuild`), including NuGet + GitHub release workflows.

## Golden Path (Do This In Order)

1. Confirm repository scope and branch hygiene.
   - Prefer feature branch/worktree.
2. Validate `project.build.json`.
   - Discovery filters (`IncludeProjects`, `ExcludeProjects`, expected map include mode).
   - Version expectations and version sources.
3. Generate plan before publishing.
   - Use `PlanOnly` or cmdlet `-Plan` first.
4. Execute build/pack/sign path.
   - Ensure staging/output paths are deterministic.
5. Publish NuGet with explicit fail-fast and duplicate policy.
6. Publish GitHub release with explicit tag policy.
   - Choose `Single` or `PerProject` intentionally.
   - Set `GitHubPrimaryProject` for single-mode version source.
7. Handle tag conflicts intentionally.
   - Prefer configurable conflict policy instead of ad-hoc retries.
8. Verify final release state.
   - Confirm release/tag and attached asset set match plan.
9. Update docs/schema/help for any new config fields.

## High-Value Commands

```powershell
# Plan-only
Invoke-ProjectBuild -ConfigFilePath .\Build\project.build.json -Plan

# Full run
Invoke-ProjectBuild -ConfigFilePath .\Build\project.build.json

# Validate core tests after engine changes
dotnet test .\PowerForge.Tests\PowerForge.Tests.csproj -c Release
```

## Decision Rules

- If projects have mixed versions in `Single` release mode:
  - set `GitHubPrimaryProject` or use date/timestamp tags by template.
- Prefer template-based tags over hardcoded tags:
  - tokens include `{Project}`, `{Version}`, `{PrimaryProject}`, `{PrimaryVersion}`, `{Repo}`, `{Date}`, `{UtcDate}`, `{DateTime}`, `{UtcDateTime}`, `{Timestamp}`, `{UtcTimestamp}`.
- Use explicit conflict policy for existing tags:
  - `Reuse`, `Fail`, or `AppendUtcTimestamp`.
- In plan/what-if mode, avoid hard failures that require produced artefacts on disk.

## Reference Files (Read As Needed)

- `references/checklist.md` for quick release-mode and tag-policy decisions.
- `Docs/PSPublishModule.ProjectBuild.md` for JSON behavior.
- `schemas/project.build.schema.json` for allowed fields and enums.
- `Module/Docs/Invoke-ProjectBuild.md` for cmdlet behavior.
