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
- [ ] Connect Git changes, issues, PRs and provider status through existing owners.
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
- Shared Git status currently maps exceptions to NotARepository. Preserve error meaning before presenting failures as a clean or empty repository.

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

Still required: resolved module build plans, inspectable plan contents, build execution with streaming progress and cancellation, artifact receipts, signing/publishing controls, and portable process/JSON execution. No user repository was built or published by this milestone.
