# Library Builder Checklist

## Preflight

1. Confirm target branch/worktree and clean status.
2. Inspect `Build/project.build.json`:
   - project discovery filters
   - expected version map
   - staging and output paths
   - publish toggles (`PublishNuget`, `PublishGitHub`).
3. Verify secrets/token resolution paths before release.

## Plan and Execute

1. Run plan mode first.
2. Validate proposed versions and package paths.
3. Run full build/publish.
4. Verify published package/release inventory.

## GitHub Release Decisions

- `PerProject`:
  - each project gets its own tag/release.
- `Single`:
  - choose `GitHubPrimaryProject` for version source.
  - if omitted and versions diverge, use date/timestamp template tokens.
- Tag conflicts:
  - `Reuse`: attach/update existing release.
  - `Fail`: stop and require manual intervention.
  - `AppendUtcTimestamp`: generate unique suffix automatically.

## Common Failure Patterns

- Existing tag conflict (`422 already_exists`):
  - apply configured tag conflict policy.
- Plan mode fails on package file existence:
  - ensure plan/what-if path does not enforce produced-file checks.
- Mixed-version confusion in single release:
  - set primary project or tag template to date/timestamp.
