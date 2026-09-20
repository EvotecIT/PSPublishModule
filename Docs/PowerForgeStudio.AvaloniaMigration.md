# PowerForge Studio Avalonia migration

Status: active implementation. Windows first; shared core remains portable.

The existing GUI is PowerForgeStudio.Wpf. Its domain and orchestration projects already own repository discovery, Git status/worktrees, file enumeration, GitHub reads and the release queue. The replacement is PowerForgeStudio.Avalonia, a thin presentation host over those owners.

## Delivery checklist

- [x] Inspect current WPF host, shared owners and project state.
- [x] Isolate implementation on feature/studio-avalonia.
- [ ] Implement the reviewed workspace shell and hierarchical project explorer.
- [x] Apply native tree row geometry, ancestry guides, context/selection styling, vector navigation icons and Git markers.
- [x] Restore workspace favorites, expanded folders and document tabs through shared catalog persistence.
- [ ] Connect real file navigation, previews and explicit file operations.
- [x] Add create, copy, move and rename dialogs over shared explorer operations; refresh affected tree branches.
- [x] Add text editing, conflict-aware saves, unsaved-document choices and draft-safe navigation.
- [ ] Complete deletion/recovery behavior.
- [ ] Expose reviewed execution plans for JSON, PowerShell, .NET and executable workflows.
- [x] Add explicit working-copy contract inspection and available plan generation through the shared planner.
- [ ] Connect cancellation, live progress, artifacts and release receipts.
- [x] Connect build execution, structured phase output, cancellation and artifact results to the shared executor.
- [x] Connect Git status, diffs, staging, unstaging and local commits through the shared Git owner.
- [ ] Complete issues, PR review actions and provider status through existing owners.
- [x] Connect selected-project issues, PR discussions and checks at the captured PR head.
- [ ] Inventory and expose schedules, storage and optional licensing/IntelligenceX integration.
- [ ] Validate native rendering, keyboard navigation and representative workflows.
- [ ] Review interacting behavior, update build/run/publish entry points and retire the WPF host.
- [ ] Clean task-owned validation artifacts and report delivery limits.

## Ownership and migration boundary

| Capability | Owner | Avalonia responsibility |
|---|---|---|
| Repository and worktree discovery | Studio.Orchestrator Catalog/Hub, PowerForge GitClient | Lazy tree and selection |
| Files | Studio.Orchestrator Explorer | Navigation, previews, explicit operations |
| Build/sign/publish/verify | Studio.Orchestrator Queue and PowerForge | Plan, execute, progress, receipts |
| GitHub issues and PRs | Studio.Orchestrator Hub/Portfolio | Read/review/action presentation |
| Local state | Studio.Orchestrator workspace catalog; release state through DbaClientX | Restore user workspace |
| Licensing and AI | Existing Licensing and IntelligenceX owners | Scoped optional connections |

The existing WPF app stays runnable until the replacement covers its useful workflows. Its removal and switching the default launcher are delivery steps, not prerequisites for getting the new host running. No existing workspace state is overwritten during development.

## First proof

Launch the native Avalonia app against an explicit workspace root. Discover actual projects, select a repository, expand its primary checkout and worktrees, browse files and display a bounded file preview. Discovery runs away from the UI thread; expanding a folder loads only that directory. Real errors appear in the status/output area.

The reviewed design uses a navy rail and title bar, white workspace, a persistent tree with guide lines, differentiated file icons, worktrees under their project, inline status, document tabs, an inspector and output dock. Eight representative designs define shared layouts, not eight isolated implementations or 108 screens.

## Known audit findings

- The old generic ProjectBuildService resolves a detected script without explicit mode arguments. Do not wire it to a new one-click Build command without a reviewable execution plan.
- The same service has a separate streaming process implementation; inspect cancellation, stdout/stderr lifetime and buffer bounds before reusing that path.
- Shared Git status propagates failures and preserves merge conflicts. GitHub project reads now distinguish access failures and bounded partial listings; the older WPF UI still has silent display-level catches and is retained only during migration.

## Validation record

The first host builds without warnings on .NET 10 / Avalonia 12.1.1. Two focused tests pass: a real temporary Git repository is discovered, expanded and previewed in rendered Avalonia controls; bounded text previews support UTF-16 and reject binary/oversized content. The test harness disposes its dispatcher from a worker thread to avoid joining itself.

The native Windows process launched and was closed after the desktop capture helper failed twice with "foreground window did not report a process id". Desktop interaction is still unverified. Headless rendering was inspected at 1600 × 1000; this is not equivalent to native OS/input proof. The current shell is an early foundation with disabled routes, not the completed eight-screen product. Tree guides, responsive layout, document navigation, user state and the remaining workflows are still required.

Run the current host from the repository root:

```powershell
dotnet run --project PowerForgeStudio.Avalonia -- --workspace <repository-or-workspace-folder>
dotnet test PowerForgeStudio.Avalonia.Tests
```

Set POWERFORGE_STUDIO_VISUAL_OUTPUT to a task-owned folder to retain the rendered test screenshot. The initial screenshot is retained locally under Artifacts/StudioValidation/workspace.png. No package was published, no user project was built or modified, and no existing Studio state was migrated.

### File-management milestone

The files page now supports up-navigation, refresh, keyboard Enter, clipboard paths, opening externally, and explicit create/copy/move/rename dialogs. The shared explorer service keeps operations inside the selected working copy, rejects Git metadata and linked paths, and refuses existing destinations. Copy cancellation removes task-created partial output and retains the source. Unix file copies retain executable and restrictive permission modes. The dialog shows transfer progress and accepts cancellation while copying.

The working-copy root is retained independently of tree selection. A winning asynchronous selection repopulates the file list; refreshing a parent preserves loaded child-node identity. Operations refresh loaded source and destination folders, including expanded folders outside the central view.

Evidence:

- Windows: 19 focused FileExplorerOperationsTests cases passed (Unix-only bodies are platform-guarded).
- WSL: the same 19 cases passed using an isolated test project that linked the exact shared service, domain and test sources (Windows-only bodies are platform-guarded). This is file-service proof, not a full Linux repository/app build. WSL has SDK 10.0.112; the repository requests 10.0.303.
- Avalonia: two tests passed with expanded real-Git discovery/navigation, copy dialog, destination collision, cross-directory move/tree refresh, UTF-16 preview and bounded binary/large-file behavior.
- Rendered evidence inspected at 1600 × 1000 and 1050 × 720, plus the operation dialog. Compact layout hides the inspector and wraps file actions. Clipboard/external-opening and native modal interaction still need desktop proof.
- Local review: review_file_management inspected this milestone read-only against 7c4b36958. Three P2 findings (superseded navigation, destination-tree invalidation, Unix file modes) were fixed and tested. One targeted confirmation found the fixes addressed with no additional actionable finding. The navigation stress test does not force a particular read-completion schedule; the source fix always repopulates the winning request.

Next implementation focus: reviewed build plans and execution through canonical PowerForge services. The existing ProcessRunRequest already supports streaming stdout/stderr callbacks, and ReleaseBuildExecutionService already adapts project/module/unified release contracts. Reuse those owners rather than the old independent streaming implementation in ProjectBuildService.

### Build inspection milestone

Build & Run now opens a working-copy-specific inspection page. Its explicit action uses RepositoryPlanPreviewService through a narrow interface; it does not construct a synthetic portfolio or execute on selection. Project and unified release contracts use their existing planning adapters. Module JSON is validated, and PowerShell module contracts export configuration. These results are distinguished in the UI: module configuration validation is not presented as a resolved build plan.

Catalog discovery recognizes Build/project.build.json without a PowerShell wrapper. It checks the repository's Build directory before child directories and prefers JSON within each directory. Invalid project configuration becomes a failed result. Changing working copies cancels the old request and discards late results. Synchronous adapter work may finish before cancellation is observed; the UI does not claim immediate termination.

Evidence: 31 focused catalog/planner tests and four Avalonia tests passed. A controlled delayed-planner test verifies cancellation and stale-result suppression while switching working copies. The rendered page was inspected at 1600 × 1000 and 1050 × 720; the compact page scrolls its content below the output dock. Native input/scrolling proof remains open. Test dispatchers are serialized because Avalonia's application state is process-wide.

The build-planning review found two P2 issues: nested JSON taking precedence over a root script, and validation being described as full planning. Both were corrected and confirmed in one targeted follow-up. No further review loop was started. Temporary fixtures from the interrupted dispatcher test and the failed JSON test were removed. Small rendered evidence remains under Artifacts/StudioValidation/build-plan*.png.

This inspection milestone did not build or publish a user repository. The execution milestone below extends this surface.

### Build execution milestone

After a successful inspection, Build current configuration invokes ReleaseBuildExecutionService for that working copy. The output dock shows structured engine phases and work items. The result lists adapter diagnostics, artifact files and build directories. Changing projects preserves the active build and its original root; Cancel build targets that run. Closing the workspace requests cancellation. Output is bounded, and displayed diagnostics use the existing command-secret redactor. This filter recognizes secret argument patterns; it cannot infer arbitrary secrets printed by trusted project code.

The shared executor disables PowerForge publication. Project builds may perform configured local signing; module builds request Build mode with signing, installation and module publishing disabled. Legacy module scripts must expose the required controls before invocation, otherwise they fail with an actionable error. This is a build-only contract check, not a sandbox for arbitrary scripts or hooks. The PowerShell project fallback now forwards cancellation to the shared cancellable runner instead of cancelling only its waiting task.

Evidence:

- Six Avalonia workflow tests passed. A JSON-only fixture produced a real NuGet package containing lib/net10.0/StudioFixture.dll. A subsequent real compiler failure produced visible diagnostics and left the build action available. A controlled execution test proved that navigation retains the running build's root and that explicit cancellation reaches that run.
- Five PowerForge host tests passed, including actual PowerShell invocations of supported and unsupported module-script fixtures, plus cancellable-runner dispatch.
- Twenty-two focused execution/queue tests and one progress/redaction test passed.
- PowerForge built without warnings for net10.0, net8.0 and net472. The application builds against net10.0.
- Success/failure rendering was inspected at 1600 × 1000 and 1050 × 720. Small local evidence is retained in Artifacts/StudioValidation/build-result*.png and build-failure.png. Native Windows input and cancellation rendering remain unverified.
- Independent read-only execution review found no actionable P0–P2 issue. Its wording observation was addressed by scoping the shared completion summary to PowerForge publication rather than claiming no possible script side effects.

An initially broad release-build test filter included Apple source-trust tests in unchanged code. Several expected exception types/messages differed from the current wrapper behavior, so that run was stopped and the changed execution contracts were tested separately. This milestone does not claim a green full Studio suite or verified Apple execution. Its 78 disposable Git fixture residues were removed after containment/attribute checks. The completed core test binary output (about 565 MiB) was also removed; the active app's build output remains available for continued development. No user project or public feed was modified.

Remaining execution work includes complete module planning, inspectable plan contents, arbitrary executable/PowerShell task profiles, live raw process output where supported, durable activity/history and artifact provenance, release signing/publishing/verification controls, and native interaction proof. The earlier checklist remains the full replacement scope.

### Git Changes milestone

The Changes page separates staged, working-copy and untracked entries, with a text diff or bounded untracked preview. Stage and Unstage affect the selected literal path; Commit commits the current index. The shared status reader uses NUL-delimited porcelain records to preserve Unicode and unusual filenames, rename sources and merge conflicts. Failed Git status is reported as an error. Conflicts disable the Avalonia commit action, and Git rejects unresolved commits at execution.

Unstaging before the first commit preserves working files and requires evidence of an unborn local branch. Failed, timed-out or unresolved HEAD probes do not authorize index removal. Staged rename diffs include both paths; unstaging a rename resets both index entries. Working-copy diffs compare the current indexed path. The legacy WPF host catches and displays the newly explicit shared-service failures while it remains available.

Operations capture their starting working copy. Commit messages are retained separately per working copy for this application session. A commit completing after navigation clears only the matching original draft. Changing projects does not redirect a running operation. The page scrolls at short window heights so the file list, diff and commit controls remain usable.

Evidence:

- Five focused shared Git contract tests passed, including a real merge conflict and failed HEAD/status probes.
- Seven Avalonia tests passed before the final rename correction; the focused Git workflows were rerun after correction, including an additional controlled navigation test that delays a real commit across a project switch.
- The WPF host builds without warnings. Its native error presentation has not been visually verified.
- Actual Avalonia Skia rendering was inspected at 1600 × 1000 and 1050 × 720, including the scrolled compact commit form. Evidence: Artifacts/StudioValidation/git-changes*.png. This does not establish native OS/input behavior.
- Independent read-only review /root/review_git_changes covered the staged Git milestone against 6ae099822, fingerprint a2c1981079c0d0aa33c857cc769c3096a251e277. It found one P2: destination-only staged rename diffs hid the move. Both hosts now pass the source path and the real repository test checks the resulting rename diff. Boundary: candidate-reviewed; focused primary validation covers remediation, with no repeated full review.

Remaining Git work includes refresh after external edits, branch/history and remote workflows, partial staging, and native interaction proof. Diffs displayed in the UI are capped at 256 KiB after capture; the shared Git runner's capture is not yet bounded by that display limit. The shared branch/worktree list helpers also still return empty lists on secondary probe failure. These limits are separate from the now-explicit primary status failures.

No user repository was staged, committed, or published by the GUI validation. Disposable repositories were removed by their tests. Temporary WPF validation binaries were removed; active Avalonia outputs and small screenshots are retained.

### Tree and shell milestone

The explorer now uses a native TreeViewItem theme with 30-pixel rows, 19-pixel indentation and ancestor guides. The active project uses a blue folder and a subtle context background; other projects remain amber. Selected files have a separate highlight. The theme retains Avalonia's named expansion, header and item-presenter parts and focus target. Rail actions use one outline vector family, while page buttons identify the selected page with a blue underline. Unimplemented rail routes remain disabled.

Loaded file nodes show real Git markers, and each inspected working copy shows its own change count or clean/conflict state. Windows paths from Git worktree output are normalized before matching file nodes. Selecting a linked checkout retains its owning project name. Tree file selection also synchronizes the central list so file actions target that file. Filtering retains existing project nodes and their expanded children.

Evidence:

- Nine Avalonia tests passed after the tree/theme/status changes. The three workspace tests passed again after synchronizing tree and central-file selection.
- A real two-repository fixture with a linked checkout proves separate Git markers and owning-project context. A native-control headless keyboard check uses Right to expand a folder and Down to select and preview its script.
- Inspected updated wide and compact rendered views, including Artifacts/StudioValidation/workspace-tree.png with primary checkout, worktree, modified file and active-project context.
- Independent read-only review /root/review_tree_shell found no actionable P0-P3 issue. Boundary: current; base ed759f285; staged patch fingerprint ab65f8fcba8692e272d1d3afa89ae3b84154fb4e. No targeted confirmation was needed.
- Native Windows validation was retried after window discovery began responding. The application process and window appeared, but capture/activation still failed with foreground window did not report a process id on both attempts. Both owned validation processes were closed. Native input/rendering remains unverified; headless keyboard input is not equivalent evidence.

Complete keyboard/mouse coverage and the remaining pages are still required. Current Git markers reflect the last explicit status read, not a filesystem watcher.


### Workspace persistence and document tabs

The existing workspace catalog and profile records now live in Domain/Workspace and Orchestrator/Workspace. Both hosts use that owner. Avalonia restores favorite projects, expanded folders and document tabs per workspace, and loads the saved active workspace when no command-line root is supplied. File contents and credentials are not stored in the catalog. Same-named files show their working-copy name in the tab; the full path remains available as a tooltip.

Favorite updates and session writes preserve each other's fields. Catalog writes use a bounded cross-process file lock, a flushed adjacent temporary file and atomic replacement. Unknown top-level JSON fields and legacy profiles/templates survive saves. Malformed catalogs are preserved and reported rather than overwritten. WPF also displays persistence failures and retains its original workspace when an attempted profile switch cannot be saved.

Open tabs follow file and folder moves performed in Studio. A missing file displays its own failed-preview state. Closing an active tab selects a remaining tab, or returns to the workspace. Closing during startup or after failed discovery preserves the last persisted session. If a settings save fails at window close, the error remains visible; closing again exits without saving.

Evidence:

- All 12 Avalonia tests passed before review. After remediation, all five session tests passed, including the two new regressions below.
- Six shared persistence tests passed: concurrent field updates, legacy profile coexistence, unknown top-level fields, corrupt-catalog preservation and document path validation. Six existing WPF catalog tests also passed.
- The WPF host built with zero warnings; its new view-model failure test passed for refresh, profile switch and profile save against a malformed catalog.
- Actual Avalonia Skia screenshots were inspected at 1600 × 1000 and 1050 × 720: Artifacts/StudioValidation/workspace-session.png and workspace-session-compact.png. Keyboard activation of a document tab selects its owning repository. Native desktop validation remains blocked by the previously recorded automation failure.
- Independent read-only review /root/review_workspace_session covered the original staged milestone against aa0ab79c2, fingerprint 1a304ca0105907b8c29a424ec8cbde5ad353ece9. It identified two P2 races: saving empty state before restoration, and late preview completion after closing the sole tab. Both were fixed and covered by a seeded failed-discovery test and a controlled pending filesystem read. The single targeted confirmation accepted both fixes without further findings in that boundary. Status: candidate-reviewed; the original fingerprint identifies the pre-remediation candidate only.

Temporary WPF validation binaries (about 325 MiB) and the isolated legacy-test temp directory were removed. The active Avalonia outputs and small rendered evidence remain local. There has been no application distribution switch, package publication or live user-workspace migration. Deletion/recovery, releases, GitHub, schedules, storage/worktree actions and provider connections remain on the delivery checklist.


### Text editing and save conflicts

Files can be edited in the central pane, with Save, Reload, Browse files and Ctrl+S. Each document keeps its own draft and shows an unsaved marker in its tab. Closing a dirty tab, closing the window or leaving the workspace offers Save, Discard and Cancel. Navigation and refresh retain drafts. Draft contents are kept in memory and are not written to the workspace catalog; crash recovery is not implemented.

The shared PowerForge RepositoryTextFileEditor uses the existing repository text transaction owner. It accepts UTF-8 and BOM-marked UTF-16/UTF-32 up to 256 KiB, preserves encoding/BOM and existing line endings, and checks the original byte hash before saving. Binary, invalid-Unicode, oversized and deleted files are rejected. The Studio adapter applies the same working-copy, Git-metadata and link restrictions as other explorer operations. Successful saves use a flushed temporary file and replacement. If another process replaces the pathname in the final comparison/replacement interval, the draft may already be installed; the displaced contents are retained in a named backup and the error identifies that recovery path. This is not a filesystem-wide lock against external editors.

Move/rename retains document identity and draft state while rebinding its original snapshot to the destination. Input and saves are disabled during a file operation. A late binding update is preserved rather than discarded. Repository discovery now runs through the shared WorkspaceRepositorySource; refresh never reconstructs a live document collection from an older saved list. Build inspection/execution is disabled for working copies with drafts, and an edit invalidates pending inspection results even if it is saved before the old inspection completes.

Evidence:

- All 16 Avalonia tests passed before independent review. After remediation, ten focused editor/session/planning tests passed. The two controlled refresh/relocation tests passed again after adding an actual headless keyboard check proving that a moving file cannot be typed into while its editor is read-only.
- Twenty-one shared text transaction/editor tests passed after remediation, including the existing transaction tests, six Unicode/BOM variants, byte-only external changes, bounded content, and normal/oversized displaced-file recovery. The Studio path-boundary test passed for outside-root paths, Git metadata and a deleted target.
- PowerForge builds with zero warnings for net472, net8.0 and net10.0. No PowerForge package has been published; the Avalonia worktree consumes the local project reference.
- Actual Skia renders were inspected at 1600 × 1000 and 1050 × 720. The first compact render hid the editor below its conflict message; the corrected editor spans the central pane and bounds the message region. Current evidence: Artifacts/StudioValidation/workspace-editor.png and workspace-editor-compact.png. Native Windows validation remains the previously recorded gap.
- Independent read-only review /root/review_file_editing inspected the editor milestone against c3baca719, original fingerprint ea76d13cd14f166c837ca8a334fea0f0761c5304. It found two P1 draft-loss races during discovery and relocation, a P2 stale-inspection result, and a P3 missing recovery-path diagnostic. These were interactions introduced by the editor feature. All were fixed, with a sibling sweep across tab/window close, root changes, folder refresh and save/move overlap. The single targeted confirmation accepted fingerprint 1dd6656c6ea3a90b0df2c65cd9ceab39cd94f050 with no additional actionable findings in the remediation scope. Boundary: candidate-reviewed.

The editor is a bounded plain-text editor. Syntax tooling and crash-recoverable drafts are not implemented. No user repository file was edited by validation; tests used disposable fixtures. Large task-owned test/CLI/module binaries were removed after validation; the active Avalonia build and small screenshots remain local.


### GitHub project workspace

The GitHub rail and project tab open pull requests or issues for the selected working copy. Refresh resolves its github.com origin and loads the selected open/closed/all filter. Lists, discussion, inline review comments and timeline entries use the shared GitHubProjectService. Switching working copy, filter or item cancels previous reads and rejects late results. The GitHub page uses the output dock's space for discussion; builds and local changes keep their existing dock.

PR details capture the head SHA returned by GitHub, then load check runs and latest commit status per context for that SHA. Failures are visible; access failures for checks leave the discussion readable. This snapshot is not a merge-policy decision. PR file diffs, review posting, branch protection/ruleset evaluation and merge actions remain unfinished. Discussion Markdown is currently shown as selectable plain text. Open on GitHub uses a URL constructed from the validated repository and numeric item number, not remote body links.

The shared service validates repository identifiers, reports HTTP access/rate-limit errors without echoing response bodies, limits each response to 4 MiB, and bounds requests including body reads. Lists expose possible additional pages after five 100-item pages, counting raw issue-endpoint records before filtering PRs. Discussion exposes its own coverage flag. Incomplete counts raise an error instead of returning an exact-looking number. The old "Ready to merge" display was replaced with "No merge conflicts"; list responses state that reviews have not been loaded.

Authentication retains the existing environment/gh/token-file resolution order, with thread-safe lazy resolution. The CLI path now uses the canonical PowerForge ProcessRunner, a five-second timeout per executable candidate, bounded captured output and an explicit github.com hostname. Initial discovery runs away from the UI thread. Credential discovery is cached for the process lifetime; changing credentials requires restarting Studio. No credentials are persisted in the workspace catalog.

Evidence:

- Twenty-six focused shared GitHub tests passed: access failures, later-page failure, filtered pagination coverage, invalid repository names, malformed/oversized responses, captured-head checks, latest commit status per context, disposal during an active request, and bounded CLI credential lookup.
- All 21 Avalonia tests passed before the final GitHub dock adjustment; the two GitHub UI tests passed again after it. Controlled tests cover ignored cancellation on project/filter switches, stale discussion results, explicit access errors and discussion retention when check access fails.
- Avalonia and retained WPF builds passed with zero warnings. Actual headless Skia renders were inspected at 1600 × 1000 and 1050 × 700: Artifacts/StudioValidation/workspace-github.png and workspace-github-compact.png. Native Windows input and launching the external browser remain unverified.
- A disposable console harness exercised the actual shared service and normal credential resolution against EvotecIT/PSPublishModule: two open PRs, one issue, PR 958 with one comment and four timeline events, and nine checks/statuses at its returned head. These are point-in-time observations, not a release/readiness claim. No remote writes occurred and only counts were logged.
- Independent read-only review /root/review_github_workspace inspected the staged milestone against 5f7131552, fingerprint 1c8e6d50a69048a67d044adcf945b7414f855ce6, including both renders. It found no actionable introduced defect. Boundary: candidate-reviewed. No targeted confirmation was needed. Native interaction and the remaining review/action scope remain explicit gaps.
- The disposable live harness and completed shared-test/WPF binaries were removed after containment, link, tracked-file and running-process checks: about 225 MiB. Active Avalonia outputs and the small rendered evidence remain local.

API contract references: [GitHub check runs](https://docs.github.com/en/rest/checks/runs#list-check-runs-for-a-git-reference) and [commit statuses](https://docs.github.com/en/rest/commits/statuses#list-commit-statuses-for-a-reference).
