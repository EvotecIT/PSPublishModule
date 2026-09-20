# PowerForge Studio Avalonia migration

Status: active implementation. Windows first; shared core remains portable.

The existing GUI is PowerForgeStudio.Wpf. Its domain and orchestration projects already own repository discovery, Git status/worktrees, file enumeration, GitHub reads and the release queue. The replacement is PowerForgeStudio.Avalonia, a thin presentation host over those owners.

## Delivery checklist

- [x] Inspect current WPF host, shared owners and project state.
- [x] Isolate implementation on feature/studio-avalonia.
- [ ] Implement the reviewed workspace shell and hierarchical project explorer.
- [ ] Connect real file navigation, previews and explicit file operations.
- [x] Add create, copy, move and rename dialogs over shared explorer operations; refresh affected tree branches.
- [ ] Complete file editing/save conflicts and deletion/recovery behavior.
- [ ] Expose reviewed execution plans for JSON, PowerShell, .NET and executable workflows.
- [x] Add explicit working-copy contract inspection and available plan generation through the shared planner.
- [ ] Connect cancellation, live progress, artifacts and release receipts.
- [x] Connect build execution, structured phase output, cancellation and artifact results to the shared executor.
- [x] Connect Git status, diffs, staging, unstaging and local commits through the shared Git owner.
- [ ] Connect issues, PRs and provider status through existing owners.
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
| Local state | Studio.Orchestrator storage through DbaClientX | Restore user workspace |
| Licensing and AI | Existing Licensing and IntelligenceX owners | Scoped optional connections |

The existing WPF app stays runnable until the replacement covers its useful workflows. Its removal and switching the default launcher are delivery steps, not prerequisites for getting the new host running. No existing workspace state is overwritten during development.

## First proof

Launch the native Avalonia app against an explicit workspace root. Discover actual projects, select a repository, expand its primary checkout and worktrees, browse files and display a bounded file preview. Discovery runs away from the UI thread; expanding a folder loads only that directory. Real errors appear in the status/output area.

The reviewed design uses a navy rail and title bar, white workspace, a persistent tree with guide lines, differentiated file icons, worktrees under their project, inline status, document tabs, an inspector and output dock. Eight representative designs define shared layouts, not eight isolated implementations or 108 screens.

## Known audit findings

- The old generic ProjectBuildService resolves a detected script without explicit mode arguments. Do not wire it to a new one-click Build command without a reviewable execution plan.
- The same service has a separate streaming process implementation; inspect cancellation, stdout/stderr lifetime and buffer bounds before reusing that path.
- Workspace profile persistence still lives in WPF view-model services. Move reusable persistence into the orchestrator when the new host needs it.
- Shared Git status now propagates failures and preserves merge conflicts. The GitHub service still returns empty/partial lists for some access failures; distinguish those states before connecting its inbox.

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
