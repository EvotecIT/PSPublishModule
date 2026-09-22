# PowerForge Studio visual review

This is the complete concept set for the Avalonia workspace: eight representative page patterns, not a mockup for every route, dialog, loading state, or viewport. The [product map](PowerForgeStudio.ProductMap.md) defines navigation and behavior. These images define visual hierarchy, spacing, typography, color, icons, tree density, and panel composition. All repository names, counts, dates, versions, paths, statuses, and output in the images are illustrative.

| Pattern | Concept | What to carry into the app |
|---|---|---|
| Workspace and files | [01](Assets/PowerForgeStudio/01-workspace.png) | Persistent project/working-copy tree, file list and preview, tabs, inspector, output dock |
| Project overview | [02](Assets/PowerForgeStudio/02-overview.png) | Project identity, detected entrypoints, prerequisites and recent evidence in compact cards |
| Changes and history | [03](Assets/PowerForgeStudio/03-changes-history.png) | Branch context, changed paths, readable diff, explicit stage and commit workflow |
| Build & Run | [04](Assets/PowerForgeStudio/04-build-run.png) | Reviewed source configuration, build-only intent, stages, artifacts and output |
| Releases | [05](Assets/PowerForgeStudio/05-releases.png) | Prepare, sign, destination review, publish, verify and saved receipts |
| Project GitHub | [06](Assets/PowerForgeStudio/06-project-github.png) | Issues, PRs, checks, discussion, changed files and draft-first actions |
| Workspace catalog | [07](Assets/PowerForgeStudio/07-activity.png) | Cross-project activity list and evidence inspector; Automations and Connections reuse the catalog pattern |
| Storage review | [08](Assets/PowerForgeStudio/08-storage.png) | Measured working copies, candidate evidence, guarded review and output |

## Concept screens

### 01 Workspace and files

![Workspace and files concept](Assets/PowerForgeStudio/01-workspace.png)

### 02 Project overview

![Project overview concept](Assets/PowerForgeStudio/02-overview.png)

### 03 Changes and history

![Changes and history concept](Assets/PowerForgeStudio/03-changes-history.png)

### 04 Build and Run

![Build and Run concept](Assets/PowerForgeStudio/04-build-run.png)

### 05 Releases

![Releases concept](Assets/PowerForgeStudio/05-releases.png)

### 06 Project GitHub

![Project GitHub concept](Assets/PowerForgeStudio/06-project-github.png)

### 07 Workspace Activity

![Workspace Activity concept](Assets/PowerForgeStudio/07-activity.png)

### 08 Storage review

![Storage review concept](Assets/PowerForgeStudio/08-storage.png)

The shared visual system is a navy 70 px rail and title band, white workspace, restrained blue selection, thin blue-gray dividers, compact vector icons, a roughly 325 px tree, contextual inspector, and dark output dock. Selected repository, selected working copy, selected file, and selected workspace route need distinct indicators. Keep controls that perform an action visually different from read-only status. Wide and compact layouts use the same design system; the inspector or preview may collapse when space is limited.

## Corrections before implementation

- The concepts show `Schedules` in the rail; the implemented workspace route is **Automations**. The project tab is **Build & Run**, while **Releases** is its separate project tab. Follow the product map for route names.
- The top search and tree filter must describe their actual search scope. A project-only filter must not claim to search commits, issues, or file contents.
- Overview detection, package versions, tool readiness and recent activity come from observed sources. The Overview image does not authorize defaulting these to green or inventing a release state.
- Changes must show staged and unstaged state accurately. Any discard operation needs its own guarded workflow. A commit always uses the selected working copy and explicit user-authored message.
- Build & Run keeps Build only separate from Publish release and Retry destinations. Retry uses verified artifacts where supported; it does not silently rebuild. Artifact types come from the reviewed plan, not from the pictured example.
- The Releases image mixes illustrative package names and claims readiness while its sample output says the build has not started. The app must derive readiness from its durable checkpoints. WinGet and Microsoft Store are distinct destinations. Never display credential values.
- GitHub actions use the captured repository/item and exact PR head. A review or comment opens a draft and explicit confirmation. The picture's PR, checks, and reviewer controls are illustrative, not proof of current provider capability.
- Activity totals and states are observed provider evidence with time and source, not synthesized success claims. Automations and Connections may have unavailable or partial provider states.
- Storage sizes are logical measurements, not guaranteed reclaimable space. Broken references belong in an Inspect/Repair group. Refresh remote, active-use and retained-artifact evidence before any removal.

## Review status

- [x] Eight representative concepts have one coherent shell, tree, colors and icon treatment.
- [x] The 53-page/108-state browser gallery is superseded as an implementation checklist.
- [x] Image-model inaccuracies are called out above; the product map remains the behavior contract.
- [x] A first shared Avalonia styling pass updated the title band, project filter order, typography, chips, inspector path density and output dock; fresh wide and compact Overview, Activity and Storage renders were inspected.
- [x] Storage and Activity evidence rows now pair shared vector state cues with their text labels; Storage summary cards use the same navigation icon system.
- [x] The project tree prioritizes Build and Docs at working-copy roots; Storage evidence is aligned in the inspector, and compact Storage can scroll through the complete table above a shorter output dock.
- [x] Activity source cards now fit on one row; compact Activity keeps provider state visible while showing the first actionable rows without scrolling.
- [x] Overview names the page separately from the selected project and keeps the full working-copy path copyable without letting it dominate compact layout.
- [x] Releases now shows the captured checkpoint state above a compact five-stage guide, keeps artifact paths secondary, and emphasizes the action available at that checkpoint.
- [x] Changes uses the right panel for selected-file Git evidence and the applicable stage action instead of repeating generic working-copy labels.
- [ ] Compare current Avalonia wide and compact renders for each pattern against this set.
- [ ] Close the remaining shell, density, inspector and output-dock differences in the running application.
- [ ] Capture final native Windows and headless evidence after the last visual change.

The 1600 × 1000 renders have now been compared with all eight concepts. The runtime captures are test fixtures, so their project names and counts differ from the illustrative designs. Compact Release, Activity and Storage were also inspected; the other compact routes still need a final side-by-side pass.

| Pattern | Current rendering | Remaining visual work |
|---|---|---|
| Workspace and files | The tree, file list, preview, tabs and output dock use the shared shell. | Give the selected file a more useful context panel; avoid repeating a long path in several places. |
| Overview | Identity, observed products, entrypoints and prerequisites form readable cards. | Tighten card density and reduce the generic context panel. Do not invent recent activity or tool health. |
| Changes and history | Branch actions, changed paths, patch, commit form and selected-change context are present. | Improve patch readability and finish the compact diff/commit pass. |
| Build & Run | Validated contracts and reviewed local actions are visible before execution. | Make source, intent, progress and artifacts easier to scan without hiding the trust boundary or raw-output access. |
| Releases | The captured state, available action and artifacts are clear at wide and compact sizes. | Group publication controls and receipts by stage; do not imply that a destination was published until a receipt exists. |
| Project GitHub | Issues, PR details, checks, files and reviewed actions are functional. | Use the context panel for selected item evidence, and reduce the empty space in the list/detail split. |
| Activity catalog | Source availability, counts, filters, actionable rows and evidence remain visible. | Keep the compact inspector readable when provider explanations are long. |
| Storage | Measured sizes, candidate rows and guarded evidence align closely with the reference. | Keep long paths and safety explanations readable at compact width. |

The shared right panel is still generic on Files, Overview, Build & Run, Releases and project GitHub, even when the center surface already has selected-item detail. Preserve source and safety information while changing its placement; do not fill the panel with illustrative values or controls that have no owner.
