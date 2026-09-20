# PowerForge Studio Avalonia migration

Status: active implementation. Windows first; shared core remains portable.

The existing GUI is PowerForgeStudio.Wpf. Its domain and orchestration projects already own repository discovery, Git status/worktrees, file enumeration, GitHub reads and the release queue. The replacement is PowerForgeStudio.Avalonia, a thin presentation host over those owners.

## Delivery checklist

- [x] Inspect current WPF host, shared owners and project state.
- [x] Isolate implementation on feature/studio-avalonia.
- [ ] Implement the reviewed workspace shell and hierarchical project explorer.
- [ ] Connect real file navigation, previews and explicit file operations.
- [ ] Expose reviewed execution plans for JSON, PowerShell, .NET and executable workflows.
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
