# PowerForge Studio Avalonia migration

Status: Avalonia product active; capability expansion continues. Windows first; shared core remains portable.

The canonical page, subpage, shared-state and WPF retirement decisions are in [PowerForgeStudio.ProductMap.md](PowerForgeStudio.ProductMap.md). That smaller map supersedes the exploratory 53-page/108-state browser pack.

The desktop GUI is PowerForgeStudio.Avalonia, a thin presentation host over the shared Domain and Orchestrator owners for repository discovery, Git status/worktrees, file operations, GitHub reads and release workflows. The former PowerForgeStudio.Wpf host has been retired.

## Delivery checklist

- [x] Inspect current WPF host, shared owners and project state.
- [x] Isolate implementation on feature/studio-avalonia.
- [x] Implement the reviewed workspace shell and hierarchical project explorer.
- [x] Apply native tree row geometry, ancestry guides, context/selection styling, vector navigation icons and Git markers.
- [x] Restore workspace favorites, expanded folders and document tabs through shared catalog persistence.
- [x] Add a bounded per-project overview with working-copy identity, product signals, entrypoints and prerequisites.
- [x] Add bounded per-working-copy commit history with changed-file and diff inspection.
- [x] Connect real file navigation, previews and explicit file operations.
- [x] Add create, copy, move and rename dialogs over shared explorer operations; refresh affected tree branches.
- [x] Add text editing, conflict-aware saves, unsaved-document choices and draft-safe navigation.
- [x] Complete deletion/recovery behavior.
- [x] Expose reviewed execution plans for JSON, PowerShell, .NET and executable workflows.
- [x] Add explicit working-copy contract inspection and available plan generation through the shared planner.
- [x] Connect cancellation, stage progress, artifacts and release receipts.
- [x] Add per-artifact signing progress and durable process-level recovery evidence.
- [x] Extend per-target progress through publication and verification.
- [x] Connect build execution, structured phase output, cancellation and artifact results to the shared executor.
- [x] Prepare release artifacts from a captured successful build using the existing queue checkpoint owner.
- [x] Connect explicit signing, cancellation and session receipts with build/close interlocks.
- [x] Journal signing, reopen saved receipt history and recover failed local saves.
- [x] Connect inspected targets, explicit publication approval, durable publication and verification.
- [x] Connect Git status, diffs, staging, unstaging and local commits through the shared Git owner.
- [x] Add validated local branch creation and switching to the Changes workspace.
- [x] Complete issues, PR review actions and provider status through existing owners.
- [x] Connect selected-project issues, PR discussions and checks at the captured PR head.
- [x] Add PR changed-file lists and bounded patch previews with revision checks.
- [x] Add review-only workspace storage inventory with measured worktrees and local ancestry evidence.
- [x] Show cancellable storage-scan progress and expose a bounded read-only storage CLI for large workspaces.
- [x] Add current-remote, Studio-use and retained-artifact checks with guarded no-force worktree removal.
- [x] Add matching merged-PR-head fallback, reviewed broken-reference pruning and bounded external process detection where available.
- [x] Inventory Windows schedules and local GitHub workflow definitions with explicit provider evidence boundaries.
- [x] Add secret-free GitHub, registry, Licensing, IntelligenceX and local-toolchain connection evidence.
- [x] Add a cross-project Activity inbox over existing portfolio, release, GitHub issue/PR/CI and automation owners.
- [x] Add machine-local Settings with durable behavior and bounded Activity refresh preferences.
- [x] Connect bounded, read-only GitHub Actions workflow state and scheduled-run evidence to local definitions.
- [ ] Connect a supported Codex automation inventory adapter and provider-owned configuration actions when their owners expose safe APIs.
- [x] Validate native rendering, keyboard navigation and representative workflows within the recorded Windows capture limits.
- [x] Review interacting behavior and retire the WPF host, its tests, dependencies and compatibility switches.
- [x] Clean task-owned validation artifacts and report delivery limits.

## Ownership and migration boundary

| Capability | Owner | Avalonia responsibility |
|---|---|---|
| Repository and worktree discovery | Studio.Orchestrator Catalog/Hub, PowerForge GitClient | Lazy tree and selection |
| Files | Studio.Orchestrator Explorer | Navigation, previews, explicit operations |
| Build/sign/publish/verify | Studio.Orchestrator Queue and PowerForge | Plan, execute, progress, receipts |
| GitHub issues and PRs | Studio.Orchestrator Hub/Portfolio | Read/review/action presentation |
| Local state | Studio.Orchestrator workspace catalog; release state through DbaClientX | Restore user workspace |
| Licensing and AI | Existing Licensing and IntelligenceX owners | Scoped optional connections |

The WPF host is removed. Its machine-local JSON profiles and custom templates remain readable in Avalonia Settings; legacy saved portfolio-view rows remain untouched in each workspace SQLite database. Current build, run and publish entry points target Avalonia only.

## First proof

Launch the native Avalonia app against an explicit workspace root. Discover actual projects, select a repository, expand its primary checkout and worktrees, browse files and display a bounded file preview. Discovery runs away from the UI thread; expanding a folder loads only that directory. Real errors appear in the status/output area.

The reviewed design uses a navy rail and title bar, white workspace, a persistent tree with guide lines, differentiated file icons, worktrees under their project, inline status, document tabs, an inspector and output dock. Eight representative designs define shared layouts, not eight isolated implementations or 108 screens.

## Known audit findings

- The old generic ProjectBuildService resolves a detected script without explicit mode arguments. Do not wire it to a new one-click Build command without a reviewable execution plan.
- The same service has a separate streaming process implementation; inspect cancellation, stdout/stderr lifetime and buffer bounds before reusing that path.
- Shared Git status propagates failures and preserves merge conflicts. GitHub project reads distinguish access failures and bounded partial listings.

## Validation record

The first host builds without warnings on .NET 10 / Avalonia 12.1.1. Two focused tests pass: a real temporary Git repository is discovered, expanded and previewed in rendered Avalonia controls; bounded text previews support UTF-16 and reject binary/oversized content. The test harness disposes its dispatcher from a worker thread to avoid joining itself.

The native Windows process launched and was closed after the desktop capture helper failed twice with "foreground window did not report a process id". Desktop interaction is still unverified. Headless rendering was inspected at 1600 × 1000; this is not equivalent to native OS/input proof. The current shell is an early foundation with disabled routes, not the completed eight-screen product. Tree guides, responsive layout, document navigation, user state and the remaining workflows are still required.

Run the current host from the repository root:

```powershell
dotnet run --project PowerForgeStudio.Avalonia -- --workspace <repository-or-workspace-folder>
dotnet test PowerForgeStudio.Avalonia.Tests
```

Set POWERFORGE_STUDIO_VISUAL_OUTPUT to a task-owned folder to retain the rendered test screenshot. The initial screenshot is retained locally under Artifacts/StudioValidation/workspace.png. No package was published, no user project was built or modified, and no existing Studio state was migrated.

### Project overview milestone

Selecting a project or one of its working copies now opens the `/projects/p/overview` surface before any build action. The page keeps the project tree and shared project tabs visible, identifies the selected primary checkout or worktree, shows current local Git state, summarizes the root README, and lists bounded product signals, exact build entrypoints and prerequisites. Direct actions lead to Build & Run, Files, Changes, GitHub and Releases. Selecting a folder or file continues to open Files.

The reusable overview owner reads only the root and one directory level, streams at most 80 child-directory candidates and 400 file entries with cancellation checks, skips linked paths and the explorer's metadata/cache exclusions, bounds README inspection to 256 KiB and reports partial filesystem failures. Configured build paths are remapped from the primary repository into the selected worktree. JSON, PowerShell and solution entrypoints are classified without parsing or executing project code. Opening README is an explicit user action and is restricted to an existing non-linked path inside the selected working copy. User-requested Overview Refresh reacquires Git and worktree state before it rebuilds the metadata snapshot, while initial selection reuses the fresh Git query already completed by the workspace coordinator. Git refresh failures stay inside the route and produce a safe retry message.

Evidence:

- Two focused shared tests pass. One maps a configured JSON project contract from a primary checkout into a selected worktree, ignores 400 Git-metadata files without losing the immediate-child project signal, detects README, solution, product and prerequisite signals, and proves a marker PowerShell script was not executed. The other keeps an oversized README and missing build mapping explicit.
- All 43 Avalonia tests pass on the final candidate. The route case uses a disposable Git repository, proves project selection opens Overview while Files remains separate, checks the detected JSON/solution entries, refreshes an externally changed Git state, keeps a corrupt-Git refresh failure inside the route without exposing its path, and confirms the marker script was not executed. A controlled race proves a newer Git snapshot for the same working copy does not cancel and strand the metadata view.
- Actual Skia rendering was inspected at 1600 x 1000 and 1050 x 720, including the compact bottom state: `Artifacts/StudioValidation/project-overview.png`, `project-overview-compact.png` and `project-overview-compact-bottom.png`.

The overview does not claim installed-tool readiness; prerequisites link the user conceptually to Connections for observed runtime evidence. Persisted run history, release history, package/download summaries, editable project descriptors and archive inspection remain separate planned routes. Native pointer, keyboard, external README opening and filesystem watcher behavior remain unverified.

### Project history milestone

The `/projects/p/history?w=w` route keeps the project tree and project tabs visible while showing the 50 most recent commits for the selected working copy. Selecting a commit shows its author, message, changed paths and patch without checking out a revision or modifying repository content. The History route joins Overview, Files and Changes as a working-copy-specific surface; switching projects cancels the old read and clears its evidence before another repository can populate the page.

History reuses ProjectGitService through a narrow interface. The service verifies an unborn `HEAD` separately from a real log failure, clamps requested log counts to 100, accepts only full SHA-1 or SHA-256 commit IDs observed by Studio, compares merge commits with their first parent, caps changed paths at 400 and caps captured file-list and diff output at 256K decoded characters. Its branch-only status probe skips untracked-file enumeration and caps capture at 16K characters. The bounded GitClient overload preserves the earlier public CLR signature for compiled consumers. Truncation is explicit in the typed result and rendered diff. Opening or selecting a project does not start a history scan; the route and its Refresh action own that work.

Evidence:

- Six focused shared tests pass. Real repositories prove bounded diff capture, changed-file projection, first-parent merge evidence, invalid-object rejection and unchanged working-copy state. An unborn repository returns an empty history while an actual `git log` failure remains an error. Forced capture-boundary results prove that incomplete paths are never published and that the branch probe remains bounded without enumerating untracked files. Reflection verifies that the original four-parameter GitClient CLR signature remains present.
- Three focused Avalonia tests pass. Controlled delayed providers prove that switching working copies cancels old history and detail requests, clears their progress states and retains only the new evidence. A real repository proves navigation, commit selection, changed files and patch rendering.
- Actual Skia rendering was inspected at 1600 x 1000 and 1050 x 720: `Artifacts/StudioValidation/project-history.png` and `project-history-compact.png`. The compact project-tab row remains complete, and the history-specific output height keeps both changed-file and diff evidence visible.

History is currently read-only. Commit comparison, opening the exact commit on GitHub, pagination beyond the bounded recent set and path-specific history remain planned actions. Native pointer, keyboard and non-Windows rendering remain unverified.

### File-management milestone

The files page now supports up-navigation, refresh, keyboard Enter, clipboard paths, opening externally, and explicit create/copy/move/rename dialogs. Frequent actions use a compact icon-and-label toolbar; recovery, refresh, path copying and external opening live in its accessible More menu. The shared explorer service keeps operations inside the selected working copy, rejects Git metadata and linked paths, and refuses existing destinations. Copy cancellation removes task-created partial output and retains the source. Unix file copies retain executable and restrictive permission modes. The dialog shows transfer progress and accepts cancellation while copying.

The working-copy root is retained independently of tree selection. A winning asynchronous selection repopulates the file list; refreshing a parent preserves loaded child-node identity. Operations refresh loaded source and destination folders, including expanded folders outside the central view.

Evidence:

- Windows: 19 focused FileExplorerOperationsTests cases passed (Unix-only bodies are platform-guarded).
- WSL: the same 19 cases passed using an isolated test project that linked the exact shared service, domain and test sources (Windows-only bodies are platform-guarded). This is file-service proof, not a full Linux repository/app build. WSL has SDK 10.0.112; the repository requests 10.0.303.
- Avalonia: two tests passed with expanded real-Git discovery/navigation, copy dialog, destination collision, cross-directory move/tree refresh, UTF-16 preview and bounded binary/large-file behavior.
- Rendered evidence inspected at 1600 × 1000 and 1050 × 720, plus the operation dialog. The revised toolbar and open More menu were inspected in `Artifacts/StudioValidation/file-toolbar/`; at the compact viewport, the frequent actions fit in one row and the inspector is hidden. Clipboard/external-opening and native modal interaction still need desktop proof.
- Local review: review_file_management inspected this milestone read-only against 7c4b36958. Three P2 findings (superseded navigation, destination-tree invalidation, Unix file modes) were fixed and tested. One targeted confirmation found the fixes addressed with no additional actionable finding. The navigation stress test does not force a particular read-completion schedule; the source fix always repopulates the winning request.

The later toolbar refinement received a separate read-only review (`review_file_toolbar`). It found that icon-and-label buttons had lost their screen-reader names. The controls now set explicit automation names, and the real-repository headless test checks the names returned by Avalonia's automation peers, opens the More menu and captures its rendered state. The reviewer closed the finding in targeted confirmation. Native keyboard and screen-reader behavior remain unverified while the Windows session is locked.

Next implementation focus: reviewed build plans and execution through canonical PowerForge services. The existing ProcessRunRequest already supports streaming stdout/stderr callbacks, and ReleaseBuildExecutionService already adapts project/module/unified release contracts. Reuse those owners rather than the old independent streaming implementation in ProjectBuildService.

### File deletion and recovery milestone

Files and folders are deleted through a reviewed recovery workflow. Studio first captures the selected path, item count, logical size and a metadata snapshot, then requires explicit confirmation. The operation is refused when an open document under that path is dirty or busy, when the item changed after review, or when the selected tree contains Git metadata, a symbolic link or a junction. The working-copy root and `.git` metadata cannot be selected.

Confirmed items move atomically into the application-managed recovery store under the user's local application-data folder. Each entry has a durable manifest with prepared, available, restoring and deleting states. Listing repairs interrupted states when the payload remains and removes orphaned manifests when the payload is gone. Restore refuses to replace an occupied destination or recreate a missing parent. Permanent deletion requires a separate checkbox and re-inspects directory payloads before removing the exact recovery entry. A recovery store inside the working copy or below a linked path is rejected.

Evidence:

- Twenty-nine focused shared tests passed for the recovery owner and existing explorer operations. They cover review drift, root and Git protection, restore conflicts, interrupted manifests, exact-entry deletion, nested Git metadata introduced after recovery, missing-payload cleanup, recovery-store containment and tampered manifest isolation.
- All 31 Avalonia tests passed. The end-to-end recovery test uses a real disposable Git repository and exercises a dirty-document block, confirmed move, durable listing, restore, a second move and explicitly confirmed permanent deletion.
- Wide and compact Skia renders were inspected for the Files toolbar, review dialog and recovery dialog: `Artifacts/StudioValidation/workspace-recovery-toolbar.png`, `file-delete-review.png`, `file-recovery.png` and `file-recovery-compact.png`. The compact footer keeps confirmation and actions on separate responsive rows.

The review fingerprint intentionally covers path/type/size/timestamp/attributes rather than hashing every file's contents; it detects ordinary edits but is not a content-integrity guarantee against a process that preserves the same metadata. The store uses an atomic same-volume move. If the working copy and local application-data store are on different volumes, the operation fails and leaves the source in place; cross-volume copy-to-recovery is not implemented. Recovery entries consume local disk until restored or explicitly deleted. Headless rendering and input tests do not establish native Windows modal, keyboard or pointer behavior. The independent reviewer quota was exhausted, so this data-loss-sensitive boundary received a structured primary review rather than a fresh external pass.

### Workspace storage inspection milestone

The Storage rail route now inventories primary checkouts and every worktree registered by Git. The shared inspection owner measures logical file size without traversing links, reads tracked and untracked changes through the typed Git client, resolves the local default branch, and checks whether each worktree `HEAD` is already an ancestor of that local branch. Missing registered paths appear as broken references. Filters expose all working copies, local review candidates, changed copies and broken references.

A review candidate remains local triage evidence: a linked worktree that is unlocked, clean and locally merged. The page now offers separate review flows for an existing linked worktree and a Git registration whose path is missing. Both flows refresh their stronger evidence before enabling an action. Primary checkouts are always retained by these workflows.

Evidence:

- Two real-Git shared tests passed. They create a primary repository and linked worktree, verify unmerged-to-merged ancestry changes, invalidate candidate state after an untracked edit, and report a manually missing registered worktree as broken.
- All 32 Avalonia tests passed. The Storage route test verifies measured summary cards, candidate filtering and selection state.
- Wide and compact Skia renders were inspected: `Artifacts/StudioValidation/storage-review.png` and `storage-review-compact.png`. The wide view retains the evidence inspector; compact mode hides that side panel and keeps the measured list scrollable above the output dock.

Reported sizes are logical bytes. Hard links and shared Git objects can make their sum larger than physically reclaimable space. Inaccessible directories are retained with a warning and linked entries are not measured. The inventory scan is local and does not fetch remotes. Large workspaces are inspected sequentially with per-repository progress and a cancellation action. The headless `storage` command reports the same progress on stderr and returns a bounded inventory on stdout; it does not open the release-state database. No user repository was modified during validation; removal and pruning tests used disposable repositories.

Scale check (2026-09-21): a read-only scan of the maintainer workspace completed across 194 discovered repositories and 266 working copies. It measured 477.1 GiB logical, including 81 existing worktrees totaling 228.7 GiB logical, and identified four local review candidates and no broken references. The scan took about eight minutes; progress remained visible throughout. The 1000 × 700 headless progress frame was inspected at `Artifacts/StudioValidation/storage-progress/storage-progress.png`. The rebuilt CLI also returned valid JSON for a disposable primary checkout with one linked worktree. All 54 Avalonia tests and 495 shared tests passed, with one intentionally skipped shared smoke test. A fresh read-only review found and prompted a fix for cancellation during repository discovery. No real working copy was removed or pruned.

### Guarded worktree removal milestone

Storage can open a dedicated removal review for a linked worktree. The shared removal owner requires a physical path below the workspace without an intervening directory link, exact unlocked Git registration, and a clean tracked and untracked state. Publication evidence can follow either of two exact routes: the worktree `HEAD` is an ancestor of the current `origin` default branch, or GitHub reports a merged pull request into that same default branch whose recorded head SHA exactly equals the worktree `HEAD`. It reads the remote default with `git ls-remote --symref origin HEAD`, validates the returned branch ref and object ID, and does not fetch or rewrite local refs. The GitHub route uses the shared bounded read client and returns no match when the origin is not a GitHub repository.

The review also blocks a working copy used by PowerForge Studio's current selection, open documents, build or protected release state. On Windows, PowerForge's Restart Manager owner inspects a bounded sample of up to 256 physical files and blocks removal when another process has an open handle. It reads only process ID and display name, not command lines, environment variables or file contents. Git's ignored-file view is folded into reviewable retained-content roots alongside known build output locations. The user must still confirm that no external editor, terminal, task or process is using the worktree because a terminal can keep the directory current without locking a sampled file. Retained content requires a separate confirmation. The evidence is fingerprinted and rebuilt immediately before execution. Any change invalidates the review. Git removes the worktree without `--force`, after which Studio verifies that both the path and worktree registration are gone.

Broken references use a separate cleanup review. Studio reads Git's listed working copies and independently inventories every immediate administrative entry under the repository's common `worktrees` directory. Each bounded `gitdir` record must map one-to-one to a disclosed working copy; missing, unreadable, linked, malformed, duplicate or unmapped entries block the action. The selected missing path must be marked `prunable`, and the dialog shows the one exact stale registration included in the action. The action is also blocked when that path is outside the workspace, is the primary checkout, still exists, or was not disclosed. Both registry views, the selected administrative identity and filesystem-existence evidence are fingerprinted and rechecked immediately before execution.

Studio atomically creates an exclusive marker at the reviewed missing path. If a new worktree won that path after review, marker creation fails and the replacement is preserved. With the path guarded, Studio verifies the complete registry identity again, moves only the matching administrative directory out of Git's active `worktrees` registry, and rereads the moved `gitdir` target against its original administrative base before deletion. This retains Git's relative-worktree-path semantics. A concurrent `git worktree repair` restores the repointed registration instead of deleting it. Studio then proves the selected registration disappeared while every other registration remained. Any failed identity or post-check restores the administrative directory before releasing the guard. Studio never runs the repository-wide prune command, uses `--force` or `--expire now`, or sends a path-only removal command that could target a same-path replacement.

Evidence:

- Twenty-seven focused shared tests passed. Real Git fixtures cover merged ancestry, exact merged-PR-head fallback for an unmerged local branch, clean no-force removal, open-handle blocking, Git-marked stale registrations, changed-set rejection, duplicate and hidden malformed administrative-registration blocking, and preservation of the primary registration. A late unrelated malformed administrative entry aborts cleanup and remains intact. A second boundary fixture replaces the stale registration with a new clean worktree at the same path after final review; guard acquisition fails and preserves both the replacement registration and its ignored sentinel file. Two repair-boundary cases relocate the worktree and run `git worktree repair` after guarded validation using absolute and relative administrative paths; the quarantined identity check restores each repaired registration and preserves its sentinel. The GitHub client contract proves exact head and base matching, and a live Windows Restart Manager probe observes the test process holding a real file handle.
- All 48 Avalonia tests passed. The storage cases cover invalidating prior removal evidence, discarding late removal and prune reviews after selection or workspace changes, candidate and broken-reference selection, both confirmation gates, and opening the separate removal and prune reviews.
- Wide and compact Skia renders were inspected: `Artifacts/StudioValidation/storage-review.png`, `storage-review-compact.png`, `worktree-removal-review.png`, `worktree-removal-review-compact.png`, `worktree-prune-review.png` and `worktree-prune-review-compact.png`. The compact page keeps its title and actions readable; both dialogs pin their action bars and scroll longer evidence.

The exact merged-PR route supports squash-merged work only when GitHub still records the local commit as the PR head and the base equals the current remote default branch. A branch-name match is never sufficient. GitHub authentication uses the existing shared client configuration; Studio neither requests nor stores a credential. Automatic open-handle evidence is Windows-only and bounded, so external-use confirmation remains an explicit human assertion. Registration cleanup covers only the selected missing path and exact administrative identity already reviewed; Studio does not run a repository-wide prune, expire newer registrations or offer broad automatic cleanup.

### Automation inventory milestone

The Automations rail route now keeps the project tree visible while combining read-only evidence from separate providers. Windows Task Scheduler supplies task state, last run, next run and result through an exact Windows PowerShell probe. The probe excludes Microsoft task folders, caps captured output, invokes the installed `ScheduledTasks` module without a profile and never reads action arguments. Product-name matches and action executable or working-directory paths below the workspace drive the default Relevant filter; All observed exposes the remaining non-Microsoft rows.

Local GitHub workflow files contribute block-form `on` / `schedule` / `cron` definitions from primary checkouts only. Workflow files are bounded to 1 MiB and worktree copies are skipped to reduce duplicate definitions. A bounded GitHub API read now matches their paths to workflow enablement and the most recent observed scheduled run. A missing workflow, inaccessible repository or capped listing keeps the local definition and makes partial evidence explicit. No next occurrence is inferred from cron text. Codex appears as an unavailable provider: the installed Codex app-server schema exposes plugin-declared scheduled-task metadata but no supported API for the user's actual automation inventory. The current official [Scheduled tasks](https://learn.chatgpt.com/docs/automations) documentation directs management to the desktop/web UI, while [App Server](https://learn.chatgpt.com/docs/app-server) documents no automation inventory method (checked 2026-09-21). Studio does not read or edit private Codex automation files.

Evidence:

- Three shared automation tests passed. They keep Windows runtime evidence separate from GitHub definitions, preserve Windows rows when GitHub discovery fails, verify provider status for unavailable Codex integration, enforce the PowerShell output bound and prove that unrelated `cron` keys outside `on.schedule` are ignored while quoted cron text is preserved.
- The exact Windows probe ran read-only on the development workstation. It observed 29 non-Microsoft tasks and classified three current product tasks as relevant without collecting action arguments.
- All 35 Avalonia tests passed. The automation route test covers relevant, all, provider and attention filters while retaining the workspace tree. A controlled delayed source proves that switching workspaces cancels the old inspection and permits an immediate refresh in the new workspace.
- Wide, compact-top and compact-list Skia renders were inspected: `Artifacts/StudioValidation/automations-inventory.png`, `automations-inventory-compact.png` and `automations-inventory-compact-list.png`.

The GitHub runtime read caps itself at 20 repositories, four concurrent requests, 100 workflows and 100 scheduled runs per repository, 4 MiB per response and a 65-second combined deadline. It uses the shared GitHub authentication configuration; no credential is placed in a workspace file. A successful read proves workflow state and a recent run if one appears in the bounded response, but it does not prove that every schedule has fired. Windows schedule text summarizes the first trigger while the provider's next-run timestamp remains the runtime evidence. Tasks identifiable only through command arguments may appear under All observed because arguments are intentionally excluded. Inline/flow-style GitHub schedule YAML is not parsed. Provider-specific detail/history pages, supported provider editing and schedule creation remain future work.

The runtime extension passed 14 focused shared tests, including caller cancellation, origin resolution, response order, remote access failure and actionable scheduled-run outcomes. The full desktop-only gate passed with zero build warnings, 53 Avalonia tests and 493 shared tests; the opt-in smoke test remains skipped, and the existing Apple exception-shape and station projection cases remain excluded by that gate. Read-only live GitHub API checks confirmed workflow and scheduled-run payloads for the PowerForge repository. Wide and compact final Skia renders were inspected in `Artifacts/StudioValidation/automation-runtime/`. An independent local review raised two P2 cases in cancellation and run-outcome classification; both were fixed and closed by targeted confirmation. The Release desktop executable launched against the real workspace and exposed its routes and 194 repositories through UI Automation. The Windows session was locked, so the captured pixels were the lock screen and window activation failed. The exact validation process was closed; native visual interaction remains unverified.

### Connections inventory milestone

The Connections rail route keeps the project tree visible while verifying service, registry and local-tool boundaries through read-only provider adapters. Its domain model can carry an endpoint, credential reference, observed capabilities, verification time and evidence, but has no credential-value field. URI display removes user information, query and fragment data; non-loopback HTTP endpoints are blocked. Provider failures and timeouts are isolated so one unavailable service does not hide other evidence.

GitHub verification invokes the installed CLI with a fixed `auth status --hostname github.com --active` request and discards all command output. The page reports only whether an active account was confirmed and names the GitHub CLI credential store; it does not infer repository or write scopes. Local Git, .NET, PowerShell and GitHub CLI rows use bounded direct version checks. NuGet.org and PowerShell Gallery use public response-header checks; reachability is not described as authenticated publication access.

Licensing remains owned by `Licensing.Core`, `Licensing.Admin` and `Licensing.Release`. Studio calls the public `control.evotec.xyz/healthz` endpoint without credentials and counts protected profile filenames under the existing `Licensing.Admin` profile directory without opening their contents. It does not unprotect a profile or infer authenticated scope. IntelligenceX remains owned by `IntelligenceX.Chat.Client` and `IntelligenceX.Chat.Service`. Studio performs the documented named-pipe `hello` handshake, retains only the sanitized service version and never reads provider profiles or API keys. Both integrations work as optional capability evidence; Studio still runs when either owner is absent.

Evidence:

- Twelve focused shared tests pass. They cover credential/query URI redaction, non-loopback HTTP rejection, locked Licensing profile files that cannot be opened, an explicit reachable-but-unconfigured Licensing state, discarded GitHub command output containing a secret sentinel, GitHub timeout classification, provider failure isolation, provider-owned HTTP timeout handling, portable IntelligenceX owner discovery and a correlated owner handshake that discards unrelated payload fields.
- All 37 Avalonia tests pass. Two focused Connections cases cover route/filter/inspector state, secret-free output, cancellation and immediate refresh after a workspace switch.
- A disposable live console probe observed GitHub authentication; Git, .NET 10.0.400, PowerShell 7.6.5 and GitHub CLI 2.97.0; reachable NuGet.org, PowerShell Gallery and Evotec Control endpoints; nine protected Licensing profile references; and a successful IntelligenceX.Chat.Service 1.0.0.0 handshake. The probe emitted only the same sanitized rows shown by Studio and was removed after verification.
- Wide, compact-top and compact-list Skia renders were inspected: `Artifacts/StudioValidation/connections-inventory.png`, `connections-inventory-compact.png` and `connections-inventory-compact-list.png`. Compact mode retains the tree, collapses the inspector and scrolls the page above the output dock.

This milestone does not edit connections, authenticate a new account, publish a package, retrieve licensing customer data, query download totals or open an IntelligenceX chat session. Those actions must remain in their existing owners and expose explicit capability/scope evidence before Studio enables them. Native Windows keyboard, pointer and credential-flow interaction remain unverified.

### Activity and attention milestone

The Activity rail route is a read-only cross-project inbox. It projects the existing repository catalog, local Git readiness, release drift, release inbox, GitHub issue/PR/CI reads and automation inventory instead of introducing another issue tracker or release state engine. Opening the page never executes a build script, schedule, publication action, issue mutation or pull-request mutation. External probes are bounded to eight repositories by default and three displayed issues per repository.

Every row names its provider and observation time. Provider cards keep Available, Partial, Authentication required, Access denied, Rate limited, Unavailable and Absent states separate, so a failed or deferred probe cannot appear as an empty healthy inbox. The page states when its bounded display omits lower-priority rows instead of presenting a truncated list as complete. Filters expose actionable items, all observed activity, GitHub, releases, schedules and items hidden for the current Studio session. Open source is restricted to GitHub HTTPS links or existing non-linked paths inside the active workspace. Hide for this session changes only the current view and is labelled accordingly; it does not acknowledge or resolve provider content.

Evidence:

- Five focused shared tests pass. A disposable Git repository with a real build script proves Activity inspects the release contract without executing that script, while fake owner evidence contributes a failing CI run, pull request, issue and schedule. A second test proves an automation-provider failure remains visible without discarding local repository evidence. A bounded-timeout case proves a stalled GitHub resolver cannot discard local evidence or masquerade as an empty inbox. Two access cases keep a denied credential distinct from provider rate limiting.
- Three focused Avalonia tests pass. They cover cancellation and immediate refresh after a workspace switch, route state, provider/source cards, counts, filters, selection, session-only hiding and restore, plus the external-open boundary for workspace paths and GitHub HTTPS links.
- All 40 Avalonia tests pass on the final Activity candidate.
- Wide, compact-top and compact-list Skia renders were inspected: `Artifacts/StudioValidation/activity-attention.png`, `activity-attention-compact.png` and `activity-attention-compact-list.png`. The compact shell keeps the project tree, collapses the right inspector and scrolls the inbox above the output dock.

The Activity route currently reads open GitHub issues and aggregate pull-request/CI signals. It does not provide a global issue search, review submission, issue edits, persistent mute rules, GitHub workflow runtime history or durable Activity history. The existing selected-project GitHub page remains the detail owner. Native Windows pointer, keyboard and external-link opening remain unverified.

### Settings milestone

The Settings rail route keeps the workspace tree visible and uses five consistent sections: General, Workspace, Execution, Integrations and Diagnostics. General currently owns document-tab restoration. Workspace owns bounded Activity limits for queried GitHub repositories, issue rows, combined inbox rows and the overall GitHub deadline. Execution and Integrations explain their existing owners without exposing controls that Studio cannot yet apply safely. Diagnostics reports the Studio/runtime/OS versions and the machine-local configuration path.

Editable preferences live in the existing machine-local workspace catalog as a small JSON object. Credentials, source contents, build logs and provider runtime state are excluded. Catalog writes retain explorer state, profiles, templates and unknown future top-level properties, use the existing process lock and atomic replacement, and normalize numeric limits before returning them to either host. Reset only previews defaults; the rest of Studio receives new preferences after a successful durable save. Unsaved drafts survive route, refresh and workspace navigation, can be explicitly discarded, and block window close until the user chooses Save or Discard. Disabling document restoration leaves the saved references intact while skipping their next reopen.

Evidence:

- Seven focused catalog/explorer tests pass. The new case persists deliberately out-of-range values, verifies their normalized bounds, and proves that explorer favorites plus an unknown future JSON field survive the settings write.
- All 41 Avalonia tests pass. The Settings case seeds a saved document and non-default preferences, proves that restoration is disabled, retains a draft across route navigation, keeps previewed defaults isolated until Save, reloads the persisted JSON and passes the saved limits into the Activity owner.
- The retained WPF host builds in Release with zero warnings after the optional catalog field was added.
- Actual Skia rendering was inspected at 1600 x 1000 and 1050 x 720: `Artifacts/StudioValidation/settings-workspace.png`, `settings-workspace-compact.png`, `settings-workspace-compact-bottom.png` and `settings-diagnostics-compact.png`. The wide shell retains the tree and Settings inspector; compact mode hides the inspector while keeping the section selector, scrollable editable limits, diagnostics and output visible.

Workspace-root registration, exclusions, groups, retention, runtime/terminal profiles, keyboard mappings, themes, scaling, provider binding edits and diagnostic export remain planned settings. Current roots are visible but cannot be removed from Settings yet. A future removal control must delete only the registration and must never imply deleting its directory. Native Windows keyboard, pointer, scaling and file-picker behavior remain unverified.

### Build inspection milestone

Build & Run now opens a working-copy-specific inspection page. Its explicit action uses RepositoryPlanPreviewService through a narrow interface; it does not construct a synthetic portfolio or execute on selection. Project and unified release contracts use their existing planning adapters. Module JSON is loaded directly, and PowerShell module contracts export the same JSON contract before inspection. Each successful result now includes an ordered reviewed-action list projected from the authoritative PowerForge plan or resolved module configuration.

Reviewed actions cover module staging, .NET payloads, lifecycle PowerShell actions, NuGet/package lanes, ZIP artifacts, executables, bundles, MSI, MSIX, command hooks and WinGet submission intent. Module rows follow `ModulePipelineStep.Create` order after the same build-only host overrides used for execution. Remote publishing and module installation are clearly deferred to Releases. The display model is capped at 200 rows and 240 characters per field; when a plan is longer, row 200 states how many actions were omitted and points to the plan source. It does not include inline scripts, environment values, publish credentials, raw command-hook arguments or raw WinGet arguments. The original plan/configuration remains linked as the source of truth.

Catalog discovery recognizes Build/project.build.json without a PowerShell wrapper. It checks the repository's Build directory before child directories and prefers JSON within each directory. Invalid project configuration becomes a failed result. Changing working copies cancels the old request and discards late results. Synchronous adapter work may finish before cancellation is observed; the UI does not claim immediate termination.

Evidence: 17 focused reviewed-plan/planner tests cover direct module JSON, direct project JSON, unified module JSON, legacy PowerShell-generated project plans, NuGet and symbol packages, ZIPs, executable publish steps, MSI, MSIX, command hooks, WinGet, invalid plan rejection and explicit 200-row truncation. The 53 focused PowerForge module planning/preparation tests and all 46 Avalonia tests pass. PowerForge.PowerShell builds without warnings for net472, net8.0 and net10.0, and the legacy WPF host builds cleanly. A controlled delayed-planner test verifies cancellation and stale-result suppression while switching working copies. The rendered page was inspected at 1600 × 1000 and 1050 × 720, including the reviewed action list in `Artifacts/StudioValidation/build-plan-actions.png` and `build-plan-actions-compact.png`; the compact page scrolls its content below the output dock. Native input/scrolling proof remains open. Test dispatchers are serialized because Avalonia's application state is process-wide.

The earlier build-planning review found two P2 issues: nested JSON taking precedence over a root script, and validation being described as full planning. Both were corrected and confirmed in one targeted follow-up. The reviewed-plan extension retains the established constructor contract for stored plan results; action rows are inspection-time evidence and are regenerated from the current configuration. Small rendered evidence remains under `Artifacts/StudioValidation/build-plan*.png`.

Independent read-only review `/root/review_build_reviewed_plan` found three P1 request/order defects and three P2 validation/presentation defects in the first reviewed-action candidate. The shared request factories, canonical module planner, explicit truncation row, fail-closed project plan parser and status-specific badges address the complete wave. The same reviewer confirmed the six bounded remediations in patch fingerprint `d786f9e8b3000ff0add4afaf939d8667d8e7d962d63bdcd4105f21df511c41f6` with no remaining actionable P0–P3 finding. Boundary: candidate-reviewed.

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

Remaining execution work includes arbitrary executable/PowerShell task profiles, live raw process output where supported, durable activity/history and artifact provenance, release signing/publishing/verification controls, and native interaction proof. The earlier checklist remains the full replacement scope.

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

PR details capture the head SHA returned by GitHub, then load check runs and latest commit status per context for that SHA. Failures are visible; access failures for checks leave the discussion readable. This snapshot is not a merge-policy decision. PR file review is covered by the following milestone. Review posting, branch protection/ruleset evaluation and merge actions remain unfinished. Discussion Markdown is currently shown as selectable plain text. Open on GitHub uses a URL constructed from the validated repository and numeric item number, not remote body links.

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


### Pull-request changed files

Review files opens a file list and patch pane in the central workspace. It shows change status, additions/deletions and previous paths for renames. Remote paths remain display data; they do not become local file-operation targets. Patches are selectable plain text and have a 262,144-character preview limit. Missing patches explicitly identify unavailable text rather than implying no changes. All patches are labelled excerpts because GitHub can omit unchanged context and some large-file content.

The shared GitHubProjectService validates the expected PR head before fetching files and compares both head and base afterward. If either revision changes, the page asks for a detail refresh and returns no file snapshot. This checks the observations around pagination; it is not an atomic server-side snapshot or a guarantee that the PR cannot change after loading. Five-page listing limits and GitHub's reported changed-file count determine the incomplete-list warning. Back navigation, project switching and detail reload invalidate pending file results.

Evidence:

- Seventeen shared file/read contract tests passed, including four new cases covering initial head mismatch, head/base changes during pagination, rename metadata, missing binary patches, bounded large patches and incomplete coverage.
- Three GitHub Avalonia tests passed, including ignored cancellation during back/project navigation and actual Skia file-review renders at 1600 × 1000 and 1050 × 700. Evidence: Artifacts/StudioValidation/workspace-pr-files.png and workspace-pr-files-compact.png. Native Windows input, browser launching and long-path/large-patch interaction remain unverified.
- A disposable harness consumed the actual shared service against PowerForge PR 958. It returned two changed files, two text patches, no local patch truncation and no partial-list flag; revision validation completed. No remote writes occurred and only counts were logged.
- Independent read-only review /root/review_pr_files inspected the staged milestone against bc4cff7f4, fingerprint 7c735286ee0854241421e62686fd5623629f018e, and both renders. It found no actionable introduced defect. Boundary: candidate-reviewed; no targeted confirmation needed.

Review submission is not yet implemented. The existing Open on GitHub action remains available from the discussion view. Authentication and HTTP response bounds remain owned by the shared GitHub service.

Task-owned cleanup removed the disposable file-review harness and completed shared-test binaries (about 146 MiB) after containment, link, tracked-file and process checks. Active Avalonia outputs and small rendered evidence remain local.


### Release preparation

The Releases tab accepts the completed build's captured working copy and artifacts. It prepares the existing ReleaseQueueRunner signing checkpoint through ReleaseBuildHandoffService, then lists the canonical signing manifest. It does not infer a release from the project currently selected in the explorer. A newer build invalidates an existing handoff and late preparation results; cancelled builds cannot be prepared. Failed adapters, missing artifacts, relative artifact paths and empty manifests produce explicit errors.

Preparation checks artifact existence, not content integrity or signing readiness. It performs no signing, feed publication or remote mutation. The handoff is currently in memory; durable release history and resume remain required. Signing, target preview, publication, verification and their receipts are the next release-workspace steps, using the existing shared executors rather than new UI-owned release rules.

Evidence: the real JSON-only build test produced a NuGet package containing the expected assembly, prepared its canonical signing checkpoint, rendered the package and output directory at 1600 × 1000 and 1050 × 720, then removed the package and verified that preparation failed without retaining the prior handoff. A subsequent compiler failure disabled preparation. A controlled late-result test verified invalidation by a newer build and cancellation exclusion. The focused shared handoff test checks canonical checkpoint contents, missing/relative artifacts and failed/empty builds. Actual rendered evidence is in Artifacts/StudioValidation/release-prepare.png and release-prepare-compact.png; native Windows interaction remains unverified.

This bounded read-only adapter used focused service, actual-build and rendered validation. No new independent review was requested for preparation alone; the forthcoming stage-execution and durable-state boundary requires its own risk-based review before publication. No public package, user certificate or user release configuration was modified.


### Release cancellation and receipt retention

Before connecting signing controls, the shared signing executor was hardened to retain completed artifact receipts when a later artifact is interrupted. Interrupted and unattempted artifacts receive failed receipts, no further signer is started after cancellation, and cancellation after the last artifact still prevents a successful signing-stage result. Expected filesystem, signing-state and cryptographic exceptions become artifact failures without discarding earlier receipts. Invalid, failed, mismatched or empty build checkpoints cannot advance signing. Archive refresh is withheld when signing failed or was cancelled; completed artifacts still have integrity digests captured. Cancellation can wait for already-running postprocessing or digest capture to finish.

The queue command owner now saves returned executor evidence with an independent 30-second finalization timeout. This keeps an already-cancelled execution token from discarding local receipts and checkpoint updates. Cancelled build/signing results remain failed. Actual returned publication/verification results are retained as observed facts; cancellation does not pretend that completed remote publication was rolled back. Local receipt and queue writes still use their existing separate persistence calls; transactional consolidation remains part of durable release-workspace work.

Thirty focused tests passed across signing, queue transitions and returned-result finalization for build/sign/publish/verify. After the retry correction, the decisive 23-test subset passed again. Regression fixtures mutate an artifact before throwing, retain earlier receipts, serialize the failed checkpoint, and exercise both single and batch retry. Database-backed tests verify receipt retention and rebuild routing after late cancellation. Signing uses controlled process-boundary delegates; no user certificate, public package feed or remote release was modified.

Independent review found a P2: retry could reuse a build checkpoint after partially modifying a signed artifact. The result now carries a serialized RequiresRebuild flag for interrupted or failed execution. Both retry paths clear the old checkpoint and queue a rebuild. Configuration failure before execution keeps its existing signing retry behavior. The same reviewer confirmed this correction with no further findings; confirmation patch fingerprint: 4ab451c5e3bc5f64adedbe5c7fecf2f330eb46a2. Boundary: candidate-reviewed. Real certificate signing and native process termination remain unverified; Avalonia signing controls are not yet connected.

Next: connect signing through a captured release session, prevent simultaneous rebuilds of its artifacts, retain stage progress and receipts, and consolidate durable checkpoint/receipt persistence before adding publication and resume. Task-owned shared-test binaries (about 75.6 MiB) were removed after validation; active Avalonia outputs and small evidence remain.


### Explicit signing in the release workspace

The Releases page now runs signing through ReleaseSigningWorkflow, the canonical signing executor and queue transitions. It operates on the captured handoff, retains per-artifact receipts, and reports cancellation or a rebuild requirement. Publication remains a separate action and is not invoked by signing. The UI shows indeterminate stage progress because the executor does not yet expose per-artifact progress events.

Workspace build commands are disabled while signing is active. Switching projects leaves the captured release root intact. Closing the window during signing keeps it open and brings the release page forward, allowing cancellation and receipt capture to finish. A successful stage cannot be signed again from the same handoff. An execution exception requires rebuilding; pre-execution configuration failures can be prepared again after correcting configuration.

Five focused Avalonia tests passed, including the existing real JSON build/NuGet artifact workflow and new success/cancellation tests through the actual signing workflow with a controlled signer. Two decisive tests passed again after adding the window-close guard. They cover project switching, build interlocking, active-window close, cancellation after a returned receipt, and canonical stage transitions. Wide and compact Skia renders were inspected at 1600 x 1000 and 1050 x 720: Artifacts/StudioValidation/release-signing-complete.png and release-signing-cancelled-compact.png. Native Windows input and real certificate signing remain unverified.

Receipts and the release handoff remain in the current app session. Durable storage, resume, target preview, publication, verification and per-artifact live progress are still required. Shared storage inspection found that existing queue/header/item and receipt replacement calls do not form one transaction; consolidation through the existing DbaClientX owner is needed before claiming crash-safe recovery. No user artifact was signed or published during validation.


Independent review of the signing UI found a P2 close/save race. A controlled state-store test reproduced signing starting while close awaited persistence. Moving the signing guard ahead of final-close authorization fixed that sequence. Targeted confirmation identified retained close authorization after a blocked close; a second regression reproduced skipped unsaved-document handling. The guard now clears both close/discard flags. The extended test edits a document after the blocked close and verifies that a subsequent close invokes draft resolution and preserves the draft when cancelled. Five focused signing/preparation/editor tests passed after the final correction. The final two-line reset was validated by regression and a local inspection of both close continuation branches; no additional independent full pass was requested. Review boundary: candidate-reviewed with both reproduced findings addressed.

Compact artifact paths now use single-line ellipsis with full-path tooltips. The final rendered compact state was inspected. Small retained validation artifacts total about 2.6 MiB; disposable build and close-race fixtures were removed by their test cleanup. Active app/test outputs remain for ongoing implementation.


### Atomic release checkpoints

ReleaseStateDatabase now owns an atomic PersistReleaseCheckpointAsync operation using DbaClientX SQLiteAsyncSession transactions. Queue header and item replacement plus supplied signing, publication and verification receipt sets commit together. Null receipt sets preserve existing evidence; empty sets explicitly clear the selected scope. Queue execution supplies the current working-copy root so advancing one project preserves receipts belonging to other projects in the session. A mismatched receipt root rejects and rolls back the whole update.

Queue command finalization uses this operation with its existing independent timeout. LoadReleaseCheckpointAsync reads a requested session and all receipt sets in one transaction. Command results use the captured session ID instead of reading whichever session was created most recently. PersistQueueSessionAsync also makes its header/item update atomic. Domain queue SQL lives in a separate partial file; provider connection, transaction and rollback behavior remain in DbaClientX. No schema migration or new provider dependency was introduced.

Twenty-one focused tests passed: eleven checkpoint/queue execution tests and ten existing database/command-state tests. Real disposable SQLite databases verified rollback after header/item changes, failure during receipt replacement after an earlier insert, cancellation between receipts, reopening the prior state, scoped replacement and scope mismatch, null/empty receipt semantics, and captured-session reads after creation of a newer queue. Existing build/sign/publish/verify finalization tests continue to pass.

This storage milestone does not yet persist the Avalonia release handoff. Durable UI history/resume still needs integration, including an in-progress marker and exclusive/conditional execution claim before signing starts. Atomic finalization alone does not make artifact mutation and database updates one transaction or prevent duplicate execution by separate app instances. Legacy standalone receipt-writing methods retain their previous behavior; execution finalization uses the new combined operation. Local validation consumes the existing sibling DbaClientX source references; packaged distribution remains part of final migration delivery.


Independent read-only review /root/review_release_storage found no actionable defect in the frozen storage candidate, fingerprint 2db1ed5fc7886a686afb2ff249a8550aca3c8a3f. The review inspected DbaClientX transaction semantics and the queue integration. Boundary: current; no targeted confirmation required. Task-owned shared-test binaries (about 75.7 MiB) were removed after containment, tracked-file, link and process checks. Active Avalonia outputs and compact evidence remain.


### Conditional signing claims

TryAdvanceReleaseCheckpointAsync creates a session only if absent, or advances exactly the checkpoint the caller observed. The comparison and write run within one DbaClientX transaction. Session identity, workspace, creation time, scope and ordered items form the comparison; summary counts are derived when loading. Stale callers make no checkpoint or receipt changes.

DurableReleaseSigningWorkflow claims a new captured session by saving a failed-signing result with RequiresRebuild before calling the executor. The marker explicitly says signing may still be running or may have been interrupted: it is not evidence that a process has stopped. Successful or returned-failed execution replaces that exact marker and commits its receipts. A thrown executor exception leaves the marker. Finalization uses a separate 30-second token; a failed finalization returns the completed execution evidence with PersistenceError instead of discarding receipts.

Nine focused tests passed against disposable SQLite databases. They verify one winner for competing expected-state updates, exclusion of a second execution for the same session, reopening success and interruption records, and a trigger-injected receipt write failure that rolls back the completion checkpoint while preserving receipts in the returned result. These tests use controlled signing executors; no user artifact or certificate was used.

This owner is not yet the Avalonia default. The next UI integration must display saved-session history and handle PersistenceError with an explicit save/recovery path before clearing receipts or closing. Per-session claims do not exclude different sessions targeting the same artifact paths or unconditional legacy writers. Working-copy exclusion and cautious reopen behavior remain requirements for final release workflow delivery.


Independent read-only review /root/review_signing_claim found no actionable defect within the single-session signing claim contract, fingerprint 5469dc2efe854ab6b7feb6f6c4965310bd970d6e. Boundary: current. Multi-item expected checkpoints must retain canonical QueueOrder ordering, since storage reloads items in that order. Existing file line endings were restored after review; no semantic changes followed. Task-owned shared-test binaries were removed after validation; active Avalonia outputs remain.


### Durable signing and saved-release UI

Avalonia now uses the durable signing workflow by default. Its dedicated release-history.db lives under the portable Studio local-data owner, separate from the older releaseops.db queue. The release page can refresh the 100 most recently created sessions and open a captured session with its signing receipts. Reopened records are view-only: they cannot implicitly re-sign or publish. Execution resume and publication remain further delivery work.

A failed completion save leaves receipts visible and protects them from new builds, replacement preparation, history navigation and window close. Retry saving invokes only the local conditional persistence operation. It recognizes an already-committed identical checkpoint and receipts, making a lost-response retry idempotent. A checkbox plus explicit discard action allows the user to abandon an unsaved copy; that leaves the durable interrupted record intact and requires rebuilding before continuing.

Cooperating durable signing instances using the same journal hold a per-working-copy file lease through signing and finalization, in addition to the per-session database claim. Empty lock files remain as stable identities; file handles provide exclusion and release automatically when the process ends. These leases do not stop external build tools, legacy executors or different filesystem aliases from changing artifacts. The app's build/close interlock also remains active during local receipt recovery.

Validation uses a controlled signer and a real disposable database. An injected receipt-write failure preserved UI evidence and blocked close/build, retry saved without invoking the signer again, and a fresh release view reopened the receipt. Shared tests cover different sessions contending for one working copy and repeated persistence retry. Five focused Avalonia tests and nine shared storage/signing tests passed before final review. Wide and compact Skia renders were inspected, including the scrolled compact recovery state. Native Windows input and real certificate signing remain unverified.


Independent review /root/review_release_history identified a P2 Windows case-variant lease bypass. The focused Windows regression failed before normalizing casing and passed afterward. Targeted confirmation found the correction addressed, fingerprint f110df7600bd10eacf73a5f003f4ab03ef3c5f96. Final validation passed ten shared tests and five Avalonia tests. A small subsequent guard resets discard confirmation for every new build/signing result; the UI regression checks that an earlier checked value cannot authorize discarding a later failed save. Review boundary: candidate-reviewed. No further full review loop was run.

Cross-process OS interaction remains unverified independently; the tests exercise competing handles/instances in the same process. Execution resume, publication, verification and external-artifact mutation exclusion remain incomplete. Task-owned shared-test binaries were removed after containment, tracked-file, link and process checks; active Avalonia outputs and small visual evidence remain for ongoing work.


### Durable per-artifact signing progress

Signing now emits ordered Running, outcome and finalized updates for every captured artifact. The durable workflow commits each update to the release database before forwarding it to the live Avalonia view. A failed first progress write therefore leaves the existing interruption marker and prevents the signing executor from reaching artifact mutation. Updates use the captured release session, retain stage, artifact path, completed/total counts and a bounded sanitized detail, and keep the latest 500 rows per session.

The Releases page shows determinate signing progress and a bounded 100-row live journal. Opening saved release history restores the same durable events, so an interrupted or completed session retains the last observed artifact state after an application restart. Progress evidence is observational: Running proves the signer was about to enter that artifact operation, while an absent completion or finalized update means the outcome remains uncertain and requires the existing rebuild/reconciliation path.

Focused validation covers the real signing executor's event order, cancellation and partial failure, trigger-injected progress write failures before and after mutation, restart readback, schema-20 direct-open migration, live UI updates and saved-history restoration. The final signing/schema set passed 24 shared tests, and the signing/history surface passed four Avalonia tests. All 48 Avalonia tests passed before the direct-open correction; the affected four passed again afterward. Another 460 shared tests passed with the known unrelated Apple exact-exception and environment-dependent release-station cases excluded. The retained WPF host builds without warnings. Wide and compact Skia renders were inspected in `Artifacts/StudioValidation/signing-progress/`, including completed, cancelled and reopened-history states. Real certificate signing and abrupt process termination remain unverified. Publication and verification still expose stage-level progress only and are the next extension of this journal.

Independent read-only review `/root/review_signing_progress` found no actionable P0-P2 issue in the frozen candidate, fingerprint `5d997ed5f9c0d849af42c5833639a9464ce3b026c88181910ffccafe523ef51c`. A subsequent primary call-site audit found that direct history loading did not initialize an older journal before reading the new table. `ReleaseHistoryService.LoadAsync` now migrates an existing journal first and returns null without creating a database when the file is absent. The same reviewer accepted that bounded correction and its schema-20 regression in remediated fingerprint `649efc849aaa613a5a22f39c237138e269fab46dd687a22679ec15e90f99cc78`. Boundary: candidate-reviewed with targeted confirmation.


### Publication destination inspection

The release page now inspects publication targets from its captured successful signing checkpoint. Project-build NuGet and GitHub destinations are read through a new credential-free ProjectBuildPublishHostService preview API in PowerForge. The preview shares existing feed-selection rules, returns publish flags and display destinations, and does not resolve credential files or environment variables. URI user information, query and fragment components are removed and marked as omitted. The DTO carries no credential values or credential-file references.

Studio filters disabled project destinations and shows the configured feed/repository beside captured artifact targets. Module and unified targets still use the existing engine projection, which can contain generic destination descriptions. The page explicitly labels this as destination inspection, not a credential check, readiness assertion or executable approved plan. There is no publication action in this milestone. Configuration fingerprinting, complete destination resolution, publication cancellation/receipt retention and durable remote-operation recovery remain necessary before connecting execution.

Three focused shared tests passed for default and GitHub Packages destination rules, locked credential-file inputs, DTO secret exclusion and URI redaction. The existing real-database signing/recovery UI test now inspects a configured JSON NuGet destination through the actual preview service. Two focused Avalonia tests passed. Rendered target rows were inspected at 1600 x 1000 and 1050 x 720 (scrolled): Artifacts/StudioValidation/release-publication-preview.png and release-publication-preview-compact.png. PowerForge builds for net472 and net8.0 passed without warnings; net10.0 was built by the focused tests. No remote publication or credential use occurred.

Independent review found a malformed-URL credential display path. The preview now replaces URL-shaped values that fail URI parsing with a neutral invalid-destination label. Five shared tests pass, including invalid port and invalid IPv6 examples. One targeted read-only confirmation accepted the correction; no further full review was run. Task-owned shared-test binaries were removed after containment, link, tracked-file and process checks.

### Publication checkpoint integrity

The shared publication executor now requires a ready Publish queue item and a successful signing checkpoint for the same working copy. Failed or rebuild-required signing results, invalid receipt status and mismatched receipt roots are refused. Artifact digest validation now also covers standalone project and script-module targets. Metadata-only unified operations may still have an empty receipt list; their existing destination-specific validation remains in place.

Project GitHub publication validates the actual planned archive paths against signing receipts and rechecks their digests after plan generation, before calling the shared publisher. This prevents a generated plan from selecting an unapproved archive or silently substituting a different archive in reporting. It is a preflight guarantee, not protection against external mutation after the last hash check.

Validation passed 84 publication, unified-checkpoint and verification tests plus the Avalonia release-history test. Regression cases cover modified or missing files, absent digests, failed signing, rebuild-required state, mismatched working copies, wrong queue state, unreceipted Single/PerProject GitHub assets, and plan-time mutation/deletion. All publication calls use controlled fixtures; no remote package was published. Independent review /root/review_publication_integrity found the plan/receipt mismatch; one targeted confirmation accepted the correction. Review boundary: candidate-reviewed.

Next: retain partial publication receipts across cancellation/errors; extend conditional journal transitions to publication; bind the displayed destination plan to execution; connect guarded UI execution and verification. Current cancellation can throw after earlier uploads and discard accumulated evidence. Publication controls remain unfinished until these boundaries are implemented.

### Interrupted and partial publication evidence

The shared executor now returns completed adapter receipts when a later publication operation throws or is cancelled. Results identify cancellation and require remote reconciliation before whole-stage replay. Single and batch queue retry preserve these checkpoints instead of unwrapping and rerunning them. A normal mixed success/failure result also requires reconciliation, because replay would repeat already completed targets. Successful publisher results are retained when cancellation races their return.

The change covers receipt accumulation in project, module, module-owned package and unified adapters. Exceptions crossing the boundary use a neutral failure message rather than exposing raw exception text. It cannot recover an individual upload that never returned evidence from the shared publisher; such remote outcomes remain uncertain. Process-crash journaling and granular per-upload progress are still necessary before enabling Avalonia publication.

Independent review /root/review_publication_interruption found a P1 gap: the canonical NuGet process can return unsuccessful exit 130 on cancellation rather than throw. The regression failed with the guard removed and passed after restoring it. The sibling returned-failure paths were checked, module result success/failure mapping was corrected, and one targeted confirmation accepted the fixes. A subsequent narrow guard covers ordinary mixed success/failure replay, with direct regression proof. Final validation passed 95 publication, queue retry, cancellation persistence and result-factory tests. Review boundary: candidate-reviewed; no further full review loop. No real publication was attempted.

### Durable publication journal

A shared durable publication workflow now conditionally replaces the exact saved publish-ready session with an interruption marker before invoking the publisher. Completion and publication receipts commit in one DbaClientX transaction. An executor exception leaves an explicit uncertain outcome; final receipt persistence uses a separate bounded token so user cancellation does not discard returned evidence. Signing and publication now share the same cooperative working-copy lease.

Failed finalization returns pending evidence and a local-only retry operation. Retry accepts an already-committed identical checkpoint and full receipt multiset, regardless of database ordering, while rejecting changed evidence without overwrite. Fourteen focused durable-publication, signing and checkpoint-storage tests passed. They exercise contention, cancellation, missing/stale checkpoints, a transactional receipt-write failure, recovery without republishing, reopening and repeated-save idempotence. The final four publication tests also prove changed persisted evidence is rejected.

Independent review /root/review_durable_publication found a P2 ordering defect in the idempotent comparison; the multiset correction and expanded regression were accepted in the one targeted confirmation. Boundary: candidate-reviewed. Process termination and cross-process lease behavior remain unverified independently. The workflow is not yet wired into Avalonia.

The publication receipt-key limitation identified here is resolved by schema 19 below. Verification schema and destination identity are resolved in schema 20 below; durable verification and UI execution remain open.

### Publication receipt schema 19

Publication receipts now use an internal integer row identity, allowing the same named target to retain separate destinations and duplicate evidence. Existing readers and writers keep their explicit evidence column lists. The legacy table is renamed, copied and replaced within one DbaClientX SQLite transaction; older source_path addition and index recreation are part of that transaction. Invalid legacy data leaves the original table intact for repair, and repeated initialization does not recopy evidence.

Twenty-three focused schema, durable publication, checkpoint and state-database tests passed. Evidence includes exact legacy receipt preservation, null destination/source fields, repeated and concurrent initialization, older missing-source-column databases, multiple feeds, duplicate counts, and rollback followed by repair/retry. The durable save-recovery fixture now uses two destinations for the same target name and kind. Independent review /root/review_publication_schema found no actionable issues, fingerprint 97db3023bedbe2381f6ff358a777039c3f397a4b; boundary current. No user database was opened or migrated, and abrupt process termination remains untested.

### Verification destinations and schema 20

Verification targets now use structured identity including destination and source path. Distinct case-sensitive feed paths remain separate, delimiter characters cannot combine unrelated identities, and the existing shared string-key projection API retains its case-insensitive behavior. Verification receipts use an internal row identity so multiple destinations and duplicate evidence survive storage.

The legacy verification table is copied and replaced transactionally. Twenty-one focused verification, schema, projection and checkpoint tests passed, including two feeds for one package through preview, controlled HTTP probes and database readback; exact legacy evidence retention; rollback and repair; repeat initialization; and concurrent initialization. No real publication or user database migration occurred. Independent review /root/review_verification_destinations found no actionable issues, fingerprint 7fc634cc731b45af7b9ccec251c8d7be4946ab2f; boundary current. Separate-process migration concurrency and forced process termination remain untested.

Next: retain partial verification evidence across cancellation/errors, add durable verification, bind displayed publication destinations to execution, and connect the Avalonia release controls. Unknown published target kinds currently return Skipped and must not be presented as verified delivery.

### Interrupted verification evidence

Verification now returns completed checks when a later probe is cancelled or interrupted, adds a neutral failed receipt for the unfinished target, and stops before subsequent targets. A cancellation racing a successful final probe does not erase that success. Exception details are not copied into durable or UI-facing evidence. Failed verification can be safely retried from its preserved publication checkpoint because verification probes are read-only.

Published targets no longer complete the queue when the configured destination cannot be probed or the target kind has no verifier. They remain explicitly failed and unverified. A publication receipt that was intentionally skipped still produces a skipped verification receipt.

Thirty-five focused verification, cancellation, result-factory and queue-transition tests passed. Controlled HTTP fixtures cover cancellation before, between and during probes, final-result races, unsupported target kinds, missing destinations, database readback and retry restoration. No external service was contacted. The independent local review could not start because the reviewer agent quota was exhausted; a structured primary review found no actionable defect. Independent review remains an explicit validation gap for this boundary.

Next: add a durable verification claim/finalization workflow, then connect publication and verification execution to the Avalonia release page with explicit acknowledgement and saved receipts.

### Durable verification journal

A shared durable verification workflow now conditionally replaces the exact saved verify-ready checkpoint with an interruption marker before remote probes begin. The same working-copy lease used by signing and publication prevents overlapping cooperating release operations. Completion and verification receipts commit in one database transaction.

If final persistence fails, the workflow returns the receipts and marker needed for a local-only retry. Retry accepts only the exact completed checkpoint and complete receipt multiset, so it neither repeats remote probes nor overwrites different evidence. The workflow disposes the default verification host it owns while leaving injected hosts under caller ownership.

Thirty-six focused durable verification, publication, signing, verification execution, cancellation and checkpoint tests passed. They cover the in-progress marker, competing-operation exclusion, cancellation, successful completion, receipt-write rollback, recovery without another probe, idempotent save, changed-evidence refusal, cross-working-copy evidence rejection, missing checkpoints and multiple destinations. The validation used controlled fixtures and no external service. A structured primary review found and corrected default verifier disposal. Independent review remains unavailable because the local reviewer quota was exhausted.

Next: connect the durable publication and verification workflows to the Avalonia release page. The UI must capture an inspected destination snapshot, require an explicit publish acknowledgement, surface progress and receipts, prevent navigation/close while evidence is unsaved, and clearly distinguish verified, failed and unsupported delivery.

### Avalonia publication and verification controls

The Avalonia release page now carries a prepared release through destination inspection, explicit publication acknowledgement, durable publication, and durable verification. The page shows publication and verification receipts separately, offers cancellation while each operation is running, and restores all three receipt types in saved history. Actual external publication remains additionally gated by `RELEASE_OPS_STUDIO_ENABLE_PUBLISH=true` in the shared executor.

Publication is enabled only for the exact captured release session after targets have been inspected. Studio re-inspects and compares the complete displayed target set immediately before invoking the durable workflow; any destination or artifact-target change clears approval. This binds the current UI decision to the visible targets, while build/signing hashes protect captured artifacts and module, unified and project-build JSON configuration. The project-build checkpoint is detailed below.

Running publication or verification, and any locally unsaved receipts, keep the release page visible and prevent window close, history changes and new build replacement. Local save retry routes to the owning signing, publication or verification workflow without repeating remote work. An explicit discard leaves the durable marker for later review. User-facing summaries and receipt fields pass through the Studio sanitizer.

All 30 Avalonia tests pass. New cases cover the full inspect/acknowledge/publish/verify view-model flow, a changed destination blocking publication, unsaved publication receipts blocking navigation and build replacement, and local save recovery without another publish. The rendered page was inspected at 1600 x 1000, at its bottom receipt state, and at 1050 x 720: `Artifacts/StudioValidation/release-publish-verify.png`, `release-publish-verify-bottom.png`, and `release-publish-verify-compact.png`. Controlled workflows were used; no package, release or network request was published. Independent local review remained unavailable because the reviewer quota was exhausted; structured source and rendered-state review found and corrected release-operation disposal and generic close/navigation messages.

Next: add progress events below the stage level and exercise the guarded default workflows against a private disposable feed/repository before describing external publication as end-to-end validated.

### Project-build publication configuration checkpoint

Every Studio project build now fingerprints the exact JSON configuration resolved for that build. The fingerprint is stored in the build checkpoint, survives signing, and is validated before and after publication configuration parsing. A change to any JSON option, including fields that do not alter the displayed destination row such as a GitHub tag, release name or release mode, blocks publication and requires a rebuild. Configuration changes detected while the build is running also prevent creation of a successful checkpoint.

Project publication fails closed when the signing state has no readable build checkpoint or when an older checkpoint has no project-configuration fingerprint. This intentionally requires a rebuild after upgrading rather than allowing a legacy checkpoint to publish against current configuration. The loaded publication configuration is immutable for the operation after the second hash check. The guard addresses accidental or cooperating-process drift; it is not a filesystem lock against a hostile process that swaps and restores bytes inside the narrow read/check interval. Credential environment values and external credential-file contents are resolved at publication and are not copied into the checkpoint.

Seventy-eight focused tests passed: all 76 project/module/unified publication cases plus the two direct project-build checkpoint cases. They prove an unchanged JSON contract publishes through controlled NuGet and GitHub delegates, changed or missing checkpoint state invokes no publisher, destination drift after signing is rejected, and drift during execution aborts the build checkpoint. The publication suite also retains its cancellation, partial-evidence, artifact-integrity and fail-fast coverage. No external feed, GitHub release or user repository was modified.

Independent local review remained unavailable because the reviewer quota was exhausted. A structured primary review found and fixed the missing-checkpoint bypass before the final test run. The earlier broad build-class filter also selected the unrelated Apple source-trust matrix, whose macOS path-attestation fixtures fail on this Windows host; that run was stopped. The exact 78-test filter above is the validation boundary for this milestone.

### Durable publication and verification progress

Publication and verification now use the same stage-neutral durable journal as signing. The publication executor registers the exact reviewed destination rows as stable work units, records a target before its adapter enters remote work, and records the terminal published, failed or skipped outcome from returned receipts. Aggregate NuGet, module-package and unified-release rows remain aggregate by design because those are the units the operator approved. A running unified target without a terminal event is conservative reconciliation evidence: the shared engine may have reached that target before an interruption, so Studio does not present untouched remote state as known.

Verification records each publication receipt independently as planned, checking and verified, failed, skipped or cancelled. Journal writes use an independent local token so cancellation does not discard the last evidence update. The durable workflows persist every progress event before forwarding it to the live Avalonia sink; an injected journal failure before the executor starts prevents the controlled remote mutation or probe. Saved release history restores signing, publication and verification rows chronologically from the same bounded 500-row database journal, while the live page retains the newest 100 rows.

The Avalonia Releases page now has one labeled execution-progress surface for all three stages. It resets the current counter between stages without clearing earlier events, uses a determinate bar after the first target update, and removes the duplicate publication and verification spinners. Empty signing receipts no longer create an orphaned heading. Wide, bottom-scrolled and compact Skia renders were inspected in `Artifacts/StudioValidation/publication-progress/`.

Validation passed all 49 Avalonia tests and 121 release-specific shared tests covering real controlled NuGet publication and verification adapters, durable signing/publication/verification claims, cancellation, partial evidence, state storage and schema behavior. The retained WPF host also builds without warnings. No real feed, GitHub release, PowerShell repository or external verification endpoint was modified. Abrupt process termination and a disposable authenticated end-to-end publication remain unverified.

### Avalonia default product entry points

The repository build, run and publish commands now target the Avalonia host exclusively. The product assembly and app host are named `PowerForgeStudio`, so a Windows publish produces `PowerForgeStudio.exe` without exposing the UI framework in the operator-facing filename. The former WPF compatibility switches were removed with the retired host.

`Build-PowerForgeStudio.ps1` builds Avalonia and runs its UI suite plus the complete shared Studio suite by default. `Run-PowerForgeStudio.ps1` accepts an explicit workspace and otherwise keeps the saved-root behavior. `Publish-PowerForgeStudio.ps1` retains framework-dependent, self-contained and single-file modes and supports portable Avalonia runtime identifiers; Windows remains the first supported and validated target. The maintainer runbook describes these paths and the current local state files.

The exact build path produced the renamed application assembly with zero warnings and all 50 Avalonia tests passed. The full shared suite still detects existing stale Apple source-trust exception assertions and an environment-dependent station-projection case; those unrelated failures remain enabled in the normal wrapper and are disclosed below. Both framework-dependent and self-contained `win-x64` publishes completed and produced `PowerForgeStudio.exe` with their expected dependency sets. The framework-dependent executable launched against the dedicated worktree, discovered its repository and exposed the complete project tree, project tabs, files surface and output dock through Windows accessibility. Native capture could not bring the validation window to the foreground (`failed to activate captured window`), so final pointer and keyboard interaction proof remains unavailable. The agent-owned process was closed afterward.

The WPF source and tests were removed after the Avalonia host covered the retained workflows; the final retirement evidence appears below.

### Avalonia branch management

The Changes page now keeps local branch work beside status, diff, staging and commit operations. It lists branches observed by the shared Git owner, disables switching to the current branch, and offers an explicit `Create & switch` action. The controls wrap as two intact field/action groups at compact widths. Unfinished branch names are kept separately for each working copy, and an operation that completes after the user selects another project cannot clear or replace that project's draft.

`ProjectGitService` owns the mutation boundary. It validates full `refs/heads/...` names, rejects option-like and Git shorthand expressions, verifies that switch targets are existing local branches, and turns Git failures into the same sanitized exception contract as other Studio mutations. Failed creation retains the entered name for correction. No branch action fetches, pushes, deletes a branch or changes a remote.

Validation passed all 50 Avalonia tests and the eight focused shared Git-change tests. Real disposable repositories cover creation, switching, duplicate-name failures, invalid names, local-target enforcement, operational Git failure classification, unchanged repository state after rejection, and staging/commit/rename behavior. A delayed-process test switches projects while branch creation is paused and proves the original repository remains the mutation target while the newly selected project's draft survives. Wide and compact Skia renders were inspected in `Artifacts/StudioValidation/branch-management/`, including the scrolled compact commit state.

Native Windows UI Automation selected the disposable project, opened Changes, set the branch value, invoked create/switch, selected `main`, invoked Switch and confirmed each result from the real repository. Windows still refused foreground keyboard delivery to the agent-started window, and a foreground screenshot could not be captured; the resulting desktop-only image was discarded. Native keyboard interaction therefore remains part of the final migration gate.

### Canonical product map and profile retirement boundary

`PowerForgeStudio.ProductMap.md` is now the canonical implementation map: 13 top-level surfaces, contextual subpages, one shared state contract and eight representative visual references. The earlier 53-page/108-state browser pack remains exploratory design input rather than an implementation checklist. The Projects rail remains selected throughout project-scoped tabs; the workspace GitHub rail entry now opens Activity with its cross-project GitHub filter, while the project GitHub tab remains bound to the selected working copy.

Workspace profiles and custom templates have a shared persisted owner, so WPF retirement no longer treats them as absent or disposable. Avalonia Settings shows every retained profile and custom template, including its workspace, startup behavior and action-chain summary, and opens the machine-local JSON location for export. The records remain intact across preference saves. Their old dashboard queue/action-chain execution is deliberately retired; current workflows use favorites, filters, restored workspace state and per-project Releases. The local catalog inspected during this migration contained no profiles or custom templates, while a focused fixture proves non-empty records remain visible and durable.

Saved portfolio views and quick presets are a separate WPF dashboard contract stored as rows in each workspace `releaseops.db`, not in the workspace-profile JSON. Avalonia does not read, apply or delete those rows. They remain in the existing SQLite database as recoverable legacy data, but their dashboard-specific focus, family and queue-filter semantics are deprecated and receive no replacement execution surface. Removing the WPF project removes the editor for those rows; it does not remove the database or migrate the rows into current favorites and filters.

Four focused Activity, navigation and Settings tests pass. The new Settings render was inspected at 1600 x 1000 with retained profile and template fixtures, and the existing compact render continues to scroll the full page. This closes the only material finding from the independent WPF-retirement review. Headless rendered controls already cover tree navigation, text input, Ctrl+S, Enter and Escape. Native UI Automation covers a real branch mutation; Windows foreground-key injection remains unavailable in the validation host and is recorded as a platform limitation rather than a reason to retain the WPF product.

### WPF host retirement

The WPF application and test projects, 91 tracked files in total, are removed from the repository and solution. This also removes the WPF-only OfficeIMO Markdown renderer, WebView2 and bundled xterm presentation assets. `Build-PowerForgeStudio.ps1`, `Run-PowerForgeStudio.ps1` and `Publish-PowerForgeStudio.ps1` now have one desktop target and no compatibility switches. Historical foundation and regrouping documents are labeled as superseded records; the runbook describes only the supported Avalonia and CLI paths.

The Avalonia application build completed with zero warnings and all 50 Avalonia tests passed. An explicit `-DesktopOnlyTests` validation pass added 472 shared Studio tests passing with one opt-in smoke test skipped. The normal build keeps the complete shared suite enabled. A direct full run reproduced pre-existing failures in the Apple exact-source assertion matrix and one environment-dependent station projection, then stopped after the repeated Apple exception-shape cases were established. Those failures remain a disclosed repository-wide validation gap rather than being hidden from the normal workflow.

Fresh framework-dependent and self-contained `win-x64` packages produced `PowerForgeStudio.exe`; the outputs contain no WPF, WebView2, xterm or WPF Markdown-renderer dependency. The framework-dependent package launched as exactly one native `PowerForge Studio` window. The Windows capture helper again failed to activate that window after one retry, so a final native screenshot is unavailable. The owned process was closed and the machine-local catalog root changed by validation was restored to `C:\Support\GitHub`. Final visual evidence therefore combines the current 50-test rendered suite with the earlier native UI Automation branch mutation against the same Avalonia host.

### Reviewed GitHub item actions

The project GitHub page now supports a bounded action set over the existing authenticated GitHub owner: post a general issue or PR comment, close or reopen an issue, approve a pull request, and request changes. The operator chooses the action and writes any required text on the page, then reviews a separate captured plan before submission. That dialog names the repository and item, displays the observed issue state or full PR head SHA, and shows the exact text that will be sent. Merge and branch deletion stay outside this page because they require the repository settlement workflow.

The shared action service validates the repository slug, item number, action/target combination and 65,536-character text bound. It re-reads the current issue state and rejects PR-shaped issue responses before an issue action, and re-reads the PR before any PR action. A changed state or head stops before mutation. PR reviews also send the captured `commit_id`; authentication remains in the existing environment, GitHub CLI or token-file owner and no credential value enters Studio state, logs or receipts. Preflight responses are bounded to 512 KiB and failures expose sanitized status text without response bodies. A successful mutation status is terminal evidence even when optional response metadata is empty, malformed, oversized or cancelled; Studio will not offer a false retry. Failed post-write refresh is reported separately from the completed action, and the outcome remains visible in the page header even after an issue-list refresh clears selection.

Eleven service tests cover exact-head approval, moved-head rejection before POST, state-checked issue closing, PR-shaped issue rejection, accepted writes with unusable response bodies and invalid action plans. Six GitHub workspace tests cover project/filter races, reviewed PR and issue plans, post-write refresh failures, changed-file revisions and rendered presentation. Wide, compact, confirmation-dialog and failed-refresh outcome renders were inspected under `Artifacts/StudioValidation/github-actions/`. These tests use controlled HTTP and action fakes; no real GitHub issue or pull request was changed.

### Native Windows storage interaction (2026-09-21)

The current Release build launched as one native `PowerForge Studio` window at 1600 × 1030. Windows capture showed the populated project tree and the Storage route with live progress against the 194-repository workspace. A pointer click on Cancel scan stopped the inspection promptly. That check exposed a misleading empty-table message after cancellation; the page now says that inspection was cancelled and offers Refresh inspection. The focused Avalonia test and a second native launch confirmed the corrected rendered state. Both agent-owned validation windows were closed. This is native pointer and capture evidence for the Storage route; it does not establish native keyboard, modal, signing or publishing behavior across the whole application.
