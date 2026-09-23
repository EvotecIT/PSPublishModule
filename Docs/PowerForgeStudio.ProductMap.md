# PowerForge Studio product map

Status: canonical navigation and WPF retirement map.

PowerForge Studio uses one workspace shell. The project tree, document tabs, context panel and output dock stay in place while the center surface changes. The product does not need one mockup for every loading, empty, error and dialog state. Eight representative visual references define the shell and reusable page patterns; runtime tests cover the state matrix.

At compact widths, Details temporarily shows the same context panel in place of the project tree. Projects in the document bar or rail restores the tree; widening the window restores the tree and inspector together.

## Navigation model

The left rail is workspace-wide. Project tabs are scoped to the selected project folder. A rail entry must never silently behave like a project tab.

The tree discovers immediate Git checkouts and folders with a supported PowerForge build contract, including local JSON or PowerShell projects before they have Git metadata. A selected local project keeps Files, Build & Run and release planning available. Changes, History and project GitHub require a Git working copy and are disabled until one exists; workspace Activity still separates local build evidence from GitHub evidence.

| Scope | Entry | Purpose | Main owner |
|---|---|---|---|
| Project | Overview | Identity, detected products, entrypoints, prerequisites and working-copy state | Project overview service |
| Project | Files | Tree, file list, preview/editor and explicit file operations | Explorer services |
| Project | Changes | Branches, status, diff, stage, unstage and commit | Project Git service |
| Project | History | Bounded commit list, changed paths and patch | Project Git service |
| Project | Build & Run | Inspect configuration, plan intent, execute and cancel | Build planning/execution services |
| Project | GitHub | Issues, pull requests, checks, discussion, changed files and explicitly reviewed item actions for this repository | GitHub project services |
| Project | Releases | Prepare, sign, publish, verify, recover and reopen durable receipts; inspect recent GitHub releases and asset downloads | Durable release workflows and GitHub release catalog |
| Workspace | Activity | Cross-project readiness, saved release checkpoints, reviews, CI and schedules needing attention | Activity inventory and durable release journal |
| Workspace | GitHub | The Activity catalog prefiltered to cross-project GitHub evidence | Activity inventory |
| Workspace | Automations | Provider-owned schedules and workflow definitions | Automation inventory |
| Workspace | Storage | Measured working copies, cleanup candidates and guarded removal review | Storage inspection/removal services |
| Workspace | Packages | Public NuGet and PowerShell Gallery versions and downloads with source freshness and warnings | PowerForge.Web ecosystem snapshot consumer |
| Workspace | Connections | Secret-free capability and endpoint evidence for GitHub, registries, Licensing, IntelligenceX and toolchains | Connection inventory |
| Workspace | Settings | Local roots, restore behavior, limits and diagnostics preferences | Local Studio settings |

Projects in the rail returns to the selected project's Overview. It remains highlighted for every project tab. GitHub in the rail opens the cross-project Activity filter; GitHub in the project tab stays bound to the selected working copy.

Releases distinguishes submission from verified availability. WinGet shows a receipt for each attempted package command, including its package ID, version, manifest and command outcome. When Studio captures a recognized `microsoft/winget-pkgs` pull-request URL from WinGetCreate output, the receipt retains a safe Open PR action for reviewing upstream progress. Interactive authentication keeps the console attached for prompts, so Studio cannot capture its PR URL; use the upstream repository to find that submission. A failed command requires external reconciliation before retrying, even when it is the first package attempted. A successful command or open PR does not prove upstream manifest acceptance or catalog availability, so verification remains incomplete until that evidence can be checked.

The title-bar field is a Ctrl+K project jump over the loaded workspace catalog. It shows bounded matching projects without changing the sidebar tree while typing; choosing one clears any tree filter and opens that project's Overview. The sidebar field only filters project names in the tree. The title-bar field does not search files, commits, issues or remote providers.

The Changed tree chip performs an explicit read-only local Git scan over primary checkouts and registered worktrees. It is unavailable when the catalog contains only local build folders without Git. The completed observation filters project groups and shows its UTC time plus any Git working copies that could not be inspected. A refresh keeps the previous result visible until the new scan completes. It does not fetch remotes, measure workspace storage or classify a worktree as safe to remove.

Archiving a project is reversible machine-local tree organization. It records the repository root in the existing workspace JSON and moves the project under a counted Archived group. It does not move, delete, clean or rewrite repository content. Restoring returns the project to the active groups and preserves its favorite state.

## Subpages and overlays

These are states within an owning surface, not additional top-level pages:

- Files: document editor, unsaved-change choice, create/copy/move/rename, recoverable deletion and recovery.
- Overview: detected build configuration, `Build-Project.ps1` and solution files open directly in Files for inspection. Opening a row never executes it; a missing or redirected file requires a fresh overview before use.
- Changes: local branch selector, new branch field, change list, diff and commit form.
- Build & Run: discovered contracts, reviewed plan, running progress, cancellation, artifacts and output. Optional [project tasks](PowerForgeStudio.ProjectTasks.md) expose explicit PowerShell or executable commands with their own reviewed selection and result.
- GitHub: issues, pull requests, discussion/checks, changed-file patch, action draft and reviewed-action confirmation.
- Releases: prepared plan, signing, destination review, publication, verification, saved history for the selected working copy and local-save recovery. Remote NuGet verification can use the saved package ID/version when the local archive is gone; this checks the registry endpoint, not the original archive bytes. Local-feed verification compares package bytes with the approved SHA-256, and fails if both the source archive and approved digest are unavailable. GitHub publication saves the full expected asset name and byte-size inventory; verification reads the tagged release from GitHub and compares every expected asset. This does not prove identical bytes for same-size replacements, and older receipts without an inventory stay unverified. Module-owned package receipts route to NuGet verification when they contain a package identity, or to GitHub release verification when they identify a release URL; per-project GitHub publication retains each result and its release URL separately. Unknown receipt shapes remain unverified. Publication receipts, saved progress and visible diagnostics omit URL credentials and query values; older saved release evidence is scrubbed on database upgrade. To verify a private project-build or JSON module package feed whose URL carried those values, Studio reloads the current matching configuration for the in-memory probe. Ambiguous module lanes, missing configuration and changed destinations stay unverified. Verification does not execute a script-backed module build to recover a private feed address. A multi-project release can appear in each participating project's history, but its shared progress is shown only in the full journal because those events cannot be assigned safely to one project.
- Published on GitHub: an explicit refresh reads up to ten recent releases for the selected working copy. Each row shows its GitHub release assets and reported download counts. This is a dated, read-only snapshot of asset downloads; it excludes source archives, NuGet, PowerShell Gallery and licensing activity. Failed refresh leaves the previous snapshot visible with a stale-data notice.
- Activity: recent saved release states appear under Releases alongside readiness signals. Selecting a journal row can open its exact checkpoint in the project's Releases tab when that working copy remains available inside the workspace. Completed rows are history, while failed or waiting rows remain attention signals. The bounded cross-project GitHub scan includes ordinary repositories without a PowerForge build contract. It checks non-archived favorites first, then repositories with local readiness attention or changes, and places archived projects last. Release readiness still applies only to repositories with a detected build contract. Deferred repositories remain explicitly partial; open a project's GitHub tab to inspect it directly.
- Automations: a selected GitHub schedule can open its current local `.github/workflows` file in the owning project's Files view after path and link checks. Windows Task Scheduler rows retain their provider-owned evidence without pretending a local source file exists.
- Storage: filters, selected-row evidence, removal review and broken-registration prune review. A separate Other folders filter lists immediate directories under the workspace's `_worktrees` container that do not appear in the scanned Git registrations. It measures their logical size without following linked directories, distinguishes independent Git checkouts, Git-linked folders and folders without Git metadata, and offers an open-folder action only. Git-linked folders show whether their referenced administrative directory is currently present or missing; a missing target alone does not establish that their contents are disposable. If any Git registration list fails or is incomplete, the working-copy inventory says it is partial and other folders remain unclassified until a successful rescan. These rows are not classified as dead or safe to remove and cannot enter the registered-worktree removal or prune flows.
- Packages: NuGet/Gallery filters, ID search, selected package evidence and an explicit provider-page handoff. A public NuGet or PSGallery release receipt can open its package ID in this view. The receipt keeps the exact ID and version captured before publication; the public snapshot reports only its latest known version and package-wide downloads. If the source retained a registry's values after a failed refresh, its cards, combined total and selected-package evidence say so; the snapshot generation time is not presented as the retained values' observation time. Use the saved verification receipt to assess delivery of the exact version. Private feeds and licensing are outside this snapshot.
- Connections: catalog evidence and provider-owned configuration handoff. The selected Licensing row can open the exact Evotec Control HTTPS origin in the system browser; browser authentication remains with the portal. Studio does not pass a protected profile, token or credential value. Authenticated licensing inventories and download analytics remain outside this connection check.
- Settings: recent workspace roots can be forgotten without deleting their directory or saved explorer state. The active root and any root used by a retained profile are protected. Unsaved preference drafts must be resolved before a catalog change.

Dialogs are used only when the operator must confirm a target, resolve a collision, choose what happens to unsaved work, or authorize a destructive/external effect. GitHub comments, issue state changes and PR reviews show the captured item state or exact PR head before submission. Merge and branch deletion remain in the repository settlement workflow. A successful read or simple navigation does not need a dialog.

## Eight visual references

One wide and one compact runtime render may be captured from the same reference when responsive behavior matters. The [visual review pack](PowerForgeStudio.VisualReview.md) contains the eight images and records image-model corrections. This is the complete visual set; it is intentionally smaller than the superseded 53-page/108-state browser pack.

1. Workspace shell with project tree, working copies, files and document tabs.
2. Project overview with identity, signals, entrypoints and prerequisites.
3. Changes and history pattern with branch controls, lists and diff.
4. Build & Run with reviewed plan, live stages, cancellation and artifacts.
5. Releases with destination review, progress, receipts and saved history.
6. Project GitHub with issue/PR/check context and changed files.
7. Workspace catalog pattern represented by Activity; Automations, Packages and Connections reuse it.
8. Storage review with measured candidates, evidence inspector and guarded removal.

Screenshots demonstrate hierarchy, density, spacing, typography, icons and responsive composition. They do not freeze sample repository names, versions, statuses or invented data into product requirements.

## Shared state contract

Every data surface implements the states that apply to it:

| State | Required behavior |
|---|---|
| Initial | Explain what will be read or what selection is required. |
| Loading | Keep the current route and show the active read or operation. |
| Empty | State that the read succeeded and no matching item exists. |
| Ready | Show observation time, source and actionable evidence. |
| Refreshing | Preserve stale evidence visibly until the replacement snapshot is complete. |
| Partial | Identify the unavailable provider or omitted bounded results. |
| Unauthorized | Name the missing provider capability without implying an empty result. |
| Failed | Keep drafts and prior evidence; show a sanitized retryable error. |
| Cancelled | Distinguish cancellation from failure and retain completed evidence. |
| Protected | Block navigation while a mutation is active or evidence is unsaved, with the exact reason. |

Selection, drafts, filters and scroll position survive refresh when the referenced identity still exists. A project switch cancels or versions old reads. Mutations capture their original working-copy root and cannot apply their completion state to a newly selected project.

## WPF capability disposition

| WPF capability | Avalonia disposition | Decision |
|---|---|---|
| Project/file explorer | Replaced by the persistent project tree, Files surface and shared file operations | Remove WPF copy |
| Git status, diff, stage and commit | Replaced; branch create/switch now uses the same shared Git owner | Remove WPF copy |
| Release portfolio dashboard | Useful attention outcomes are covered by Activity, project Overview and Releases | Do not migrate dashboard shell |
| Workspace profiles and custom templates | Existing JSON records remain visible in Settings as read-only compatibility data; Avalonia favorites, filters and restored workspace state cover the daily workflow | Preserve data, retire execution |
| Saved portfolio views and quick presets | These are legacy dashboard filter rows, separate from profiles. Existing rows remain untouched in each workspace `releaseops.db`; Avalonia does not apply or delete them | Preserve SQLite rows, retire the feature |
| Family lane boards and broad global queue buttons | Project tree groups working copies; per-project durable Releases owns execution | Do not migrate |
| Release stations and receipts | Replaced by Connections plus durable Releases progress/history | Remove WPF copy |
| Embedded ConPTY/WebView2 terminal | Windows-only presentation with a private terminal stack | Do not port; keep reviewed execution and output in Studio, add an external-terminal handoff only if daily use proves it necessary |
| Markdown/WebView previews | Files and GitHub use Avalonia-native bounded presentation and external handoff | Remove WPF copy |
| WPF-only view-model tests | Current contracts have moved to Domain/Orchestrator or Avalonia tests; retired dashboard profile commands are intentionally reduced to a read-only compatibility view | Delete with WPF host after the retirement gate |

## Retirement gate

The WPF source can be removed when all of the following are true:

- [x] Avalonia is the default build, run and publish target.
- [x] Project/file management, Git changes, build, release, GitHub, activity, schedules, storage, connections and settings have working Avalonia surfaces.
- [x] Branch creation and switching are available without returning to WPF.
- [x] Persisted workspace profiles and custom templates remain visible and exportable through their machine-local JSON; Avalonia does not execute the retired queue/action-chain contract.
- [x] Legacy saved portfolio-view rows have an explicit disposition: retain them in the workspace SQLite database without applying, migrating or deleting them.
- [x] Wide and compact renders cover the eight reference patterns through representative pages.
- [x] Native Windows UI Automation can navigate the tree and invoke a real branch workflow.
- [x] Headless rendered controls cover keyboard tree navigation, text input, save, Enter and Escape; native UI Automation covers a real mutation workflow. Foreground keyboard injection is unavailable in the current validation host and is recorded as a platform validation limit rather than a WPF dependency.
- [x] Build/run/publish scripts and documentation no longer offer the WPF compatibility host.
- [x] WPF projects, tests and framework-only package assets are removed from the solution and repository.
- [x] The final Avalonia package is rebuilt and launched after removal. The native window is observable through process discovery; final capture activation remains an explicit validation-host limitation.

An embedded terminal and the old global queue shell are not retirement blockers. Retained portfolio-profile records remain available in Settings and in the machine-local catalog, while their dashboard-specific execution behavior is intentionally excluded from the replacement product.
